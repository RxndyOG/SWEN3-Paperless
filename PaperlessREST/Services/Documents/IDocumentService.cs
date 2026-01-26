using Microsoft.AspNetCore.Http;
using Paperless.Contracts;

namespace PaperlessREST.Services.Documents;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentListItem>> GetAllAsync(CancellationToken ct);

    Task<UploadDocumentResult> UploadAsync(IFormFile file, CancellationToken ct);

    Task<object?> GetByIdAsync(int id, CancellationToken ct); // keep anonymous shape if you want
    Task<object?> GetVersionsAsync(int docId, CancellationToken ct);

    Task<(Stream Stream, string ContentType, string DownloadName)?> DownloadCurrentAsync(int docId, CancellationToken ct);
    Task<(Stream Stream, string ContentType, string DownloadName)?> DownloadVersionAsync(int versionId, CancellationToken ct);

    Task<object?> GetVersionAsync(int versionId, CancellationToken ct);
    Task<object?> GetVersionOcrAsync(int versionId, CancellationToken ct);

    Task<bool> SetCurrentVersionAsync(int docId, int versionId, CancellationToken ct);
    Task<ConsumeOutcome> ApplyGenAiResultAsync(GenAiCompletedMessage msg,  CancellationToken ct);

    Task<(bool Found, string? Error)> DeleteAsync(int docId, CancellationToken ct);

    Task<IEnumerable<Paperless.Contracts.MessageTransferObject>> SearchAsync(string query, CancellationToken ct);
}

public enum ConsumeOutcome
{
    Ack,        //processed or dropped
    Requeue     //transient failure -> rety
}
