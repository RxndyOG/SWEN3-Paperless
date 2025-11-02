using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaperlessREST.Controllers;
using PaperlessREST.Data;
using PaperlessREST.Models;
using Xunit;
using Microsoft.Extensions.Logging;
using PaperlessREST.Services;
using Moq;

public class DocumentRepositoryTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public DocumentRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddDocument_ShouldPersistAndRetrieve()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        using (var db = new AppDbContext(options))
        {
            var doc = new Document
            {
                FileName = "test.pdf",
                Content = "Hello World",
                CreatedAt = DateTime.UtcNow
            };

            // Act
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
        }

        // Assert
        using (var db = new AppDbContext(options))
        {
            var docs = await db.Documents.ToListAsync();
            Assert.Single(docs);
            Assert.Equal("test.pdf", docs[0].FileName);
        }
    }
}

public class DocumentsController_Update_Tests : IClassFixture<PostgresFixture>
{
    private readonly string _cs;
    public DocumentsController_Update_Tests(PostgresFixture fx) => _cs = fx.ConnectionString;

    private AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
               .UseNpgsql(_cs).Options);

    [Fact]
    public async Task UpdateDocument_Should_Return_NoContent_And_Persist_Changes()
    {
        // Arrange
        int id;
        using (var db = NewDb())
        {
            CleanDocuments(db);

            var doc = new Document
            {
                FileName = "old.pdf",
                Content = "old",
                CreatedAt = DateTime.UtcNow
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
            id = doc.Id;
        }

        IActionResult result;
        using (var db = NewDb())
        {
            var logger = new LoggerFactory().CreateLogger<DocumentsController>();
            var storage = new Mock<IObjectStorage>().Object;
            var mq = new Mock<MessageQueueService>().Object;
            var controller = new DocumentsController(db, logger, storage, mq);
            var dto = new Document { FileName = "new.pdf", Content = "new content" };

            // Act
            result = await controller.UpdateDocument(id, dto);
        }

        // Assert HTTP result
        Assert.IsType<Microsoft.AspNetCore.Mvc.NoContentResult>(result);

        // Assert DB state
        using var verify = NewDb();
        var updated = await verify.Documents.FindAsync(id);
        Assert.NotNull(updated);
        Assert.Equal("new.pdf", updated!.FileName);
        Assert.Equal("new content", updated.Content);
    }

    [Fact]
    public async Task GetExistingDocumentById()
    {
        // Arrange
        int id;
        using (var db = NewDb())
        {
            CleanDocuments(db);

            var docs = new List<Document>
        {
            new Document { FileName = "test1", Content = "dummyContent", CreatedAt = DateTime.UtcNow },
            new Document { FileName = "this should be returned", Content = "this is the content", CreatedAt = DateTime.UtcNow },
            new Document { FileName = "blah", Content = "blah", CreatedAt = DateTime.UtcNow }
        };

            db.Documents.AddRange(docs);
            await db.SaveChangesAsync();
            id = docs[1].Id;
        }

        // Act
        IActionResult result;
        using (var db = NewDb())
        {
            var logger = new LoggerFactory().CreateLogger<DocumentsController>();

            var storageMock = new Mock<IObjectStorage>();
            // Force the controller into the fallback branch that returns the Document directly:
            storageMock
                .Setup(s => s.GetPresignedGetUrl(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Throws(new Exception("presign fail"));

            var mqMock = new Mock<MessageQueueService>();

            var controller = new DocumentsController(db, logger, storageMock.Object, mqMock.Object);

            result = await controller.GetDocById(id);
        }

        var ok = Assert.IsType<OkObjectResult>(result);
        var document = Assert.IsType<Document>(ok.Value);
        Assert.Equal("this should be returned", document.FileName);
        Assert.Equal("this is the content", document.Content);
        Assert.Equal(id, document.Id);
    }


    [Fact]
    public async Task InsertDocumentThenDeleteShouldReturnOk()
    {
        //Arrange
        int id;
        using (var db = NewDb())
        {
            CleanDocuments(db);
            var docs = new List<Document>
            { new Document { FileName = "test1", Content = "dummyContent", CreatedAt = DateTime.UtcNow },
            new Document { FileName = "this should be deleted", Content = "this is the content", CreatedAt= DateTime.UtcNow },
            new Document { FileName = "blah", Content = "blah", CreatedAt = DateTime.UtcNow}
            };
            foreach (var doc in docs)
            {
                db.Documents.Add(doc);
            }
            await db.SaveChangesAsync();
            id = docs[1].Id;
        }

        // Act
        IActionResult deleteResult;
        List<Document> documentList;

        using (var db = NewDb())
        {
            var logger = new LoggerFactory().CreateLogger<DocumentsController>();
            var storage = new Mock<IObjectStorage>().Object;
            var mq = new Mock<MessageQueueService>().Object;
            var controller = new DocumentsController(db, logger, storage, mq);

            // Call DELETE
            deleteResult = await controller.DeleteDocById(id);

            // Call GET ALL
            var getAllResult = await controller.GetAll();

            // unwrap OkObjectResult and get the list
            var okObject = Assert.IsType<OkObjectResult>(getAllResult);
            documentList = Assert.IsAssignableFrom<List<Document>>(okObject.Value);
        }

        // Assert
        Assert.IsType<OkObjectResult>(deleteResult);
        Assert.Equal(2, documentList.Count);
        Assert.DoesNotContain(documentList, d => d.Id == id);
    }

    [Fact]
    public async Task UpdateDocument_Should_Return_NotFound_When_Id_Does_Not_Exist()
    {
        using var db = NewDb();
        var logger = new LoggerFactory().CreateLogger<DocumentsController>();
        var storage = new Mock<IObjectStorage>().Object;
        var mq = new Mock<MessageQueueService>().Object;
        var controller = new DocumentsController(db, logger, storage, mq);

        var dto = new Document { FileName = "x.pdf", Content = "x" };

        var result = await controller.UpdateDocument(999999, dto);

        Assert.IsType<NotFoundResult>(result);
    }

    static void CleanDocuments(AppDbContext db)
    {
        // Ensure schema exists (and migrations are applied) before truncating
        db.Database.Migrate();

        var et = db.Model.FindEntityType(typeof(Document))!;
        var schema = et.GetSchema() ?? "public";
        var table = et.GetTableName()!; // EF’s actual table name

        // Quote identifiers to work with any casing
        db.Database.ExecuteSqlRaw($@"TRUNCATE TABLE ""{schema}"".""{table}"" RESTART IDENTITY CASCADE");
    }
}