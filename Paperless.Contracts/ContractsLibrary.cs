namespace Paperless.Contracts
{
    public class OcrCompletedMessage
    {
        public int DocumentId { get; set; }
        public required string Text { get; set; } = "";
    }

    public class GenAiSummaryMessage
    {
        public int DocumentId { get; set; }
        public required string Summary { get; set; } = "";
    }

    public record UploadedDocMessage(
    int DocumentId,
    string Bucket,
    string ObjectKey,
    string FileName,
    string ContentType
);

    public static class QueueNames
    {
        public const string Documents = "documents";
        public const string OcrFinished = "ocr_finished";
        public const string GenAiFinished = "genai_finished";
    }

    public class RabbitOptions
    {
        public string Host { get; set; } = "";
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
        public required string InputQueue { get; set; }
        public required string OutputQueue { get; set; }
    }

    public class MinioOptions
    {
        public string Endpoint { get; set; } = "";
        public string Bucket { get; set; } = "";
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public bool UseSSL { get; set; } = false;
    }
}
