using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Minio;
using Minio.DataModel.Args;
using PaperlessOCR.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;
using Paperless.Contracts;

public interface IRabbitConsumerService
{
    Task StartAsync(System.Threading.CancellationToken cancellationToken);
    Task StopAsync(System.Threading.CancellationToken cancellationToken);
}

public class OcrConsumerService : BackgroundService, IRabbitConsumerService
{
    private readonly ILogger<OcrConsumerService> _logger;
    private readonly RabbitOptions _opts;
    private readonly IMinioClient _minio;
    private readonly IObjectFetcher _fetcher;
    private readonly IOcrEngine _ocr;
    private readonly IOcrResultSink _sink;

    private IConnection? _conn;
    private IModel? _channel;

    public OcrConsumerService(ILogger<OcrConsumerService> logger, IOptions<RabbitOptions> opts, IMinioClient minio, IObjectFetcher fetcher, IOcrEngine ocr, IOcrResultSink sink)
    {
        _logger = logger;
        _opts = opts.Value;
        _minio = minio;
        _fetcher = fetcher;
        _ocr = ocr;
        _sink = sink;
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
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _conn = factory.CreateConnection();
            _channel = _conn.CreateModel();

            _channel.QueueDeclare(
                queue: _opts.InputQueue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _channel.BasicQos(0, 1, false);

            _logger.LogInformation("Connected to RabbitMQ at {Host}. Listening on queue '{Queue}'",
                _opts.Host, _opts.InputQueue);
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
                var payload = JsonSerializer.Deserialize<VersionPipelineMessage>(message)!;
                await ProcessAsync(payload, stoppingToken);

                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message. DeliveryTag: {Tag}", ea.DeliveryTag);
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
            _channel.BasicConsume(queue: _opts.InputQueue, autoAck: false, consumer: consumer);
            _logger.LogInformation("Started consuming queue '{Queue}'", _opts.InputQueue);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to start consuming queue '{Queue}'", _opts.InputQueue);
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

    public async Task ProcessAsync(VersionPipelineMessage payload, CancellationToken ct)
    {
        _logger.LogInformation("OCR worker processing doc={DocId} ver={VerId} key={Key}",
            payload.DocumentId, payload.DocumentVersionId, payload.ObjectKey);

        var path = await _fetcher.FetchToTempFileAsync(payload.Bucket, payload.ObjectKey, payload.FileName, ct);

        var text = await _ocr.ExtractAsync(path, payload.ContentType, ct);

        _logger.LogInformation("OCR extracted {Len} chars for doc={DocId} ver={VerId}",
            text?.Length ?? 0, payload.DocumentId, payload.DocumentVersionId);

        await _sink.OnOcrCompletedAsync(
        payload.DocumentId,
        payload.DocumentVersionId,
        payload.DiffBaseVersionId,
        text,
        ct);
    }

}
