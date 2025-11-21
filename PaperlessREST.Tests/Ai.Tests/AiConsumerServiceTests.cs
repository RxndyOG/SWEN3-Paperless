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

public class AiConsumerServiceTests
{
    private AiConsumerService CreateService(
        Mock<IGenAiEngine> engineMock,
        Mock<IGenAiResultSink> sinkMock)
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

        return new AiConsumerService(
            logger,
            rabbitOpts,
            genOpts,
            engineMock.Object,
            sinkMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_ValidMessage_CallsEngineSummarize_Classify_AndResultSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();

        var inputText = "This is some OCR text";
        var summary = "Short summary";
        var classifiedTag = DocumentTag.Medical;

        engineMock
            .Setup(e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        engineMock
            .Setup(e => e.ClassifyAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(classifiedTag);

        var sut = CreateService(engineMock, sinkMock);

        var message = new MessageTransferObject
        {
            DocumentId = 42,
            OcrText = inputText,
            Tag = DocumentTag.Default // incoming tag is ignored/overwritten by classification
        };

        // Act
        await sut.ProcessAsync(message, CancellationToken.None);

        // Assert
        engineMock.Verify(
            e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        engineMock.Verify(
            e => e.ClassifyAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(42, summary, classifiedTag, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenSummarizeThrows_PropagatesException_AndDoesNotCallSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();

        engineMock
            .Setup(e => e.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationException("AI summarize failed"));

        // Classification should not be called if summarize already failed
        var sut = CreateService(engineMock, sinkMock);

        var message = new MessageTransferObject
        {
            DocumentId = 99,
            OcrText = "Something",
            Tag = DocumentTag.Default
        };

        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(
            () => sut.ProcessAsync(message, CancellationToken.None));

        engineMock.Verify(
            e => e.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DocumentTag>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenClassifyThrows_PropagatesException_AndDoesNotCallSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();

        var inputText = "Some OCR text";
        var summary = "Summary works";

        engineMock
            .Setup(e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        engineMock
            .Setup(e => e.ClassifyAsync(inputText, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationException("AI classify failed"));

        var sut = CreateService(engineMock, sinkMock);

        var message = new MessageTransferObject
        {
            DocumentId = 7,
            OcrText = inputText,
            Tag = DocumentTag.Default
        };

        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(
            () => sut.ProcessAsync(message, CancellationToken.None));

        // Summarize was called
        engineMock.Verify(
            e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        // Sink was NOT called because classification failed
        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DocumentTag>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
