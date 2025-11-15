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

public class OcrTests : IClassFixture<OcrFixture>
{
    private readonly OcrFixture _fx;

    public OcrTests(OcrFixture fx) => _fx = fx;

    [Fact]
    [assembly: CollectionBehavior(DisableTestParallelization = true)]
    public async Task CliOcr_Extracts_Text_From_Png_InContainer()
    {
        // Create PNG in the host workdir (shared with container)
        var tmpPng = Path.Combine(_fx.HostWorkDir, $"ocr-test-{Guid.NewGuid():N}.png");

        using (var img = new Image<Rgba32>(800, 220, Color.White))
        {
            var font = SafeFont(64);
            img.Mutate(c => c.DrawText("HELLO OCR 123", font, Color.Black, new PointF(30, 80)));
            await img.SaveAsPngAsync(tmpPng);
        }

        var engine = new ContainerOcrEngine(_fx.Container, _fx.HostWorkDir);
        var text = await engine.ExtractAsync(tmpPng, "image/png", default);

        Assert.Contains("HELLO", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCR", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("123", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [assembly: CollectionBehavior(DisableTestParallelization = true)]
    public async Task Worker_ProcessAsync_On_Png_Emits_Text_InContainer()
    {
        // 1) Prepare PNG
        var tmpPng = Path.Combine(_fx.HostWorkDir, $"ocr-worker-{Guid.NewGuid():N}.png");
        using (var img = new Image<Rgba32>(800, 220, Color.White))
        {
            var font = SafeFont(64);
            img.Mutate(c => c.DrawText("WORKER OK", font, Color.Black, new PointF(30, 80)));
            await img.SaveAsPngAsync(tmpPng);
        }

        // 2) Mocks + our container-based engine
        var logger = new LoggerFactory().CreateLogger<RabbitConsumerService>();
        var opts = Options.Create(new RabbitOptions { QueueName = "documents" });

        var fetcher = new Mock<IObjectFetcher>();
        fetcher.Setup(f => f.FetchToTempFileAsync("bucket", "key", "file.png", It.IsAny<CancellationToken>()))
               .ReturnsAsync(tmpPng);

        var ocr = new ContainerOcrEngine(_fx.Container, _fx.HostWorkDir);
        var sink = new CapturingSink();
        var minio = new Mock<IMinioClient>(); // if ctor still requires it

        var worker = new RabbitConsumerService(logger, opts, minio.Object, fetcher.Object, ocr, sink);

        var payload = new UploadedDocMessage(
            DocumentId: 77,
            Bucket: "bucket",
            ObjectKey: "key",
            FileName: "file.png",
            ContentType: "image/png");

        // 3) Act
        await worker.ProcessAsync(payload, default);

        // 4) Assert
        var (id, text) = await sink.Tcs.Task;
        Assert.Equal(77, id);
        Assert.Contains("WORKER", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OK", text, StringComparison.OrdinalIgnoreCase);
    }

    private static Font SafeFont(float size)
    {
        var fams = SystemFonts.Families.ToList();
        var name = fams.Any(f => f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase)) ? "Arial"
                 : fams.Any(f => f.Name.Contains("DejaVu", StringComparison.OrdinalIgnoreCase)) ? "DejaVu Sans"
                 : fams.First().Name;
        return SystemFonts.CreateFont(name, size, FontStyle.Bold);
    }
}