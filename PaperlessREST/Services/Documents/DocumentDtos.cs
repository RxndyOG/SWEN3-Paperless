using Paperless.Contracts;

namespace PaperlessREST.Services.Documents;

public sealed record UploadDocumentResult(
    int DocumentId,
    string FileName,
    int CurrentVersionId,
    int VersionCreatedId,
    int VersionNumber);

public sealed record DocumentListItem(
    int Id,
    string FileName,
    DateTime CreatedAt,
    int? CurrentVersionId,
    DocumentVersionSummary? Current);

public sealed record DocumentVersionSummary(
    int Id,
    int VersionNumber,
    long SizeBytes,
    DocumentTag? Tag,
    string? ChangeSummary,
    string? SummarizedContent);
