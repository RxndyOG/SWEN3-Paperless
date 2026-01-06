using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaperlessREST.Data;
using PaperlessREST.Models;
using PaperlessREST.Services;
using System.Text.Json;
using Paperless.Contracts;
using Paperless.Contracts.SharedServices;
using System.Diagnostics;
using Paperless.REST.Models;
using System.Runtime.InteropServices.Marshalling;

namespace PaperlessREST.Controllers;

public interface IDocumentsController
{
    Task<IActionResult> GetAll();
    Task<IActionResult> Upload([FromForm] IFormFile file);
    Task<IActionResult> GetDocById(int id);
    Task<IActionResult> DeleteDocById(int id);
    Task<IActionResult> ElasticSearch(string query);
}



[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase, IDocumentsController
{
    private readonly AppDbContext _db;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IObjectStorage _storage;
    private readonly RestQueueService _mq;
    private readonly IElasticService _elastic;

    public DocumentsController(
        AppDbContext db,
        ILogger<DocumentsController> logger,
        IObjectStorage storage,
        RestQueueService mq,
        IElasticService elastic)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
        _mq = mq;
        _elastic = elastic;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var docs = await _db.Documents
                .AsNoTracking()
                .Select(d => new
                {
                    d.Id,
                    d.FileName,
                    d.CreatedAt,
                    d.CurrentVersionId,
                    Current = _db.DocumentVersions
                        .Where(v => v.Id == d.CurrentVersionId)
                        .Select(v => new
                        {
                            v.Id,
                            v.VersionNumber,
                            v.SizeBytes,
                            v.Tag,
                            v.ChangeSummary,
                            v.SummarizedContent
                        })
                        .FirstOrDefault()
                })
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();
            return Ok(docs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve documents");
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve documents");
        }
    }

    [HttpGet("versions/{versionId:int}/ocr")]
    public async Task<IActionResult> GetVersionOcr(int versionId)
    {
        var v = await _db.DocumentVersions
            .AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new { x.Id, OcrText = x.Content })
            .FirstOrDefaultAsync();

        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpGet("{id:int}/versions")]
    public async Task<IActionResult> GetVersions(int id)
    {
        var doc = await _db.Documents.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.CurrentVersionId,
                Versions = d.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new
                    {
                        v.Id,
                        v.VersionNumber,
                        v.SizeBytes,
                        v.ContentType,
                        v.Tag,
                        v.SummarizedContent,
                        v.ChangeSummary,
                        v.DiffBaseVersionId
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (doc == null) return NotFound();
        return Ok(doc);
    }

    [HttpGet("versions/{versionId:int}")]
    public async Task<IActionResult> GetVersion(int versionId)
    {
        var v = await _db.DocumentVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new
            {
                x.Id,
                x.DocumentId,
                x.VersionNumber,
                x.Tag,
                x.SummarizedContent,
                x.ChangeSummary,
                OcrText = x.Content
            })
            .FirstOrDefaultAsync();

        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpGet("versions/{versionId:int}/file")]
    public async Task<IActionResult> DownloadVersion(int versionId)
    {
        var v = await _db.DocumentVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new { x.ObjectKey, x.ContentType })
            .FirstOrDefaultAsync();

        if (v == null) return NotFound();

        var stream = await _storage.GetObjectAsync(v.ObjectKey);
        return File(stream, v.ContentType, fileDownloadName: $"version-{versionId}.pdf");
    }

    [HttpPut("{id:int}/currentVersion/{versionId:int}")]
    public async Task<IActionResult> SetCurrentVersion(int id, int versionId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound();

        var exists = await _db.DocumentVersions.AnyAsync(v => v.Id == versionId && v.DocumentId == id);
        if (!exists) return BadRequest("Version does not belong to document.");

        doc.CurrentVersionId = versionId;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    //Accept only PDF files. Stores the file in MinIO and metadata in DB.
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        //Enforce PDF only. Accept if content-type indicates PDF or filename ends with .pdf
        var isPdf = file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
                        || file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf) return BadRequest("Only PDF files are accepted.");

        var fileName = Path.GetFileName(file.FileName);
        var objectKey = $"{Guid.NewGuid():N}_{fileName}";

        try
        {
            _logger.LogInformation("Uploading PDF {ObjectKey} (name: {FileName}, size: {Size})", objectKey, file.FileName, file.Length);

            await using var stream = file.OpenReadStream();
            //Ensure bucket exists and put object
            await _storage.PutObjectAsync(stream, objectKey, "application/pdf");

            await using var tx = await _db.Database.BeginTransactionAsync();

            var doc = await _db.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.FileName == fileName);

            if (doc == null)
            {
                doc = new Document
                {
                    FileName = fileName,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Documents.Add(doc);
                await _db.SaveChangesAsync();
            }

            var nextVersionNumber = (doc.Versions.Count == 0)
                ? 1
                : doc.Versions.Max(v => v.VersionNumber) + 1;

            int? baseVersionId = doc.CurrentVersionId;

            var newVersion = new DocumentVersion
            {
                DocumentId = doc.Id,
                VersionNumber = nextVersionNumber,
                DiffBaseVersionId = baseVersionId,
                ObjectKey = objectKey,
                ContentType = "application/pdf",
                SizeBytes = file.Length
            };

            _db.DocumentVersions.Add(newVersion);
            await _db.SaveChangesAsync();

            doc.CurrentVersionId = newVersion.Id;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            try
            {
                var msg = new VersionPipelineMessage(
                    DocumentId: doc.Id,
                    DocumentVersionId: newVersion.Id,
                    VersionNumber: newVersion.VersionNumber,
                    DiffBaseVersionId: baseVersionId,
                    Bucket: "documents",
                    ObjectKey: objectKey,
                    FileName: doc.FileName!,
                    ContentType: "application/pdf"
                    );

                _mq.PublishTo(JsonSerializer.Serialize(msg), QueueNames.Documents);
                _logger.LogInformation("Published upload message for document id {Id} to {Queue}", doc.Id, QueueNames.Documents);
            }
            catch (Exception mqEx)
            {
                _logger.LogWarning(mqEx, "Failed to publish message for document id {Id}", doc.Id);
            }

            _logger.LogInformation("Uploaded document saved (id: {Id}, key: {Key})", doc.Id, objectKey);
            return CreatedAtAction(nameof(GetDocById), new { id = doc.Id }, new
            {
                doc.Id,
                doc.FileName,
                CurrentVersionId = doc.CurrentVersionId,
                VersionCreated = newVersion.Id,
                newVersion.VersionNumber
            });
        }
        catch (Minio.Exceptions.MinioException mex)
        {
            _logger.LogError(mex, "Object storage error while uploading {ObjectKey}", objectKey);
            return StatusCode(StatusCodes.Status502BadGateway, "Object storage error");
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error while saving metadata for {ObjectKey}", objectKey);

            //try to cleanup stored object
            try
            {
                await _storage.RemoveObjectAsync(objectKey);
                _logger.LogInformation("Rolled back stored object {ObjectKey} after DB failure", objectKey);
            }
            catch (Exception cleanEx)
            {
                _logger.LogWarning(cleanEx, "Failed to remove object {ObjectKey} after DB failure", objectKey);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "Database error while saving document metadata");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while uploading document {FileName}", file.FileName);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error while uploading document");
        }
    }


    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDocById(int id)
    {
        try
        {
            var doc = await _db.Documents
                .AsNoTracking()
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null)
            {
                _logger.LogInformation("Document {Id} not found", id);
                return NotFound();
            }
                return Ok(new
                {
                    doc.Id,
                    doc.FileName,
                    doc.CreatedAt,
                    doc.CurrentVersionId,
                    Versions = doc.Versions
                        .OrderByDescending(v => v.VersionNumber)
                        .Select(v => new
                            {
                            v.Id,
                            v.VersionNumber,
                            v.DiffBaseVersionId,
                            v.SizeBytes,
                            v.Tag,
                            v.SummarizedContent,
                            v.ChangeSummary
                            })
                        });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve document");
        }
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> DownloadCurrent(int id)
    {
        try
        {
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null)
            {
                _logger.LogInformation("Document {Id} not found for download", id);
                return NotFound();
            }

            var version = await _db.DocumentVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == doc.CurrentVersionId);

            if (version == null) return NotFound("Current Version not found.");

            var stream = await _storage.GetObjectAsync(version.ObjectKey);

            return File(stream, "application/pdf", doc.FileName);
        }
        catch (Minio.Exceptions.MinioException mex)
        {
            _logger.LogError(mex, "Object storage error while downloading document {Id}", id);
            return StatusCode(StatusCodes.Status502BadGateway, "Object storage error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while downloading document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error");
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDocById(int id)
    {
        try
        {
            var doc = await _db.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null)
            {
                _logger.LogInformation("Document {Id} not found for deletion", id);
                return NotFound();
            }

            var failedKeys = new List<string>();

            foreach (var v in doc.Versions)
            {
                if (string.IsNullOrWhiteSpace(v.ObjectKey))
                    continue;

                try
                {
                    await _storage.RemoveObjectAsync(v.ObjectKey);
                    _logger.LogInformation(
                        "Removed object {Key} from storage for document {DocId} version {VersionId}",
                        v.ObjectKey, id, v.Id);
                }
                catch (Exception ex)
                {
                    failedKeys.Add(v.ObjectKey);
                    _logger.LogError(ex,
                        "Failed to remove object {Key} for document {DocId} version {VersionId}",
                        v.ObjectKey, id, v.Id);
                }
            }

            if (failedKeys.Count > 0)
            {
                return StatusCode(StatusCodes.Status502BadGateway,
                    $"Failed to remove {failedKeys.Count} object(s) from storage. " +
                    $"Aborting DB deletion to avoid orphaned objects. Keys: {string.Join(", ", failedKeys)}");
            }

            await using var tx = await _db.Database.BeginTransactionAsync();

            // ✅ break FK: Document -> CurrentVersion
            doc.CurrentVersionId = null;
            await _db.SaveChangesAsync();

            _db.DocumentVersions.RemoveRange(doc.Versions);
            _db.Documents.Remove(doc);

            var changes = await _db.SaveChangesAsync();
            await tx.CommitAsync();

            if (changes == 0)
            {
                _logger.LogWarning("No DB changes when deleting document {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to delete document record");
            }

            _logger.LogInformation("Deleted document {Id} and all versions ({Count})", id, doc.Versions.Count);
            return Ok("successfully removed");
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error while deleting document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Database error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deleting document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error");
        }
    }



    [HttpGet("search")]
    public async Task<IActionResult> ElasticSearch([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Query parameter is required.");

        try
        {
            var result = await _elastic.SearchAsync(query);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Elasticsearch search failed for query: {Query}", query);
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }
}