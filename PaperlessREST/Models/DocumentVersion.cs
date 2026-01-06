using Paperless.Contracts;
using PaperlessREST.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Paperless.REST.Models
{
    public class DocumentVersion
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public Document Document { get; set; } = null!;

        public int? DiffBaseVersionId { get; set; }
        public DocumentVersion? DiffBaseVersion { get; set; }

        public int VersionNumber { get; set; }

        public string ObjectKey { get; set; } = "";
        public string ContentType { get; set; } = "application/pdf";
        public long SizeBytes { get; set; }

        public string Content { get; set; } = string.Empty;
        public string SummarizedContent { get; set; } = string.Empty;
        public string ChangeSummary { get; set; } = string.Empty;
        public DocumentTag Tag { get; set; }
    }
}
