using GenerativeAI;
using Microsoft.Extensions.Options;
using Paperless.Contracts;
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

                //limit maximum characters to not blow up api tokens
                const int maxChars = 20000;
                if (textToSummarize.Length > maxChars)
                    textToSummarize = textToSummarize[..maxChars];

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

        public async Task<string> ChangeSummaryAsync(string oldText, string newText, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var apiKey = _genOptions.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("API Key is not configured.");

            var googleAi = new GoogleAi(apiKey);
            var model = googleAi.CreateGenerativeModel("models/gemini-2.5-flash-lite");
            if (model == null)
                throw new InvalidOperationException("Generative model could not be created.");

            static string Clip(string s, int max) =>
                string.IsNullOrEmpty(s) ? "" :
                s.Length <= max ? s :
                s.Substring(0, max / 2) + "\n...\n" + s.Substring(s.Length - max / 2);

            var oldC = Clip(oldText, 12000);
            var newC = Clip(newText, 12000);

            var prompt = $"""
You are generating a concise change summary between two document versions.

OLD VERSION TEXT:
<<<{oldC}>>>

NEW VERSION TEXT:
<<<{newC}>>>

Rules:
- Output 3–8 bullet points.
- Focus on what changed (added/removed/modified).
- If mostly OCR noise/formatting, say so.
- Do not restate the whole document.
Return only the bullet list.
""";

            var response = await model.GenerateContentAsync(prompt, cancellationToken: ct);
            return response.Text ?? "";
        }

        public async Task<DocumentTag> ClassifyAsync(string textToClassify, CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested)
                    ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(textToClassify))
                    throw new ArgumentException("Text to classify cannot be null or empty.", nameof(textToClassify));

                var apiKey = _genOptions.ApiKey;
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("API Key is not configured.");

                const int maxChars = 20000;
                if (textToClassify.Length > maxChars)
                    textToClassify = textToClassify[..maxChars];

                var googleAi = new GoogleAi(apiKey);
                var model = googleAi.CreateGenerativeModel("models/gemini-2.5-flash-lite");
                if (model == null)
                    throw new InvalidOperationException("Generative model could not be created.");

                var prompt = $@"
                Classify the following text into EXACTLY ONE of the following categories:

                - Invoice
                - Contract
                - Personal
                - Education
                - Medical
                - Finance
                - Legal
                - Other

                ANSWER WITH ONLY THE CATEGORY NAME. NOTHING ELSE.

                Text:
                {textToClassify}
                ";

                var response = await model.GenerateContentAsync(prompt, cancellationToken: ct);

                var output = response.Text.Trim();

                if (Enum.TryParse<DocumentTag>(output, ignoreCase: true, out var tag))
                    return tag;

                // fallback: try partial match (LLMs sometimes give surrounding quotes)
                foreach (var name in Enum.GetNames<DocumentTag>())
                {
                    if (output.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return Enum.Parse<DocumentTag>(name);
                }

                // fallback category
                return DocumentTag.Other;
            }
            catch (ArgumentException aex)
            {
                _log.LogError(aex, "Argument error during classification: {Message}", aex.Message);
                throw;
            }
            catch (InvalidOperationException oex)
            {
                _log.LogError(oex, "Invalid operation during classification: {Message}", oex.Message);
                throw;
            }
            catch (OperationCanceledException ocex)
            {
                _log.LogWarning(ocex, "Classification operation was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Error during classification", ex);
            }
        }

    }
}
