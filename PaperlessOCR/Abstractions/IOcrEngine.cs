using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessOCR.Abstractions
{
    public interface IOcrEngine
    {
        Task<string> ExtractAsync(string inputPath, string contentType, CancellationToken ct);
    }
}
