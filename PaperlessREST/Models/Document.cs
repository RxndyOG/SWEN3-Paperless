namespace PaperlessREST.Models;

public class Document
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string MimeType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

