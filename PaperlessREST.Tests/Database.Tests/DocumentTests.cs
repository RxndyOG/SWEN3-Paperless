using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Paperless.Contracts.SharedServices;
using Paperless.REST.Models;
using PaperlessREST.Data;
using PaperlessREST.Models;
using PaperlessREST.Services;
using PaperlessREST.Services.Documents; // <- wherever you placed DocumentService/IDocumentService
using System.Text.Json;
using Xunit;

public class DocumentServiceTests : IClassFixture<PostgresFixture>
{
    private readonly string _cs;

    public DocumentServiceTests(PostgresFixture fx) => _cs = fx.ConnectionString;

    private AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_cs).Options);

    private static void CleanDocuments(AppDbContext db)
    {
        db.Database.Migrate();
        var et = db.Model.FindEntityType(typeof(Document))!;
        var schema = et.GetSchema() ?? "public";
        var table = et.GetTableName()!;
        db.Database.ExecuteSqlRaw($@"TRUNCATE TABLE ""{schema}"".""{table}"" RESTART IDENTITY CASCADE");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsExpectedDocumentShape()
    {
        int id;

        // Arrange DB
        using (var db = NewDb())
        {
            CleanDocuments(db);

            var docs = new List<Document>
            {
                new Document {
                    FileName = "test1",
                    Versions = new() { new DocumentVersion { VersionNumber = 1, SummarizedContent="dummy", ContentType="application/pdf", SizeBytes=0 } },
                    CreatedAt = DateTime.UtcNow
                },
                new Document {
                    FileName = "this should be returned",
                    Versions = new() { new DocumentVersion { VersionNumber = 1, SummarizedContent="this is the content", ContentType="application/pdf", SizeBytes=0 } },
                    CreatedAt = DateTime.UtcNow
                }
            };

            db.Documents.AddRange(docs);
            await db.SaveChangesAsync();

            foreach (var d in docs)
                d.CurrentVersionId = d.Versions.First().Id;

            await db.SaveChangesAsync();

            id = docs[1].Id;
        }

        // Arrange service with mocks
        using (var db = NewDb())
        {
            var storageMock = new Mock<IObjectStorage>(MockBehavior.Strict);
            var mqMock = new Mock<IMessageQueueService>(MockBehavior.Strict);
            var elasticMock = new Mock<IElasticService>(MockBehavior.Strict);

            var svc = new DocumentService(
                db,
                NullLogger<DocumentService>.Instance,
                storageMock.Object,
                mqMock.Object,
                elasticMock.Object);

            // Act
            var result = await svc.GetByIdAsync(id, CancellationToken.None);

            // Assert
            Assert.NotNull(result);

            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("this should be returned", root.GetProperty("FileName").GetString());
            Assert.Equal(id, root.GetProperty("Id").GetInt32());

            var firstVersion = root.GetProperty("Versions").EnumerateArray().First();
            Assert.Equal("this is the content", firstVersion.GetProperty("SummarizedContent").GetString());
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromDb_WhenStorageDeletesSucceed()
    {
        int idToDelete;

        using (var db = NewDb())
        {
            CleanDocuments(db);

            var docs = new List<Document>
            {
                new Document { FileName="keep", Versions = new() {
                    new DocumentVersion{ VersionNumber=1, ObjectKey="k1", ContentType="application/pdf", SizeBytes=0 }
                }, CreatedAt=DateTime.UtcNow },
                new Document { FileName="delete-me", Versions = new() {
                    new DocumentVersion{ VersionNumber=1, ObjectKey="del1", ContentType="application/pdf", SizeBytes=0 }
                }, CreatedAt=DateTime.UtcNow }
            };

            db.Documents.AddRange(docs);
            await db.SaveChangesAsync();

            foreach (var d in docs) d.CurrentVersionId = d.Versions.First().Id;
            await db.SaveChangesAsync();

            idToDelete = docs[1].Id;
        }

        using (var db = NewDb())
        {
            var storageMock = new Mock<IObjectStorage>();
            storageMock
                .Setup(s => s.RemoveObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var mqMock = new Mock<IMessageQueueService>();
            var elasticMock = new Mock<IElasticService>();

            var svc = new DocumentService(
                db,
                NullLogger<DocumentService>.Instance,
                storageMock.Object,
                mqMock.Object,
                elasticMock.Object);

            // Act
            var (found, error) = await svc.DeleteAsync(idToDelete, CancellationToken.None);

            // Assert
            Assert.True(found);
            Assert.Null(error);

            var remaining = await db.Documents.AsNoTracking().ToListAsync();
            Assert.Single(remaining);
            Assert.Equal("keep", remaining[0].FileName);
        }
    }
}
