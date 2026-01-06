using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Paperless.AI.Abstractions
{
    public interface IVersionTextClient
    {
        Task<string> GetVersionOcrTextAsync(int versionId, CancellationToken ct);
    }
}
