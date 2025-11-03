using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Minio;
using Minio.DataModel.Args;
using PaperlessOCR.Abstractions;

namespace PaperlessOCR.Services
{
    public class MinioObjectFetcher : IObjectFetcher
    {
        private readonly IMinioClient _minio;
        public MinioObjectFetcher(IMinioClient minio) => _minio = minio;

        public async Task<string> FetchToTempFileAsync(string bucket, string objectKey, string originalFileName, CancellationToken ct)
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "paperless-ocr");
            Directory.CreateDirectory(tmpDir);
            var tmpFile = Path.Combine(tmpDir, $"{Guid.NewGuid():N}-{originalFileName}");

            await using var fs = File.Create(tmpFile);
            await _minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithCallbackStream(s => s.CopyTo(fs)), cancellationToken: ct);

            return tmpFile;
        }
    }
}
