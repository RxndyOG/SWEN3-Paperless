using Castle.Core.Logging;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PaperlessREST.Controllers;
using PaperlessREST.Services.Documents;
using Xunit;
using Microsoft.Extensions.Logging;

public class DocumentsControllerTests
{
    [Fact]
    public async Task GetDocById_ReturnsNotFound_WhenServiceReturnsNull()
    {
        var svc = new Mock<IDocumentService>();
        svc.Setup(s => s.GetByIdAsync(123, It.IsAny<CancellationToken>()))
           .ReturnsAsync((object?)null);

        var logger = new Mock<ILogger<DocumentsController>>();
        var controller = new DocumentsController(svc.Object, logger.Object);

        var ct = CancellationToken.None;
        var result = await controller.GetDocById(123, ct);

        Assert.IsType<NotFoundResult>(result);
    }
}