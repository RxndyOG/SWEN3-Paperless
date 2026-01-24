using Npgsql;
using System.Globalization;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

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

            var connString =
            _config.GetConnectionString("DefaultConnection")
            ?? _config["ConnectionStrings:DefaultConnection"]
            ?? throw new InvalidOperationException("Missing connection string: ConnectionStrings:DefaultConnection");



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

            Directory.CreateDirectory(inputFolder);
            Directory.CreateDirectory(archiveFolder);


            _logger.LogInformation("CS={CS}", _config.GetConnectionString("DefaultConnection"));
            _logger.LogInformation("Mode={Mode} PollSeconds={Poll} Input={Input} Archive={Archive} Pattern={Pattern}",
                _config["Batch:Mode"], _config["Batch:PollSeconds"],
                _config["Batch:InputFolder"], _config["Batch:ArchiveFolder"], _config["Batch:FilePattern"]);

            var mode = (_config["Batch:Mode"] ?? "Poll").Trim(); // Poll | Daily
            var pollSeconds = int.TryParse(_config["Batch:PollSeconds"], out var s) ? s : 10;

            // Daily schedule settings
            var runAtStr = _config["Batch:RunAt"] ?? "01:00";     // HH:mm
            var tzId = _config["Batch:TimeZone"] ?? "Europe/Vienna";
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);

            _logger.LogInformation(
                "AccessBatchWorker started. Mode={Mode}, Input={Input}, Archive={Archive}, Pattern={Pattern}",
                mode, inputFolder, archiveFolder, pattern);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (mode.Equals("Daily", StringComparison.OrdinalIgnoreCase))
                    {
                        var delay = GetDelayUntilNextRun(runAtStr, timeZone);
                        _logger.LogInformation("Next batch run at {DelayFromNow} (RunAt={RunAt}, TZ={TZ})",
                            delay, runAtStr, timeZone.Id);

                        await Task.Delay(delay, stoppingToken);

                        await RunOnce(inputFolder, archiveFolder, pattern, stoppingToken);
                    }

                    else if (mode.Equals("Once", StringComparison.OrdinalIgnoreCase))
                    {
                        await RunOnce(inputFolder, archiveFolder, pattern, stoppingToken);
                        _logger.LogInformation("Batch:Mode=Once finished. Exiting.");
                        return;
                    }

                    else
                    {
                        // Poll mode (dev/testing)
                        await RunOnce(inputFolder, archiveFolder, pattern, stoppingToken);
                        await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch loop error");
                    // avoid tight crash loop
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task RunOnce(string inputFolder, string archiveFolder, string pattern, CancellationToken ct)
        {
            var files = Directory.GetFiles(inputFolder, pattern).OrderBy(f => f).ToList();

            if (files.Count == 0)
            {
                _logger.LogInformation("No batch files found in {Input} (Pattern={Pattern})", inputFolder, pattern);
                return;
            }

            _logger.LogInformation("Found {Count} batch file(s) to process", files.Count);

            foreach (var file in files)
            {
                await ProcessFile(file, archiveFolder, ct);
            }
        }

        private static TimeSpan GetDelayUntilNextRun(string runAtHHmm, TimeZoneInfo tz)
        {
            // Parse "01:00"
            if (!TimeSpan.TryParseExact(runAtHHmm, @"hh\:mm", CultureInfo.InvariantCulture, out var runAt))
                throw new FormatException($"Batch:RunAt must be HH:mm, got '{runAtHHmm}'");

            var nowUtc = DateTimeOffset.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, tz);

            var todayRunLocal = new DateTimeOffset(
                nowLocal.Year, nowLocal.Month, nowLocal.Day,
                runAt.Hours, runAt.Minutes, 0,
                nowLocal.Offset);

            var nextRunLocal = (nowLocal <= todayRunLocal)
                ? todayRunLocal
                : todayRunLocal.AddDays(1);

            var nextRunUtc = TimeZoneInfo.ConvertTime(nextRunLocal, TimeZoneInfo.Utc);

            var delay = nextRunUtc - nowUtc;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero; // safety
            return delay;
        }

    }
}
