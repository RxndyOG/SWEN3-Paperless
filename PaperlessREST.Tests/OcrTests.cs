using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Moq;
using PaperlessOCR.Abstractions;
using PaperlessOCR.Services;
using PaperlessREST.Controllers;
using PaperlessREST.Data;
using PaperlessREST.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using Xunit;
using Xunit.Sdk;


public class CapturingSink : IOcrResultSink
{
    public readonly TaskCompletionSource<(int id, string text)> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task OnOcrCompletedAsync(int documentId, string text, CancellationToken ct)
    {
        Tcs.TrySetResult((documentId, text));
        return Task.CompletedTask;
    }
}

public static class ToolCheck
{
    public static bool ToolAvailable(string tool, string args = "--version")
    {
        try
        {
            var psi = new ProcessStartInfo(tool, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(4000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
    public static void RequiresTesseractOrSkip()
    {
        if (!ToolAvailable("tesseract"))
            throw SkipException.ForSkip("tesseract not available on PATH");
    }

    public static void RequiresPdftoppmOrSkip()
    {
        if (!ToolAvailable("pdftoppm"))
            throw SkipException.ForSkip("pdftoppm not available on PATH");
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

    [Fact]
    public async Task CliOcr_Extracts_Text_From_Png()
    {
        ToolCheck.RequiresTesseractOrSkip();   // <- add this

        using var img = new Image<Rgba32>(800, 220, Color.White);
        var font = SafeFont(64);
        img.Mutate(c => c.DrawText("HELLO OCR 123", font, Color.Black, new PointF(30, 80)));

        var tmpPng = Path.Combine(Path.GetTempPath(), $"ocr-test-{Guid.NewGuid():N}.png");
        await img.SaveAsPngAsync(tmpPng);

        try
        {
            var engine = new CliOcrEngine();
            var text = await engine.ExtractAsync(tmpPng, "image/png", default);

            Assert.Contains("HELLO", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OCR", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("123", text);
        }
        finally { TryDelete(tmpPng); }
    }

    [Fact]
    public async Task Worker_ProcessAsync_On_Png_Emits_Text()
    {
        ToolCheck.RequiresTesseractOrSkip();

        // Create the PNG
        var tmpPng = Path.Combine(Path.GetTempPath(), $"ocr-worker-{Guid.NewGuid():N}.png");
        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(800, 220, SixLabors.ImageSharp.Color.White))
        {
            var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 64, SixLabors.Fonts.FontStyle.Bold);
            img.Mutate(c => c.DrawText("WORKER OK", font, SixLabors.ImageSharp.Color.Black, new PointF(30, 80)));
            await img.SaveAsPngAsync(tmpPng);
        }

        try
        {
            // Arrange worker dependencies
            var logger = new LoggerFactory().CreateLogger<RabbitConsumerService>();
            var opts = Options.Create(new RabbitOptions { QueueName = "documents" });

            var fetcher = new Mock<IObjectFetcher>();
            fetcher.Setup(f => f.FetchToTempFileAsync("bucket", "key", "file.png", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(tmpPng);

            var ocr = new CliOcrEngine();            // real OCR
            var sink = new CapturingSink();

            // If your constructor no longer needs IMinioClient, remove it.
            // If it still does (but unused in ProcessAsync), pass a dummy mock:
            var minio = new Mock<Minio.IMinioClient>();

            var worker = new RabbitConsumerService(logger, opts, minio.Object, fetcher.Object, ocr, sink);

            var payload = new UploadedDocMessage(
                DocumentId: 77,
                Bucket: "bucket",
                ObjectKey: "key",
                FileName: "file.png",
                ContentType: "image/png");

            // Act
            await worker.ProcessAsync(payload, default);

            // Assert sink received expected text
            var (id, text) = await sink.Tcs.Task;
            Assert.Equal(77, id);
            Assert.Contains("WORKER", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OK", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(tmpPng); } catch { }
        }
    }


    private static Font SafeFont(float size)
    {
        // Try common fonts; fallback to first available
        var families = SystemFonts.Families.ToList();
        var name = families.Any(f => f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase)) ? "Arial"
                 : families.Any(f => f.Name.Contains("DejaVu", StringComparison.OrdinalIgnoreCase)) ? "DejaVu Sans"
                 : families.First().Name;
        return SystemFonts.CreateFont(name, size, FontStyle.Bold);
    }

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
