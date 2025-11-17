using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaperlessAI.Services;

public class GenAiEngineTests
{
    private GenAiEngine CreateEngine(string? apiKey = "dummy")
    {
        var opts = Options.Create(new GenAiOptions { ApiKey = apiKey });
        var logger = Mock.Of<ILogger<GenAiEngine>>();
        return new GenAiEngine(opts, logger);
    }

    [Fact]
    public async Task SummarizeAsync_EmptyText_ThrowsArgumentException()
    {
        var engine = CreateEngine("dummy");

        await Assert.ThrowsAsync<ArgumentException>(
            () => engine.SummarizeAsync("  ", CancellationToken.None));
    }

    [Fact]
    public async Task SummarizeAsync_NoApiKey_ThrowsInvalidOperationException()
    {
        var engine = CreateEngine(apiKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.SummarizeAsync("Some text", CancellationToken.None));
    }

    [Fact]
    public async Task SummarizeAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var engine = CreateEngine("dummy");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.SummarizeAsync("Some text", cts.Token));
    }
}
