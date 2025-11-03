using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessOCR.Abstractions
{
    public interface IOcrResultSink
    {
        Task OnOcrCompletedAsync(int documentId, string text, CancellationToken ct);
    }
}
