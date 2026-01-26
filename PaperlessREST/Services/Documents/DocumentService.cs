using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Paperless.Contracts;
using Paperless.Contracts.SharedServices;
using Paperless.REST.Models;
using PaperlessREST.Data;
using PaperlessREST.Models;
using System.Linq;
using System.Text.Json;

namespace PaperlessREST.Services.Documents;

public sealed class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DocumentService> _logger;
    private readonly IObjectStorage _storage;
    private readonly IMessageQueueService _mq;
    private readonly IElasticService _elastic;

    public DocumentService(
        AppDbContext db,
        ILogger<DocumentService> logger,
        IObjectStorage storage,
        IMessageQueueService mq,
        IElasticService elastic)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
        _mq = mq;
        _elastic = elastic;
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetAllAsync(CancellationToken ct)
    {
        var docs = await _db.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentListItem(
                d.Id,
                d.FileName!,
                d.CreatedAt,
                d.CurrentVersionId,
                d.Versions
                    .Where(v => v.Id == d.CurrentVersionId)
                    .Select(v => new DocumentVersionSummary(
                        v.Id,
                        v.VersionNumber,
                        v.SizeBytes,
                        v.Tag,
                        v.ChangeSummary,
                        v.SummarizedContent))
                    .FirstOrDefault()
            ))
            .ToListAsync(ct);

        return docs;
    }

    public async Task<UploadDocumentResult> UploadAsync(IFormFile file, CancellationToken ct)
    {
        var fileName = Path.GetFileName(file.FileName);
        var objectKey = $"{Guid.NewGuid():N}_{fileName}";

        _logger.LogInformation("Uploading PDF {ObjectKey} (name: {FileName}, size: {Size})",
            objectKey, file.FileName, file.Length);

        // 1) store object first
        await using (var stream = file.OpenReadStream())
        {
            await _storage.PutObjectAsync(stream, objectKey, "application/pdf");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // 2) doc + version in DB
            var doc = await _db.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.FileName == fileName, ct);

            if (doc == null)
            {
                doc = new Document { FileName = fileName, CreatedAt = DateTime.UtcNow };
                _db.Documents.Add(doc);
                await _db.SaveChangesAsync(ct);
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
            await _db.SaveChangesAsync(ct);

            doc.CurrentVersionId = newVersion.Id;
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            // 3) publish event (outside tx is fine — or keep inside if you really want)
            TryPublishVersionMessage(doc, newVersion, baseVersionId, objectKey);

            _logger.LogInformation("Uploaded document saved (id: {Id}, key: {Key})", doc.Id, objectKey);

            return new UploadDocumentResult(
                doc.Id,
                doc.FileName!,
                doc.CurrentVersionId!.Value,
                newVersion.Id,
                newVersion.VersionNumber);
        }
        catch
        {
            await tx.RollbackAsync(ct);

            // cleanup object if DB failed
            try
            {
                await _storage.RemoveObjectAsync(objectKey);
                _logger.LogInformation("Rolled back stored object {ObjectKey} after failure", objectKey);
            }
            catch (Exception cleanEx)
            {
                _logger.LogWarning(cleanEx, "Failed to remove object {ObjectKey} after rollback", objectKey);
            }

            throw;
        }
    }

    private void TryPublishVersionMessage(Document doc, DocumentVersion newVersion, int? baseVersionId, string objectKey)
    {
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
            _logger.LogInformation("Published upload message for doc {Id} to {Queue}", doc.Id, QueueNames.Documents);
        }
        catch (Exception mqEx)
        {
            _logger.LogWarning(mqEx, "Failed to publish message for doc {Id}", doc.Id);
        }
    }

    public async Task<object?> GetByIdAsync(int id, CancellationToken ct)
    {
        var doc = await _db.Documents.AsNoTracking()
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (doc == null) return null;

        return new
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
        };
    }

    public async Task<object?> GetVersionsAsync(int docId, CancellationToken ct)
    {
        var doc = await _db.Documents.AsNoTracking()
            .Where(d => d.Id == docId)
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
            .FirstOrDefaultAsync(ct);

        return doc;
    }

    public async Task<object?> GetVersionAsync(int versionId, CancellationToken ct)
    {
        return await _db.DocumentVersions.AsNoTracking()
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
            .FirstOrDefaultAsync(ct);
    }

    public async Task<object?> GetVersionOcrAsync(int versionId, CancellationToken ct)
    {
        return await _db.DocumentVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new { x.Id, OcrText = x.Content })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(Stream Stream, string ContentType, string DownloadName)?> DownloadVersionAsync(int versionId, CancellationToken ct)
    {
        var v = await _db.DocumentVersions.AsNoTracking()
            .Where(x => x.Id == versionId)
            .Select(x => new { x.ObjectKey, x.ContentType })
            .FirstOrDefaultAsync(ct);

        if (v == null) return null;

        var stream = await _storage.GetObjectAsync(v.ObjectKey);
        return (stream, v.ContentType, $"version-{versionId}.pdf");
    }

    public async Task<(Stream Stream, string ContentType, string DownloadName)?> DownloadCurrentAsync(int docId, CancellationToken ct)
    {
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc == null) return null;

        var version = await _db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == doc.CurrentVersionId, ct);

        if (version == null) return null;

        var stream = await _storage.GetObjectAsync(version.ObjectKey);
        return (stream, "application/pdf", doc.FileName!);
    }

    public async Task<bool> SetCurrentVersionAsync(int docId, int versionId, CancellationToken ct)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc == null) return false;

        var exists = await _db.DocumentVersions.AnyAsync(v => v.Id == versionId && v.DocumentId == docId, ct);
        if (!exists) throw new InvalidOperationException("Version does not belong to document.");

        doc.CurrentVersionId = versionId;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(bool Found, string? Error)> DeleteAsync(int docId, CancellationToken ct)
    {
        var doc = await _db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == docId, ct);

        if (doc == null) return (false, null);

        var failedKeys = new List<string>();

        foreach (var v in doc.Versions)
        {
            if (string.IsNullOrWhiteSpace(v.ObjectKey))
                continue;

            try { await _storage.RemoveObjectAsync(v.ObjectKey); }
            catch { failedKeys.Add(v.ObjectKey); }
        }

        if (failedKeys.Count > 0)
            return (true, $"Failed to remove {failedKeys.Count} object(s) from storage. Aborting DB delete.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        doc.CurrentVersionId = null;
        await _db.SaveChangesAsync(ct);

        _db.DocumentVersions.RemoveRange(doc.Versions);
        _db.Documents.Remove(doc);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (true, null);
    }

    public async Task<IEnumerable<MessageTransferObject>> SearchAsync(string query, CancellationToken ct)
        => await _elastic.SearchAsync(query);

    public async Task<ConsumeOutcome> ApplyGenAiResultAsync(GenAiCompletedMessage msg,  CancellationToken ct)
    {
        //validation checks
        if (msg == null)
        {
            _logger.LogWarning("GenAI message was null -> drop");
            return ConsumeOutcome.Ack;
        }
        if (msg.DocumentVersionId <= 0)
        {
            _logger.LogWarning("GenAI message missing DocumentVersionId (DocumentID={DocumentId}) -> drop", msg.DocumentVersionId);
            return ConsumeOutcome.Ack;
        }

        var ver = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == msg.DocumentVersionId, ct);

        if (ver == null)
        {
            _logger.LogWarning("DocumentVersion {VersionId} not found for DocumentId {DocumentId} -> drop", msg.DocumentVersionId, msg.DocumentId);
            return ConsumeOutcome.Ack;
        }

        if (ver.DocumentId != msg.DocumentId)
        {
            _logger.LogWarning("Mismatch: message DocumentId={MessageDocId} but version {VersionId} belongs to Document {DocumendId}",
                msg.DocumentId, ver.Id, ver.DocumentId);
            return ConsumeOutcome.Ack;
        }

        //update fields
        ver.SummarizedContent = msg.Summary ?? "";
        ver.Tag = msg.Tag;

        if (!string.IsNullOrWhiteSpace(msg.OcrText))
            ver.Content = msg.OcrText;
        if (!string.IsNullOrWhiteSpace(msg.Summary))
            ver.ChangeSummary = msg.Summary;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Applied GenAI fields for DocumentId {DocumentId}, VersionId {VersionId}", msg.DocumentId, msg.DocumentVersionId);
        return ConsumeOutcome.Ack;
    }
}
