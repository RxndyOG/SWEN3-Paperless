using System.Text.Json;
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
using Paperless.Contracts.SharedServices;
using Paperless.REST.Models;

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
                CreatedAt = DateTime.UtcNow
            };

            // add a version (the Document no longer has Content directly)
            doc.Versions = new List<DocumentVersion>
            {
                new DocumentVersion
                {
                    VersionNumber = 1,
                    ContentType = "application/pdf",
                    SizeBytes = 0,
                    SummarizedContent = "Hello World"
                }
            };

            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            // set current version to the newly created version
            doc.CurrentVersionId = doc.Versions.First().Id;
            await db.SaveChangesAsync();
        }

        // Assert
        using (var db = new AppDbContext(options))
        {
            var docs = await db.Documents.Include(d => d.Versions).ToListAsync();
            Assert.Single(docs);
            Assert.Equal("test.pdf", docs[0].FileName);
            Assert.NotEmpty(docs[0].Versions);
            Assert.Equal("Hello World", docs[0].Versions.First().SummarizedContent);
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
    public async Task GetExistingDocumentById()
    {
        // Arrange
        int id;
        using (var db = NewDb())
        {
            CleanDocuments(db);

            var docs = new List<Document>
            {
                new Document {
                    FileName = "test1",
                    Versions = new List<DocumentVersion> {
                        new DocumentVersion { VersionNumber = 1, SummarizedContent = "dummyContent", ContentType = "application/pdf", SizeBytes = 0 }
                    },
                    CreatedAt = DateTime.UtcNow
                },
                new Document {
                    FileName = "this should be returned",
                    Versions = new List<DocumentVersion> {
                        new DocumentVersion { VersionNumber = 1, SummarizedContent = "this is the content", ContentType = "application/pdf", SizeBytes = 0 }
                    },
                    CreatedAt = DateTime.UtcNow
                },
                new Document {
                    FileName = "blah",
                    Versions = new List<DocumentVersion> {
                        new DocumentVersion { VersionNumber = 1, SummarizedContent = "blah", ContentType = "application/pdf", SizeBytes = 0 }
                    },
                    CreatedAt = DateTime.UtcNow
                }
            };

            db.Documents.AddRange(docs);
            await db.SaveChangesAsync();

            // set current version ids now that versions have ids
            foreach (var d in docs)
            {
                d.CurrentVersionId = d.Versions.First().Id;
            }
            await db.SaveChangesAsync();

            id = docs[1].Id;
        }

        // Act
        IActionResult result;
        var elasticMock = new Mock<IElasticService>();

        using (var db = NewDb())
        {
            var logger = new LoggerFactory().CreateLogger<DocumentsController>();

            var storageMock = new Mock<IObjectStorage>();
            // Force any code path that may attempt presign to fail (controller won't fail here but keep as test intent)
            storageMock
                .Setup(s => s.GetPresignedGetUrl(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Throws(new Exception("presign fail"));


            var mqMock = new Mock<RestQueueService>();

            var controller = new DocumentsController(db, logger, storageMock.Object, mqMock.Object, elasticMock.Object);

            result = await controller.GetDocById(id);
        }

        var ok = Assert.IsType<OkObjectResult>(result);
        // serialize the anonymous result to JSON and assert on returned shape
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("this should be returned", root.GetProperty("FileName").GetString());

        var versions = root.GetProperty("Versions").EnumerateArray();
        var firstVersion = versions.First();
        Assert.Equal("this is the content", firstVersion.GetProperty("SummarizedContent").GetString());
        Assert.Equal(id, root.GetProperty("Id").GetInt32());
    }


    [Fact]
    public async Task InsertDocumentThenDeleteShouldReturnOk()
    {
        var elasticMock = new Mock<IElasticService>();

        using var db = NewDb();
        CleanDocuments(db);

        var docs = new List<Document>
    {
        new Document { FileName = "test1", Versions = new() { new DocumentVersion { VersionNumber = 1, SummarizedContent = "dummyContent", ContentType="application/pdf", SizeBytes=0 } }, CreatedAt = DateTime.UtcNow },
        new Document { FileName = "this should be deleted", Versions = new() { new DocumentVersion { VersionNumber = 1, SummarizedContent = "this is the content", ContentType="application/pdf", SizeBytes=0 } }, CreatedAt = DateTime.UtcNow },
        new Document { FileName = "blah", Versions = new() { new DocumentVersion { VersionNumber = 1, SummarizedContent = "blah", ContentType="application/pdf", SizeBytes=0 } }, CreatedAt = DateTime.UtcNow }
    };

        db.Documents.AddRange(docs);
        await db.SaveChangesAsync();

        foreach (var d in docs)
            d.CurrentVersionId = d.Versions.First().Id;

        await db.SaveChangesAsync();

        var id = docs[1].Id;

        var logger = new LoggerFactory().CreateLogger<DocumentsController>();

        var storageMock = new Mock<IObjectStorage>();
        storageMock
            .Setup(s => s.RemoveObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mq = new Mock<RestQueueService>().Object;
        var controller = new DocumentsController(db, logger, storageMock.Object, mq, elasticMock.Object);

        // Act
        var deleteResult = await controller.DeleteDocById(id);
        var getAllResult = await controller.GetAll();

        // Assert delete
        Assert.IsType<OkObjectResult>(deleteResult);

        // Assert list
        var okObject = Assert.IsType<OkObjectResult>(getAllResult);
        var json = JsonSerializer.Serialize(okObject.Value);
        using var jdoc = JsonDocument.Parse(json);
        var rootArray = jdoc.RootElement;

        Assert.Equal(2, rootArray.GetArrayLength());
        Assert.DoesNotContain(rootArray.EnumerateArray(), e => e.GetProperty("Id").GetInt32() == id);
    }



    static void CleanDocuments(AppDbContext db)
    {
        // Ensure schema exists (and migrations are applied) before truncating
        db.Database.Migrate();

        var et = db.Model.FindEntityType(typeof(Document))!;
        var schema = et.GetSchema() ?? "public";
        var table = et.GetTableName()!; // EF’s actual table name

        // Build raw SQL for identifiers — do NOT use ExecuteSqlInterpolated which parameterizes identifiers
        var sql = $@"TRUNCATE TABLE ""{schema}"".""{table}"" RESTART IDENTITY CASCADE";
        db.Database.ExecuteSqlRaw(sql);
    }
}