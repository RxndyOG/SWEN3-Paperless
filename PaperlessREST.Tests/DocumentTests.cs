using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaperlessREST.Controllers;
using PaperlessREST.Data;
using PaperlessREST.Models;
using Xunit;

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
            var controller = new DocumentsController(db);
            var dto = new Document { FileName = "new.pdf", Content = "new content" };

            // Act
            result = controller.UpdateDocument(id, dto);
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
        //Arrange
        int id;
        using (var db = NewDb())
        {
            var docs = new List<Document>
            { new Document { FileName = "test1", Content = "dummyContent", CreatedAt = DateTime.UtcNow },
            new Document { FileName = "this should be returned", Content = "this is the content", CreatedAt= DateTime.UtcNow },
            new Document { FileName = "blah", Content = "blah", CreatedAt = DateTime.UtcNow}
            };
            foreach (var doc in docs)
            {
                db.Documents.Add(doc);
            }
            await db.SaveChangesAsync();
            id = docs[1].Id;
        }

        //Act
        IActionResult result;
        using (var db = NewDb())
        {
            var controller = new DocumentsController(db);
            var documentToFetch = new Document { Id = id, FileName = "this should be returned", Content = "this is the content" };

            result = controller.GetDocById(id);
        }

        //Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var document = Assert.IsType<Document>(okResult.Value);
        Assert.Equal("this should be returned", document.FileName);
        Assert.Equal("this is the content", document.Content);
        Assert.Equal(id, document.Id);
    }

    [Fact]
    public void UpdateDocument_Should_Return_NotFound_When_Id_Does_Not_Exist()
    {
        using var db = NewDb();
        var controller = new DocumentsController(db);

        var dto = new Document { FileName = "x.pdf", Content = "x" };

        var result = controller.UpdateDocument(999999, dto);

        Assert.IsType<NotFoundResult>(result);
    }
}