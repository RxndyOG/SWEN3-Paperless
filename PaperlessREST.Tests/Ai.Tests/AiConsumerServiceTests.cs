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
    public async Task ProcessAsync_ValidMessage_CallsEngineAndResultSink()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();

        var inputText = "This is some OCR text";
        var summary = "Short summary";

        engineMock
            .Setup(e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var sut = CreateService(engineMock, sinkMock);

        var message = new MessageTransferObject
        {
            DocumentId = 42,
            Text = inputText
        };

        // Act
        await sut.ProcessAsync(message, CancellationToken.None);

        // Assert
        engineMock.Verify(
            e => e.SummarizeAsync(inputText, It.IsAny<CancellationToken>()),
            Times.Once);

        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(42, summary, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenEngineThrows_PropagatesException()
    {
        // Arrange
        var engineMock = new Mock<IGenAiEngine>();
        var sinkMock = new Mock<IGenAiResultSink>();

        engineMock
            .Setup(e => e.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationException("AI failed"));

        var sut = CreateService(engineMock, sinkMock);

        var message = new MessageTransferObject
        {
            DocumentId = 99,
            Text = "Something"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ApplicationException>(
            () => sut.ProcessAsync(message, CancellationToken.None));

        sinkMock.Verify(
            s => s.OnGeminiCompletedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
