using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
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
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                _logger.LogInformation("Received message: {Message}", message);

                //Plug OCR here later

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
}
