using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using Paperless.Contracts;

namespace PaperlessREST.Services;

public interface IMessageQueueService
{
    void PublishTo(string message, string queueName);
}

public class MessageQueueService : IMessageQueueService, IDisposable
{
    private readonly IConnection? _connection;
    private readonly IModel? _channel;
    private readonly ILogger<MessageQueueService>? _logger;
    private bool _disposed;


    public MessageQueueService() { }
    public MessageQueueService(IConfiguration config, ILogger<MessageQueueService> logger)
    {
        _logger = logger;
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = config["RabbitMQ:Host"],
                UserName = config["RabbitMQ:User"],
                Password = config["RabbitMQ:Pass"]
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.QueueDeclare(queue: QueueNames.Documents,
                                  durable: false,
                                  exclusive: false,
                                  autoDelete: false,
                                  arguments: null);

            _channel.QueueDeclare(queue: QueueNames.OcrFinished,
                                  durable: false,
                                  exclusive: false,
                                  autoDelete: false,
                                  arguments: null);

            _channel.QueueDeclare(queue: QueueNames.GenAiFinished,
                                  durable: false,
                                  exclusive: false,
                                  autoDelete: false,
                                  arguments: null
                );

            _logger.LogInformation("RabbitMQ connection established and queues declared.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize RabbitMQ connection or declare queue.");
            throw;
        }
    }

    public void PublishTo(string message, string queueName)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(message);

            _channel.BasicPublish(exchange: "",
                                  routingKey: queueName,
                                  basicProperties: null,
                                  body: body);

            _logger.LogInformation($"Message published to {queueName}: {message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to publish message: {message}");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _channel?.Close();
            _connection?.Close();
            _logger.LogInformation("RabbitMQ connection and channel closed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during RabbitMQ resource cleanup.");
        }
        _disposed = true;
    }
}