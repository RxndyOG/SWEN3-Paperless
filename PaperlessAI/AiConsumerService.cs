using GenerativeAI;
using GenerativeAI.Types.RagEngine;
using Microsoft.Extensions.Options;
using Paperless.Contracts;
using PaperlessAI.Abstractions;
using PaperlessAI.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Rest;
using Paperless.AI.Abstractions;
namespace PaperlessAI
{
    public class AiConsumerService : BackgroundService
    {
        private readonly ILogger<AiConsumerService> _logger;
        private readonly RabbitOptions _opts;
        private readonly IGenAiEngine _genEngine;
        private readonly IGenAiResultSink _genResultSink;
        private readonly IVersionTextClient _restClient;

        private IConnection? _conn;
        private IModel? _channel;
        public AiConsumerService(
            ILogger<AiConsumerService> logger,
            IOptions<RabbitOptions> opts,
            IOptions<GenAiOptions> genOptions,
            IGenAiEngine genEngine,
            IGenAiResultSink genResultSink,
            IVersionTextClient restClient)
        {
            _logger = logger;
            _opts = opts.Value;
            _genEngine = genEngine;
            _genResultSink = genResultSink;
            _restClient = restClient;
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

        protected override Task ExecuteAsync(System.Threading.CancellationToken ct)
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
                    _logger.LogInformation("Raw message from ocr_finished: {json}", message);
                    var payload = JsonSerializer.Deserialize<OcrCompletedMessage>(message)!;
                    await ProcessAsync(payload, ct);

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

        public async Task ProcessAsync(OcrCompletedMessage message, CancellationToken ct)
        {
            _logger.LogInformation("Received text from OCR: doc={DocId} ver={VerId} base={BaseId}",
                message.DocumentId, message.DocumentVersionId, message.DiffBaseVersionId);

            var summary = await _genEngine.SummarizeAsync(message.OcrText, ct);
            var tag = await _genEngine.ClassifyAsync(message.OcrText, ct);

            string changeSummary;

            if (message.DiffBaseVersionId is null)
            {
                changeSummary = "Initial version.";
            }
            else
            {
                // 1) fetch base version OCR from REST
                var baseText = await _restClient.GetVersionOcrTextAsync(message.DiffBaseVersionId.Value, ct);

                // 2) ask Gemini for change summary
                changeSummary = await _genEngine.ChangeSummaryAsync(baseText, message.OcrText, ct);
            }

            await _genResultSink.OnGeminiCompletedAsync(
                message.DocumentId,
                message.DocumentVersionId,
                summary,
                tag,
                message.OcrText,
                changeSummary,
                ct);
        }

    }
}
