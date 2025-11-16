using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessAI.Abstractions
{
    public interface IGenAiEngine
    {
        Task<string> SummarizeAsync(string textToSummarize, CancellationToken ct);
    }
}
