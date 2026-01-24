using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Paperless.Batch;
using Xunit;

public sealed class AccessBatchWorkerIntegrationTests : IClassFixture<PostgresBatchFixture>
{
    private readonly PostgresBatchFixture _fx;

    public AccessBatchWorkerIntegrationTests(PostgresBatchFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task ProcessFile_InsertsOrUpdatesAccessStatistics_AndArchivesFile()
    {
        // Arrange: schema + seed parent rows
        await BatchSchema.CreateMinimalSchemaAsync(_fx.ConnectionString);
        var doc1 = await BatchSchema.InsertDocumentAsync(_fx.ConnectionString, "doc1.pdf");
        var doc2 = await BatchSchema.InsertDocumentAsync(_fx.ConnectionString, "doc2.pdf");

        // temp input/archive folders
        var baseDir = Path.Combine(Path.GetTempPath(), "paperless-batch-it", Guid.NewGuid().ToString("N"));
        var inputDir = Path.Combine(baseDir, "input");
        var archiveDir = Path.Combine(baseDir, "archive");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(archiveDir);

        var date = new DateTime(2026, 01, 21);
        var xmlPath = Path.Combine(inputDir, "access-2026-01-21.xml");

        var xml = $@"<accessStatistics date=""{date:yyyy-MM-dd}"">
  <document id=""{doc1}"" count=""5"" />
  <document id=""{doc2}"" count=""12"" />
</accessStatistics>";
        await File.WriteAllTextAsync(xmlPath, xml);

        // configuration for worker
        var cfg = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = _fx.ConnectionString
    })
    .Build();

        var worker = new AccessBatchWorker(NullLogger<AccessBatchWorker>.Instance, cfg);

        // Act
        await worker.ProcessFile(xmlPath, archiveDir, CancellationToken.None);

        // Assert DB rows
        var c1 = await BatchSchema.GetAccessCountAsync(_fx.ConnectionString, doc1, date);
        var c2 = await BatchSchema.GetAccessCountAsync(_fx.ConnectionString, doc2, date);

        Assert.Equal(5, c1);
        Assert.Equal(12, c2);

        // Assert file moved to archive
        Assert.False(File.Exists(xmlPath));
        Assert.True(Directory.GetFiles(archiveDir, "*access-2026-01-21.xml").Any());
    }

    [Fact]
    public async Task ProcessFile_WhenSameDayExists_UpdatesCount_NotDuplicates()
    {
        // Arrange
        await BatchSchema.CreateMinimalSchemaAsync(_fx.ConnectionString);
        var doc1 = await BatchSchema.InsertDocumentAsync(_fx.ConnectionString, "doc1.pdf");

        var baseDir = Path.Combine(Path.GetTempPath(), "paperless-batch-it", Guid.NewGuid().ToString("N"));
        var inputDir = Path.Combine(baseDir, "input");
        var archiveDir = Path.Combine(baseDir, "archive");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(archiveDir);

        var date = new DateTime(2026, 01, 21);

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _fx.ConnectionString
            })
            .Build();

        var worker = new AccessBatchWorker(NullLogger<AccessBatchWorker>.Instance, cfg);

        // First run (count=5)
        var xml1 = $@"<accessStatistics date=""{date:yyyy-MM-dd}"">
  <document id=""{doc1}"" count=""5"" />
</accessStatistics>";
        var p1 = Path.Combine(inputDir, "access-2026-01-21.xml");
        await File.WriteAllTextAsync(p1, xml1);
        await worker.ProcessFile(p1, archiveDir, CancellationToken.None);

        // Second run (same key, new count=9) -> should UPDATE (ON CONFLICT)
        var p2 = Path.Combine(inputDir, "access-2026-01-21-2.xml");
        var xml2 = $@"<accessStatistics date=""{date:yyyy-MM-dd}"">
  <document id=""{doc1}"" count=""9"" />
</accessStatistics>";
        await File.WriteAllTextAsync(p2, xml2);
        await worker.ProcessFile(p2, archiveDir, CancellationToken.None);

        // Assert updated
        var c = await BatchSchema.GetAccessCountAsync(_fx.ConnectionString, doc1, date);
        Assert.Equal(9, c);
    }
}
