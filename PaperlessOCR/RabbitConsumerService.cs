using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

public class RabbitConsumerService : BackgroundService
{
    private readonly ILogger<RabbitConsumerService> _logger;
    private readonly RabbitOptions _opts;

    private IConnection? _conn;
    private IModel? _channel;

    public RabbitConsumerService(ILogger<RabbitConsumerService> logger, IOptions<RabbitOptions> opts)
    {
        _logger = logger;
        _opts = opts.Value;
    }

    public override Task StartAsync(System.Threading.CancellationToken cancellationToken)
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

        // Declare the queue (must match the publisher)
        _channel.QueueDeclare(
            queue: _opts.QueueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Prefetch 1 to keep it simple
        _channel.BasicQos(0, 1, false);

        _logger.LogInformation("Connected to RabbitMQ at {Host}. Listening on queue '{Queue}'",
            _opts.Host, _opts.QueueName);

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
    {
        if (_channel is null) throw new InvalidOperationException("Channel not initialized.");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                _logger.LogInformation("Received message: {Message}", message);

                // TODO: Plug OCR here later

                // ack
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                // reject but requeue (for now)
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }

            await Task.CompletedTask;
        };

        _channel.BasicConsume(queue: _opts.QueueName, autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    public override Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping consumer…");
        _channel?.Close();
        _conn?.Close();
        return base.StopAsync(cancellationToken);
    }
}
