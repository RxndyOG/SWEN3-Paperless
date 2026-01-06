using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Rest;
using Paperless.AI.Abstractions;

namespace Paperless.AI.Services
{
    public class VersionTextClient : IVersionTextClient
    {
        private readonly HttpClient _http;

        public VersionTextClient(HttpClient http) => _http = http;

        public async Task<string> GetVersionOcrTextAsync(int versionId, CancellationToken ct)
        {
            var res = await _http.GetAsync($"api/documents/versions/{versionId}/ocr", ct);
            res.EnsureSuccessStatusCode();

            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return json.RootElement.GetProperty("ocrText").GetString() ?? "";
        }
    }

}
