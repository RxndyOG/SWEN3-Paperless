using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PaperlessOCR.Abstractions
{
    public interface IObjectFetcher
    {
        Task<string> FetchToTempFileAsync(string bucket, string objectKey, string originalFileName, CancellationToken ct);
    }
}
