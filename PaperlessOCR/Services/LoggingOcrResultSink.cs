using Microsoft.Extensions.Logging;
using PaperlessOCR.Abstractions;

public class LoggingOcrResultSink : IOcrResultSink
{
    private readonly ILogger<LoggingOcrResultSink> _log;
    public LoggingOcrResultSink(ILogger<LoggingOcrResultSink> log) => _log = log;

    public Task OnOcrCompletedAsync(int documentId, string text, CancellationToken ct)
    {
        _log.LogInformation("OCR DONE for {Id}: {Preview}",
            documentId, text.Length > 10000 ? text[..200] + "..." : text);
        return Task.CompletedTask;
    }
}