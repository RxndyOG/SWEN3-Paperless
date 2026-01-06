using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessOCR.Abstractions
{
    public interface IOcrResultSink
    {
        Task OnOcrCompletedAsync(int documentId, int documentVersionId, int? diffBaseVersionId, string text, CancellationToken ct);
    }
}
