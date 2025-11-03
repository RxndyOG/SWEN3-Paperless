using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaperlessOCR.Abstractions;
using PaperlessREST.Controllers;
using PaperlessREST.Data;
using PaperlessREST.Models;
using Xunit;
using Minio;

public class CapturingSink : IOcrResultSink
{
    public readonly TaskCompletionSource<(int id, string text)> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task OnOcrCompletedAsync(int documentId, string text, CancellationToken ct)
    {
        Tcs.TrySetResult((documentId, text));
        return Task.CompletedTask;
    }
}

public class OcrTests
{
    [Fact]
    public async Task Worker_ProcessAsync_Emits_OCR_Result_To_Sink()
    {
        // Arrange
        var logger = new LoggerFactory().CreateLogger<RabbitConsumerService>();
        var opts = Options.Create(new RabbitOptions { QueueName = "documents" });

        var fetcher = new Mock<IObjectFetcher>();
        fetcher.Setup(f => f.FetchToTempFileAsync("bucket", "key", "file.pdf", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Path.GetTempFileName());

        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(o => o.ExtractAsync(It.IsAny<string>(), "application/pdf", It.IsAny<CancellationToken>()))
           .ReturnsAsync("HELLO_OCR");

        var sink = new CapturingSink();

        // Add a mock for IMinioClient as required by the constructor
        var minio = new Mock<IMinioClient>();

        var worker = new RabbitConsumerService(logger, opts, minio.Object, fetcher.Object, ocr.Object, sink);

        var payload = new UploadedDocMessage(42, "bucket", "key", "file.pdf", "application/pdf");

        // Act
        await worker.ProcessAsync(payload, CancellationToken.None);

        // Assert
        var (id, text) = await sink.Tcs.Task;
        Assert.Equal(42, id);
        Assert.Equal("HELLO_OCR", text);
    }
}
