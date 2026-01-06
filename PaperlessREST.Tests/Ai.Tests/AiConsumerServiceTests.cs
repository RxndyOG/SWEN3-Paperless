using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PaperlessAI;
using PaperlessAI.Abstractions;
using PaperlessAI.Services;
using Paperless.Contracts;
using Xunit;
using Paperless.AI.Abstractions;

public class AiConsumerServiceTests
{
    private AiConsumerService CreateService(
        Mock<IGenAiEngine> engineMock,
        Mock<IGenAiResultSink> sinkMock,
        Mock<IVersionTextClient>? restClientMock = null)
    {
        var logger = Mock.Of<ILogger<AiConsumerService>>();

        var rabbitOpts = Options.Create(new RabbitOptions
        {
            Host = "rabbitmq",
            User = "user",
            Pass = "pass",
            InputQueue = QueueNames.OcrFinished,
            OutputQueue = QueueNames.GenAiFinished
        });

        var genOpts = Options.Create(new GenAiOptions { ApiKey = "dummy" });

        restClientMock ??= new Mock<IVersionTextClient>();

        return new AiConsumerService(
            logger,
            rabbitOpts,
            genOpts,
            engineMock.Object,
            sinkMock.Object,
            restClientMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_ValidMessage_CallsEngineSummarize_Classify_AndResultSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();
        var restClientMock = new Mock<IVersionTextClient>();

        var inputText = "This is some OCR text";
        var summary = "Short summary";
        var classifiedTag = DocumentTag.Medical;
        var changeSummary = "No significant changes";
        var versionId = 7;

        // If the service fetches OCR text via version client, return the same text
        restClientMock
            .Setup(r => r.GetVersionOcrTextAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inputText);

        engineMock
            .Setup(e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        engineMock
            .Setup(e => e.ClassifyAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(classifiedTag);

        engineMock
            .Setup(e => e.ChangeSummaryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeSummary);

        var sut = CreateService(engineMock, sinkMock, restClientMock);

        var message = new OcrCompletedMessage(
            DocumentId: 42,
            DocumentVersionId: versionId,
            // Give a non-null base version so ChangeSummaryAsync is invoked
            DiffBaseVersionId: 6,
            OcrText: inputText
        );

        // Act
        await sut.ProcessAsync(message, CancellationToken.None);

        // Assert
        engineMock.Verify(
            e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        engineMock.Verify(
            e => e.ClassifyAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        engineMock.Verify(
            e => e.ChangeSummaryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(
                42,
                versionId,
                summary,
                classifiedTag,
                inputText,
                changeSummary,
                It.IsAny<CancellationToken>()
            ),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenSummarizeThrows_PropagatesException_AndDoesNotCallSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();
        var restClientMock = new Mock<IVersionTextClient>();

        engineMock
            .Setup(e => e.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationException("AI summarize failed"));

        var sut = CreateService(engineMock, sinkMock, restClientMock);

        var message = new OcrCompletedMessage(
            DocumentId: 99,
            DocumentVersionId: 11,
            DiffBaseVersionId: null,
            OcrText: "Something"
);

        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(
            () => sut.ProcessAsync(message, CancellationToken.None));

        engineMock.Verify(
            e => e.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<DocumentTag>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenClassifyThrows_PropagatesException_AndDoesNotCallSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();
        var restClientMock = new Mock<IVersionTextClient>();

        var inputText = "Some OCR text";
        var summary = "Summary works";
        var versionId = 13;

        engineMock
            .Setup(e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        engineMock
            .Setup(e => e.ClassifyAsync(inputText, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationException("AI classify failed"));

        var sut = CreateService(engineMock, sinkMock, restClientMock);

        var message = new OcrCompletedMessage(
            DocumentId: 7,
            DocumentVersionId: versionId,
            DiffBaseVersionId: null,
            OcrText: inputText
);

        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(
            () => sut.ProcessAsync(message, CancellationToken.None));

        // Summarize was called
        engineMock.Verify(
            e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        // Sink was NOT called because classification failed
        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<DocumentTag>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
