using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Paperless.Contracts.SharedServices
{
    public interface IElasticService
    {
        Task IndexAsync(MessageTransferObject doc, CancellationToken ct);
        Task<IEnumerable<MessageTransferObject>> SearchAsync(string term);
    }
    public class ElasticService : IElasticService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ElasticService> _logger;
        private readonly ElasticOptions _opts;

        public ElasticService(HttpClient httpClient, ILogger<ElasticService> logger, IOptions<ElasticOptions> opts)
        {
            _httpClient = httpClient;
            _logger = logger;
            _opts = opts.Value;
        }

        public async Task IndexAsync(MessageTransferObject doc, CancellationToken ct)
        {
            try
            {
                var uri = new Uri($"{_opts.Uri}/{_opts.Index}/_doc/{doc.DocumentId}");
                var payload = JsonSerializer.Serialize(doc);
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Put, uri)
                {
                    Content = content
                };
                var response = await _httpClient.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation("Document {DocumentId} indexed successfully in Elasticsearch.", doc.DocumentId);
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("Failed to index document {DocumentId}. Status Code: {StatusCode}, Response: {Response}",
                        doc.DocumentId, response.StatusCode, responseBody);
                    throw new Exception($"Elasticsearch indexing failed with status code {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing document {DocumentId} in Elasticsearch: {Message}", doc.DocumentId, ex.Message);
                throw;
            }
        }
        public async Task<IEnumerable<MessageTransferObject>> SearchAsync(string term)
        {
            var requestUri = $"{_opts.Index}/_search";

            _logger.LogInformation("Searching ES at {Uri} for term {Term}", requestUri, term);

            var fields = new[]
            {
        nameof(MessageTransferObject.OcrText),
        nameof(MessageTransferObject.Summary),
        nameof(MessageTransferObject.Tag)
    };

            var payload = JsonSerializer.Serialize(new
            {
                query = new
                {
                    multi_match = new
                    {
                        query = term,
                        fields = new[] { "OcrText", "Summary" }
                    }
                }
            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(requestUri, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("ES search failed: {Status} {Body}", response.StatusCode, body);
                throw new Exception($"Elasticsearch query failed: {response.StatusCode}");
            }

            var json = JsonDocument.Parse(body);
            var hits = json.RootElement
                .GetProperty("hits")
                .GetProperty("hits")
                .EnumerateArray();

            var results = new List<MessageTransferObject>();

            foreach (var hit in hits)
            {
                var source = hit.GetProperty("_source");
                var doc = source.Deserialize<MessageTransferObject>();
                if (doc != null)
                    results.Add(doc);
            }

            return results;
        }
    }
}
