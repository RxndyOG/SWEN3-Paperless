using Paperless.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessAI.Abstractions
{
    public interface IGenAiResultSink
    {
        Task OnGeminiCompletedAsync(int documentId, string summarizedText, DocumentTag tag, string ocrText, CancellationToken ct);
    }
}
