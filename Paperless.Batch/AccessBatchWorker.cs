using Npgsql;
using System.Globalization;
using System.Xml.Linq;

namespace Paperless.Batch
{
    public class AccessBatchWorker : BackgroundService
    {
        private readonly ILogger<AccessBatchWorker> _logger;
        private readonly IConfiguration _config;

        public AccessBatchWorker(ILogger<AccessBatchWorker> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public async Task ProcessFile(string filePath, string archiveFolder, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Processing {file}", filePath);

            var (date, entries) = ParseXml(filePath);

            if (entries.Count == 0)
            {
                _logger.LogWarning("No entries in {File} -> archiving anyway", filePath);
                Archive(filePath, archiveFolder);
                return;
            }

            var connString = _config.GetConnectionString("ConnectionStrings__DefaultConnection")
                ?? throw new InvalidOperationException("Missing DefaultConnection connection");

            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync(stoppingToken);

            await using var tx = await conn.BeginTransactionAsync(stoppingToken);

            const string sql = @"
INSERT INTO ""AccessStatistics"" (""documentId"", ""accessDate"", ""accessCount"")
VALUES (@docId, @date, @count)
ON CONFLICT (""documentId"", ""accessDate"")
DO UPDATE SET ""accessCount"" = EXCLUDED.""accessCount"";
";
            foreach(var (docId, count) in entries)
            {
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                cmd.Parameters.AddWithValue("docId", docId);
                cmd.Parameters.AddWithValue("date", date);
                cmd.Parameters.AddWithValue("count", count);
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }

            await tx.CommitAsync(stoppingToken);

            _logger.LogInformation("Processed {File}: {Rows} rows for {Date}", filePath, entries.Count, date.ToString("yyyy-MM-dd"));
            Archive(filePath, archiveFolder);
        }

        public void Archive(string filePath, string archiveFolder)
        {
            var fileName = Path.GetFileName(filePath);
            var target = Path.Combine(archiveFolder, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
            File.Move(filePath, target);
            _logger.LogInformation("Archived {File} -> {Target}", filePath, target);
        }

        public static (DateTime date, List<(int docId, int count)> entries) ParseXml(string filePath)
        {
            var doc = XDocument.Load(filePath);

            var root = doc.Root ?? throw new FormatException("Missing root element");
            if (root.Name.LocalName != "accessStatistics")
                throw new FormatException("Root element must be <accessStatistics>");

            var dateAttr = root.Attribute("date")?.Value ?? throw new FormatException("Missing date attribute on <accessStatistics>");

            var date = DateTime.ParseExact(dateAttr, "yyyy-MM-dd",  CultureInfo.InvariantCulture);

            var entries = root.Elements("document")
                .Select(x =>
                {
                    var id = int.Parse(x.Attribute("id")?.Value ?? throw new FormatException("document missing id"));
                    var count = int.Parse(x.Attribute("count")?.Value ?? throw new FormatException("document missing count"));
                    return (id, count);
                })
                .ToList();

            return (date, entries);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            var baseDir = AppContext.BaseDirectory;
            var inputFolder = _config["Batch:InputFolder"] ?? Path.Combine(baseDir, "input");
            var archiveFolder = _config["Batch:ArchiveFolder"] ?? Path.Combine(baseDir, "archive");
            var pattern = _config["Batch:FilePattern"] ?? "access-*.xml";
            var pollSeconds = int.TryParse(_config["Batch:PollSeconds"], out var s) ? s : 10;

            Directory.CreateDirectory(inputFolder);
            Directory.CreateDirectory(archiveFolder);

            _logger.LogInformation("AccessBatchWorker started. Input={Input}, Archive={Archive}, Pattern={Pattern}, Poll={Poll}s",
                        inputFolder, archiveFolder, pattern, pollSeconds); 
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var files = Directory.GetFiles(inputFolder, pattern).OrderBy(f => f).ToList();
                    foreach (var file in files)
                    {
                        await ProcessFile(file, archiveFolder, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch loop error");
                }
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
    }
}
