using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using System.Security.Cryptography;

namespace PaperlessREST.Services
{
    public sealed class MinioOptions
    {
        public string Endpoint { get; set; } = "minio:9000";
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public string Bucket { get; set; } = "documents";
        public bool UseSSL { get; set; } = false;
    }

    public interface IObjectStorage
    {
        Task EnsureBucketAsync(CancellationToken ct = default);
        Task<string> PutObjectAsync(Stream content, string objectName, string contentType, CancellationToken ct = default);
        Task<Stream> GetObjectAsync(string objectName, CancellationToken ct = default);
        Task RemoveObjectAsync(string objectName, CancellationToken ct = default);
        string GetPresignedGetUrl(string objectName, TimeSpan expires);
    }

    public sealed class MinioStorage : IObjectStorage
    {
        private readonly MinioOptions _opts;
        private readonly IMinioClient _client;

        public MinioStorage(IConfiguration cfg)
        {
            _opts = cfg.GetSection("MinIO").Get<MinioOptions>() ?? new MinioOptions();
            _client = new MinioClient()
                .WithEndpoint(_opts.Endpoint)
                .WithCredentials(_opts.AccessKey, _opts.SecretKey)
                .WithSSL(_opts.UseSSL)
                .Build();
        }

        public async Task EnsureBucketAsync(CancellationToken ct = default)
        {
            var bExists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_opts.Bucket), ct);
            if (!bExists)
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_opts.Bucket), ct);
        }

        public async Task<string> PutObjectAsync(Stream content, string objectName, string contentType, CancellationToken ct = default)
        {
            await EnsureBucketAsync(ct);
            content.Position = 0;
            await _client.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_opts.Bucket)
                .WithObject(objectName)
                .WithStreamData(content)
                .WithObjectSize(content.Length)
                .WithContentType(contentType), ct);
            return objectName;
        }

        public async Task<Stream> GetObjectAsync(string objectName, CancellationToken ct = default)
        {
            var ms = new MemoryStream();
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_opts.Bucket)
                .WithObject(objectName)
                .WithCallbackStream(s => s.CopyTo(ms)), ct);
            ms.Position = 0;
            return ms;
        }

        public async Task RemoveObjectAsync(string objectName, CancellationToken ct = default)
        {
            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_opts.Bucket)
                .WithObject(objectName), ct);
        }

        public string GetPresignedGetUrl(string objectName, TimeSpan expires)
        => _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
           .WithBucket(_opts.Bucket)
           .WithObject(objectName)
           .WithExpiry((int)expires.TotalSeconds)).GetAwaiter().GetResult();
    }
}
