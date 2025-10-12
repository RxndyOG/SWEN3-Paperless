using Microsoft.EntityFrameworkCore.Metadata;
using RabbitMQ.Client;
using System.Text;

namespace PaperlessREST.Services;

public class MessageQueueService : IDisposable
{
    private readonly IConnection _connection;
    private readonly RabbitMQ.Client.IModel _channel;

    public MessageQueueService(IConfiguration config)
    {
        var factory = new ConnectionFactory()
        {
            HostName = config["RabbitMQ:Host"],
            UserName = config["RabbitMQ:User"],
            Password = config["RabbitMQ:Pass"]
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.QueueDeclare(queue: "documents",
                              durable: false,
                              exclusive: false,
                              autoDelete: false,
                              arguments: null);
    }

    public void Publish(string message)
    {
        var body = Encoding.UTF8.GetBytes(message);
        _channel.BasicPublish(exchange: "",
                              routingKey: "documents",
                              basicProperties: null,
                              body: body);
        Console.WriteLine($"[x] Sent: {message}");
    }

    public void Dispose()
    {
        _channel.Close();
        _connection.Close();
    }
}
