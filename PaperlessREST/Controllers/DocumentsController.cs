using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PaperlessREST.Services.Documents;

namespace PaperlessREST.Controllers;

public interface IDocumentsController
{
    Task<IActionResult> GetAll();
    Task<IActionResult> Upload([FromForm] IFormFile file);
    Task<IActionResult> GetDocById(int id, CancellationToken ct);
    Task<IActionResult> DeleteDocById(int id);
    Task<IActionResult> ElasticSearch(string query);
}

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase, IDocumentsController
{
    private readonly IDocumentService _docs;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentService docs, ILogger<DocumentsController> logger)
    {
        _docs = docs;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        _logger.LogInformation("GET /api/documents requested");
        var res = await _docs.GetAllAsync(HttpContext.RequestAborted);
        _logger.LogInformation("GET /api/documents returned {Count} documents", res?.Count() ?? 0);
        return Ok(res);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDocById(int id, CancellationToken ct)
    {
        _logger.LogInformation("GET /api/documents/{Id} requested", id);

        var doc = await _docs.GetByIdAsync(id, ct);

        if (doc is null)
        {
            _logger.LogWarning("Document {Id} not found", id);
            return NotFound();
        }

        _logger.LogInformation("Document {Id} returned successfully", id);
        return Ok(doc);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            _logger.LogWarning("Upload rejected: file was null or empty");
            return BadRequest("File is required.");
        }

        var isPdf =
            file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
            file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        if (!isPdf)
        {
            _logger.LogWarning(
                "Upload rejected: not a PDF. FileName={FileName}, ContentType={ContentType}",
                file.FileName, file.ContentType);
            return BadRequest("Only PDF files are accepted.");
        }

        _logger.LogInformation(
            "Upload accepted: FileName={FileName}, Size={SizeBytes}, ContentType={ContentType}",
            file.FileName, file.Length, file.ContentType);

        try
        {
            var result = await _docs.UploadAsync(file, HttpContext.RequestAborted);

            _logger.LogInformation(
                "Upload completed: DocumentId={DocumentId}, CurrentVersionId={CurrentVersionId}, VersionNumber={VersionNumber}",
                result.DocumentId, result.CurrentVersionId, result.VersionNumber);

            return CreatedAtAction(
                nameof(GetDocById),
                new { id = result.DocumentId },
                new
                {
                    id = result.DocumentId,
                    fileName = result.FileName,
                    currentVersionId = result.CurrentVersionId,
                    versionCreated = result.VersionCreatedId,
                    versionNumber = result.VersionNumber
                });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Upload canceled by client. FileName={FileName}", file.FileName);
            return StatusCode(StatusCodes.Status400BadRequest, "Request was canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during upload. FileName={FileName}", file.FileName);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error while uploading document");
        }
    }

    [HttpGet("{id:int}/versions")]
    public async Task<IActionResult> GetVersions(int id)
    {
        _logger.LogInformation("GET /api/documents/{Id}/versions requested", id);

        var doc = await _docs.GetVersionsAsync(id, HttpContext.RequestAborted);
        if (doc is null)
        {
            _logger.LogWarning("Versions requested but document {Id} not found", id);
            return NotFound();
        }

        _logger.LogInformation("Versions for document {Id} returned successfully", id);
        return Ok(doc);
    }

    [HttpGet("versions/{versionId:int}")]
    public async Task<IActionResult> GetVersion(int versionId)
    {
        _logger.LogInformation("GET /api/documents/versions/{VersionId} requested", versionId);

        var v = await _docs.GetVersionAsync(versionId, HttpContext.RequestAborted);
        if (v is null)
        {
            _logger.LogWarning("Version {VersionId} not found", versionId);
            return NotFound();
        }

        return Ok(v);
    }

    [HttpGet("versions/{versionId:int}/ocr")]
    public async Task<IActionResult> GetVersionOcr(int versionId)
    {
        _logger.LogInformation("GET /api/documents/versions/{VersionId}/ocr requested", versionId);

        var v = await _docs.GetVersionOcrAsync(versionId, HttpContext.RequestAborted);
        if (v is null)
        {
            _logger.LogWarning("OCR requested but version {VersionId} not found", versionId);
            return NotFound();
        }

        return Ok(v);
    }

    [HttpGet("versions/{versionId:int}/file")]
    public async Task<IActionResult> DownloadVersion(int versionId)
    {
        _logger.LogInformation("GET /api/documents/versions/{VersionId}/file requested", versionId);

        var res = await _docs.DownloadVersionAsync(versionId, HttpContext.RequestAborted);
        if (res is null)
        {
            _logger.LogWarning("Download requested but version {VersionId} not found", versionId);
            return NotFound();
        }

        _logger.LogInformation("Download version {VersionId} served as {DownloadName}", versionId, res.Value.DownloadName);
        return File(res.Value.Stream, res.Value.ContentType, res.Value.DownloadName);
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> DownloadCurrent(int id)
    {
        _logger.LogInformation("GET /api/documents/{Id}/download requested", id);

        var res = await _docs.DownloadCurrentAsync(id, HttpContext.RequestAborted);
        if (res is null)
        {
            _logger.LogWarning("Download requested but document {Id} not found", id);
            return NotFound();
        }

        _logger.LogInformation("Download current for doc {Id} served as {DownloadName}", id, res.Value.DownloadName);
        return File(res.Value.Stream, res.Value.ContentType, res.Value.DownloadName);
    }

    [HttpPut("{id:int}/currentVersion/{versionId:int}")]
    public async Task<IActionResult> SetCurrentVersion(int id, int versionId)
    {
        _logger.LogInformation("PUT /api/documents/{Id}/currentVersion/{VersionId} requested", id, versionId);

        try
        {
            var ok = await _docs.SetCurrentVersionAsync(id, versionId, HttpContext.RequestAborted);
            if (!ok)
            {
                _logger.LogWarning("SetCurrentVersion failed: document {Id} not found", id);
                return NotFound();
            }

            _logger.LogInformation("CurrentVersion updated: document {Id} -> version {VersionId}", id, versionId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "SetCurrentVersion rejected: version {VersionId} does not belong to document {Id}", versionId, id);
            return BadRequest("Version does not belong to document.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SetCurrentVersion for doc {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error while setting current version");
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDocById(int id)
    {
        _logger.LogInformation("DELETE /api/documents/{Id} requested", id);

        try
        {
            var (found, error) = await _docs.DeleteAsync(id, HttpContext.RequestAborted);

            if (!found)
            {
                _logger.LogWarning("Delete requested but document {Id} not found", id);
                return NotFound();
            }

            if (error != null)
            {
                _logger.LogWarning("Delete aborted for doc {Id}: {Error}", id, error);
                return StatusCode(StatusCodes.Status502BadGateway, error);
            }

            _logger.LogInformation("Document {Id} deleted successfully", id);
            return Ok("successfully removed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting document {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, "Unexpected error while deleting document");
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> ElasticSearch([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("Search rejected: query was empty");
            return BadRequest("Query parameter is required.");
        }

        _logger.LogInformation("Search requested. Query='{Query}'", query);

        try
        {
            var res = await _docs.SearchAsync(query, HttpContext.RequestAborted);
            _logger.LogInformation("Search completed. Query='{Query}'", query);
            return Ok(res);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed. Query='{Query}'", query);
            return StatusCode(StatusCodes.Status500InternalServerError, "Search failed");
        }
    }
}
