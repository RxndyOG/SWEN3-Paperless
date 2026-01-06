using Paperless.Contracts;
using Paperless.REST.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace PaperlessREST.Models;

public class Document
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public int? CurrentVersionId { get; set; }
    public DocumentVersion? CurrentVersion {  get; set; }
    public List<DocumentVersion> Versions { get; set; } = new();
}

