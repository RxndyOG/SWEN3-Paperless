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

        public GenAiEngine(IOptions<GenAiOptions> genOptions)
        {
            _genOptions = genOptions.Value;
        }
        public async Task<string> SummarizeAsync(string textToSummarize, CancellationToken ct)
        {
            string ApiKey = _genOptions.ApiKey;
            var googleAi = new GoogleAi(ApiKey);
            var model = googleAi.CreateGenerativeModel("models/gemini-2.5-flash-lite");

            var response = await model.GenerateContentAsync($"Summarize what this text is about in a short description: {textToSummarize}");
            return response.Text;
        }
    }
}
