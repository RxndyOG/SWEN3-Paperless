using GenerativeAI.Types;
using Microsoft.Extensions.Options;
using PaperlessAI.Abstractions;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Text.Json;

namespace PaperlessAI.Services
{
    public class AiMqResultSink : IGenAiResultSink, IDisposable
    {
        private readonly ILogger<AiMqResultSink> _log;
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly RabbitOptions _options;

        public AiMqResultSink(ILogger<AiMqResultSink> log, IOptions<RabbitOptions> options)
        {
            _log = log;
            _options = options.Value;

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                UserName = _options.User,
                Password = _options.Pass
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _channel.QueueDeclare(
                queue: _options.OutputQueue,
                durable: false,
                autoDelete: false,
                exclusive: false,
                arguments: null);
        }

        public Task OnGeminiCompletedAsync(int documentId, string text, CancellationToken ct)
        {
            _log.LogInformation("Reached ResultSink! {id}, {text}", documentId, text);

            var message = new SummarizedText { Summary = text, DocumentId = documentId };
            var payload = JsonSerializer.Serialize<SummarizedText>(message);
            var body = Encoding.UTF8.GetBytes(payload);

            _channel.BasicPublish(
                exchange: "",
                routingKey: _options.OutputQueue,
                body: body,
                basicProperties: null
                );

            _log.LogInformation("Published summarized text for document {id} to {queue}", documentId, _options.OutputQueue);
            _log.LogInformation("Message that was published: {payload}", Encoding.UTF8.GetString(body));

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
        }
    }
    public class SummarizedText
    {
        public required string Summary { get; set; }
        public int DocumentId { get; set; }
    }
}
