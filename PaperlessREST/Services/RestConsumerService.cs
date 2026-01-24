using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Paperless.Contracts;
using PaperlessREST.Data;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace PaperlessREST.Services;

public class RestConsumerService : BackgroundService
{
    private readonly ILogger<RestConsumerService> _logger;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;

    private IConnection? _conn;
    private IModel? _channel;

    public RestConsumerService(
        ILogger<RestConsumerService> logger,
        IServiceProvider services,
        IConfiguration config)
    {
        _logger = logger;
        _services = services;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var host = _config["RabbitMQ:Host"];
                var user = _config["RabbitMQ:User"];
                var pass = _config["RabbitMQ:Pass"];

                if (string.IsNullOrWhiteSpace(host) ||
                    string.IsNullOrWhiteSpace(user) ||
                    string.IsNullOrWhiteSpace(pass))
                {
                    _logger.LogWarning("RabbitMQ config missing. Host/User/Pass not set. Retrying in 5s...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var factory = new ConnectionFactory
                {
                    HostName = host,
                    UserName = user,
                    Password = pass,
                    DispatchConsumersAsync = true,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
                };

                _logger.LogInformation("Connecting to RabbitMQ at {Host}...", host);

                _conn = factory.CreateConnection();
                _channel = _conn.CreateModel();

                _channel.QueueDeclare(
                    queue: QueueNames.GenAiFinished,
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                _logger.LogInformation("REST service consuming queue '{Queue}'", QueueNames.GenAiFinished);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += HandleMessageAsync;

                _channel.BasicConsume(
                    queue: QueueNames.GenAiFinished,
                    autoAck: false,
                    consumer: consumer);

                // Wait until cancelled. (Consumer runs via events.)
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer failed to start or crashed. Retrying in 5s...");
                SafeClose();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void SafeClose()
    {
        try { _channel?.Close(); } catch { }
        try { _conn?.Close(); } catch { }
        _channel = null;
        _conn = null;
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("REST received GenAI result: {json}", json);

            var msg = JsonSerializer.Deserialize<GenAiCompletedMessage>(json);
            if (msg == null)
            {
                _logger.LogWarning("GenAI message deserialized to null. Acking.");
                _channel?.BasicAck(ea.DeliveryTag, false);
                return;
            }

            if (msg.DocumentVersionId <= 0)
            {
                _logger.LogWarning("GenAI message missing DocumentVersionId (DocumentId={DocumentId}). Acking.", msg.DocumentId);
                _channel?.BasicAck(ea.DeliveryTag, false);
                return;
            }

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            //Update the specific version row (NOT current version!)
            var ver = await db.DocumentVersions
                .FirstOrDefaultAsync(v => v.Id == msg.DocumentVersionId);

            if (ver == null)
            {
                _logger.LogWarning(
                    "DocumentVersion {VersionId} not found for DocumentId {DocumentId}. Acking.",
                    msg.DocumentVersionId, msg.DocumentId);

                _channel?.BasicAck(ea.DeliveryTag, false);
                return;
            }

            if (ver.DocumentId != msg.DocumentId)
            {
                _logger.LogWarning(
                    "Mismatch: message DocumentId={MessageDocId} but version {VersionId} belongs to DocumentId={DbDocId}. Acking.",
                    msg.DocumentId, ver.Id, ver.DocumentId);

                _channel?.BasicAck(ea.DeliveryTag, false);
                return;
            }

            ver.SummarizedContent = msg.Summary ?? "";
            ver.Tag = msg.Tag;

            if (!string.IsNullOrWhiteSpace(msg.OcrText))
                ver.Content = msg.OcrText;

            if (!string.IsNullOrWhiteSpace(msg.ChangeSummary))
                ver.ChangeSummary = msg.ChangeSummary;

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Updated GenAI fields for DocumentId {DocumentId}, VersionId {VersionId}",
                msg.DocumentId, ver.Id);

            _channel?.BasicAck(ea.DeliveryTag, false);
        }
        catch (JsonException jex)
        {
            _logger.LogError(jex, "Bad GenAI message JSON. Dropping message.");
            _channel?.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process GenAI message. Requeueing.");
            _channel?.BasicNack(ea.DeliveryTag, false, requeue: true);
        }
    }


    public override void Dispose()
    {
        _channel?.Close();
        _conn?.Close();
        base.Dispose();
    }
}