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

public class MessageConsumerService : BackgroundService
{
    private readonly ILogger<MessageConsumerService> _logger;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;

    private IConnection? _conn;
    private IModel? _channel;

    public MessageConsumerService(
        ILogger<MessageConsumerService> logger,
        IServiceProvider services,
        IConfiguration config)
    {
        _logger = logger;
        _services = services;
        _config = config;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMQ:Host"],
            UserName = _config["RabbitMQ:User"],
            Password = _config["RabbitMQ:Pass"],
            DispatchConsumersAsync = true
        };

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

        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("REST received summary: {json}", json);

            var msg = JsonSerializer.Deserialize<MessageTransferObject>(json);

            if (msg != null)
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var doc = await db.Documents.FindAsync(msg.DocumentId);
                if (doc == null)
                {
                    _logger.LogWarning("Document {Id} not found", msg.DocumentId);
                }
                else
                {
                    doc.SummarizedContent = msg.Text;
                    await db.SaveChangesAsync();
                    _logger.LogInformation("Updated summary for document {Id}", msg.DocumentId);
                }
            }

            _channel?.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process GenAI summary");
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