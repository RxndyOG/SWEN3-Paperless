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
            var docs = await _db.Documents.AsNoTracking().OrderByDescending(d => d.CreatedAt).ToListAsync();
            return Ok(docs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve documents");
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve documents");
        }
    }

    //Accept only PDF files (enforced). Stores the file in MinIO and metadata in DB.
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file is null)
            return BadRequest("File is required.");

        if (file.Length == 0)
            return BadRequest("File is empty.");

        //Enforce PDF only. Accept if content-type indicates PDF or filename ends with .pdf
        var isPdfContentType = string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
        var hasPdfExtension = file.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

        if (!isPdfContentType && !hasPdfExtension)
        {
            _logger.LogWarning("Rejected upload: non-PDF file {FileName} with content-type {ContentType}", file.FileName, file.ContentType);
            return BadRequest("Only PDF files are accepted.");
        }

        var objectKey = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";

        try
        {
            _logger.LogInformation("Uploading PDF {ObjectKey} (name: {FileName}, size: {Size})", objectKey, file.FileName, file.Length);

            await using var stream = file.OpenReadStream();
            // Ensure bucket exists and put object
            await _storage.PutObjectAsync(stream, objectKey, "application/pdf");

            var doc = new Document
            {
                FileName = Path.GetFileName(file.FileName)!,
                ObjectKey = objectKey,
                ContentType = "application/pdf", //enforce PDF type in metadata
                SizeBytes = file.Length,
                CreatedAt = DateTime.UtcNow
            };

            _db.Documents.Add(doc);
            await _db.SaveChangesAsync();

            try
            { 
                var msg = new UploadedDocMessage(
                    doc.Id,
                    Bucket: "documents", //or read from config
                    objectKey,
                    doc.FileName!,
                    doc.ContentType ?? "application/pdf");

                _mq.PublishTo(JsonSerializer.Serialize(msg), QueueNames.Documents);
                _logger.LogInformation("Published upload message for document id {Id} to {Queue}", doc.Id, QueueNames.Documents);
            }
            catch (Exception mqEx)
            {
                _logger.LogWarning(mqEx, "Failed to publish message for document id {Id}", doc.Id);
            }

            _logger.LogInformation("Uploaded document saved (id: {Id}, key: {Key})", doc.Id, objectKey);
            return CreatedAtAction(nameof(GetDocById), new { id = doc.Id }, doc);
        }
        catch (Minio.Exceptions.MinioException mex)
        {
            _logger.LogError(mex, "Object storage error while uploading {ObjectKey}", objectKey);
            return StatusCode(StatusCodes.Status502BadGateway, "Object storage error");
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Database error while saving metadata for {ObjectKey}", objectKey);

            // try to cleanup stored object
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
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null)
            {
                _logger.LogInformation("Document {Id} not found", id);
                return NotFound();
            }

            try
            {
                var url = _storage.GetPresignedGetUrl(doc.ObjectKey, TimeSpan.FromMinutes(10));
                var result = new
                {
                    doc.Id,
                    doc.FileName,
                    doc.ContentType,
                    doc.SizeBytes,
                    doc.CreatedAt,
                    doc.ObjectKey,
                    DownloadUrl = url
                };
                return Ok(result);
            }
            catch (Exception urlEx)
            {
                _logger.LogWarning(urlEx, "Failed to generate presigned URL for object {Key}", doc.ObjectKey);
                return Ok(doc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve document");
        }
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        try
        {
            var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
            if (doc == null)
            {
                _logger.LogInformation("Document {Id} not found for download", id);
                return NotFound();
            }

            var stream = await _storage.GetObjectAsync(doc.ObjectKey);
            return File(stream, doc.ContentType ?? "application/pdf", doc.FileName);
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
            var doc = await _db.Documents.FindAsync(id);
            if (doc == null)
            {
                _logger.LogInformation("Document {Id} not found for deletion", id);
                return NotFound();
            }

            try
            {
                await _storage.RemoveObjectAsync(doc.ObjectKey);
                _logger.LogInformation("Removed object {Key} from storage for document {Id}", doc.ObjectKey, id);
            }
            catch (Exception storageEx)
            {
                _logger.LogError(storageEx, "Failed to remove object {Key} from storage for document {Id}", doc.ObjectKey, id);
                return StatusCode(StatusCodes.Status502BadGateway, "Failed to remove object from storage");
            }

            _db.Documents.Remove(doc);
            var changes = await _db.SaveChangesAsync();
            if (changes == 0)
            {
                _logger.LogWarning("No DB changes when deleting document {Id}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to delete document record");
            }

            _logger.LogInformation("Deleted document {Id} and its object {Key}", id, doc.ObjectKey);
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