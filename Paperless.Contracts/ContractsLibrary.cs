using System.Formats.Asn1;

namespace Paperless.Contracts
{
    public class MessageTransferObject
    {
        public int DocumentId { get; set; }
        public required string Summary { get; set; }
        public required string OcrText { get; set; }
        public required DocumentTag Tag { get; set; } = 0;
    }

    public enum DocumentTag
{
    Default = 0,
    Invoice = 1,
    Contract = 2,
    Personal = 3,
    Education = 4,
    Medical = 5,
    Finance = 6,
    Legal = 7,
    Other = 8
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

    public class ElasticOptions     {
        public string Uri { get; set; } = "";
        public string Index { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
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
