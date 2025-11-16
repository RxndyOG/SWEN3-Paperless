namespace PaperlessREST.Models;

public class Document
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    public string SummarizedContent { get; set; } = string.Empty;
}

