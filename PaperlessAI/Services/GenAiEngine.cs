using GenerativeAI;
using Microsoft.Extensions.Options;
using PaperlessAI.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessAI.Services
{
    public class GenAiEngine : IGenAiEngine
    {
        private readonly GenAiOptions _genOptions;
        private readonly ILogger<GenAiEngine> _log;

        public GenAiEngine(IOptions<GenAiOptions> genOptions, ILogger<GenAiEngine> log)
        {
            _genOptions = genOptions.Value;
            _log = log;
        }
        public async Task<string> SummarizeAsync(string textToSummarize, CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested)
                    ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(textToSummarize))
                    throw new ArgumentException("Text to summarize cannot be null or empty.", nameof(textToSummarize));

                var apiKey = _genOptions.ApiKey;
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("API Key is not configured.");

                var googleAi = new GoogleAi(apiKey);
                var model = googleAi.CreateGenerativeModel("models/gemini-2.5-flash-lite");
                if (model == null)
                    throw new InvalidOperationException("Generative model could not be created.");

                var response = await model.GenerateContentAsync(
                    $"Summarize what this text is about in a short description: {textToSummarize}");

                return response.Text;
            }
            catch (ArgumentException aex)
            {
                _log.LogError(aex, "Argument error during summarization: {Message}", aex.Message);
                throw;
            }
            catch (InvalidOperationException oex)
            {
                _log.LogError(oex, "Invalid operation during summarization: {Message}", oex.Message);
                throw;
            }
            catch (OperationCanceledException ocex)
            {
                _log.LogWarning(ocex, "Summarization operation was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Error during summarization", ex);
            }
        }

    }
}
