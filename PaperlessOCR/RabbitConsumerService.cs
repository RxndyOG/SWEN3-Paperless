using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

public interface IRabbitConsumerService
{
    Task StartAsync(System.Threading.CancellationToken cancellationToken);
    Task StopAsync(System.Threading.CancellationToken cancellationToken);
}

public record UploadedDocMessage(int DocumentId, string Bucket, string ObjectKey, string FileName, string ContentType);


public class RabbitConsumerService : BackgroundService, IRabbitConsumerService
{
    private readonly ILogger<RabbitConsumerService> _logger;
    private readonly RabbitOptions _opts;
    private readonly IMinioClient _minio;

    private IConnection? _conn;
    private IModel? _channel;

    public RabbitConsumerService(ILogger<RabbitConsumerService> logger, IOptions<RabbitOptions> opts, IMinioClient minio)
    {
        _logger = logger;
        _opts = opts.Value;
        _minio = minio;
    }

    public override async Task StartAsync(System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _opts.Host,
                UserName = _opts.User,
                Password = _opts.Pass,
                DispatchConsumersAsync = true
            };

            _conn = factory.CreateConnection();
            _channel = _conn.CreateModel();

            _channel.QueueDeclare(
                queue: _opts.QueueName,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _channel.BasicQos(0, 1, false);

            _logger.LogInformation("Connected to RabbitMQ at {Host}. Listening on queue '{Queue}'",
                _opts.Host, _opts.QueueName);
        }
        catch (BrokerUnreachableException ex)
        {
            _logger.LogCritical(ex, "Could not reach RabbitMQ broker at {Host}", _opts.Host);
            throw; // Let the host handle fatal startup errors
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unexpected error during RabbitMQ startup");
            throw;
        }

        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
    {
        if (_channel is null)
        {
            _logger.LogCritical("RabbitMQ channel not initialized. Service cannot start.");
            throw new InvalidOperationException("Channel not initialized.");
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var tag = ea.DeliveryTag;
            try
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var payload = JsonSerializer.Deserialize<UploadedDocMessage>(message);
                if (payload is null)
                    throw new InvalidOperationException("Invalid message payload");

                _logger.LogInformation("OCR: Received doc {Id} {File} ({Type})", payload.DocumentId, payload.FileName, payload.ContentType);

                var tmpDir = Path.Combine(Path.GetTempPath(), "paperless-ocr");
                Directory.CreateDirectory(tmpDir);
                var tmpFile = Path.Combine(tmpDir, $"{Guid.NewGuid():N}-{payload.FileName}");

                await using (var fs = File.Create(tmpFile))
                {
                    await _minio.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
                        .WithBucket(payload.Bucket)
                        .WithObject(payload.ObjectKey)
                        .WithCallbackStream(s => s.CopyTo(fs)), cancellationToken: stoppingToken);
                }

                string text;
                // pdftoppm -png -singlefile inPath outBase
                var outBase = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(tmpFile));
                RunOrThrow(
                  "pdftoppm",
                  $"-png -f 1 -l 1 \"{tmpFile}\" \"{outBase}\""
                );

                var png = outBase + "-1.png";

                text = RunTesseractToText(png);

                TryDelete(png);

                _logger.LogInformation("OCR: Document {Id} text (first ~400 chars): {Preview}",
                    payload.DocumentId,
                    text.Length > 400 ? text.Substring(0, 400) + "..." : text);

                // TODO: update DB with recognized text later

                TryDelete(tmpFile);

                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message. DeliveryTag: {Tag}", ea.DeliveryTag);
                //reject but requeue (for now)
                try
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                }
                catch (Exception nackEx)
                {
                    _logger.LogCritical(nackEx, "Failed to NACK message. DeliveryTag: {Tag}", ea.DeliveryTag);
                }
            }

            await Task.CompletedTask;
        };

        try
        {
            _channel.BasicConsume(queue: _opts.QueueName, autoAck: false, consumer: consumer);
            _logger.LogInformation("Started consuming queue '{Queue}'", _opts.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to start consuming queue '{Queue}'", _opts.QueueName);
            throw;
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping RabbitMQ consumer service...");
        try
        {
            _channel?.Close();
            _conn?.Close();
            _logger.LogInformation("RabbitMQ connection and channel closed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RabbitMQ shutdown.");
        }
        await base.StopAsync(cancellationToken);
    }

    public static string RunTesseractToText(string imgPath)
    {
        var psi = new ProcessStartInfo("tesseract", $"\"{imgPath}\" stdout")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ERROR: Tesseract failed: {p.StandardError.ReadToEnd()}");
        return output;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void RunOrThrow(string fileName, string args)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed: {p.StandardError.ReadToEnd()}");
    }
}
