using Xunit;
using PaperlessREST.Data;
using PaperlessREST.Models;
using Microsoft.EntityFrameworkCore;

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