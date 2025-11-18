using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using PaperlessOCR.Abstractions;
using System.Text.Json;
using Paperless.Contracts;

public class OcrMqResultSink : IOcrResultSink, IDisposable
{
    private readonly ILogger<OcrMqResultSink> _log;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly RabbitOptions _options;

    public OcrMqResultSink(ILogger<OcrMqResultSink> log, IOptions<RabbitOptions> options)
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

    public Task OnOcrCompletedAsync(int documentId, string text, CancellationToken ct)
    {
        try
        {
            _log.LogInformation("OCR finished: {ocrText}", text);

            var message = new MessageTransferObject { DocumentId = documentId, Text = text , Tag = DocumentTag.Default};

            var payload = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(payload);

            if(body == null)
                throw new NullReferenceException("Serialized message body is null");

            _channel.BasicPublish(
                exchange: "",
                routingKey: _options.OutputQueue,
                basicProperties: null,
                body: body);

            _log.LogInformation("Published OCR result for document {documentId}, to {Queue}", documentId, _options.OutputQueue);
            return Task.CompletedTask;
        }
        catch (NullReferenceException nex)
        {
            _log.LogError(nex, "Null reference encountered while publishing OCR result for document {documentId}", documentId);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to publish OCR result for document {documentId}", documentId);
            throw;
        }
    }

    public void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
    }
}