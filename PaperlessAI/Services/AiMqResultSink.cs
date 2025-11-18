using GenerativeAI.Types;
using Microsoft.Extensions.Options;
using Paperless.Contracts;
using PaperlessAI.Abstractions;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PaperlessAI.Services
{
    public class AiMqResultSink : IGenAiResultSink, IDisposable
    {
        private readonly ILogger<AiMqResultSink> _log;
        private readonly IModel _channel;
        private readonly RabbitOptions _options;
        private readonly IConnection _connection;

        public AiMqResultSink(
            ILogger<AiMqResultSink> log,
            IOptions<RabbitOptions> options,
            IConnection connection)
        {
            _log = log;
            _options = options.Value;
            _connection = connection;

            _channel = connection.CreateModel();
            _channel.QueueDeclare(
                queue: _options.OutputQueue,
                durable: false,
                autoDelete: false,
                exclusive: false,
                arguments: null);
        }

        public Task OnGeminiCompletedAsync(int documentId, string text, DocumentTag tag, CancellationToken ct)
        {
            try
            {
                var message = new MessageTransferObject { DocumentId = documentId, Text = text , Tag = tag};
                var payload = JsonSerializer.Serialize<MessageTransferObject>(message);
                var body = Encoding.UTF8.GetBytes(payload);

                if(ct.IsCancellationRequested)
                {
                    _log.LogWarning("Cancellation requested before publishing summarized text for document {id}", documentId);
                    ct.ThrowIfCancellationRequested();
                }
                if(message.Text == null)
                {
                    _log.LogWarning("Summary is null for document {id}", documentId);
                    throw new NullReferenceException("Summary is null");
                }
                if(payload == null)
                {
                    _log.LogWarning("No summarized text to publish for document {id}", documentId);
                    throw new NullReferenceException("Summary is null");
                }

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
            catch(NullReferenceException nex)
            {
                _log.LogError(nex, "Summary was null for document {id}, not publishing to {queue}", documentId, _options.OutputQueue);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error publishing summarized text for document {id} to {queue}", documentId, _options.OutputQueue);
                throw;
            }
        }

        public void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
        }
    }
}
