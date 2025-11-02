using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PaperlessREST.Services;
using System.Text;

public class MinioStorageTests : IClassFixture<MinioFixture>
{
    private readonly IObjectStorage _storage;
    private readonly MinioFixture _fx;

    public MinioStorageTests(MinioFixture fx)
    {
        _fx = fx;

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinIO:Endpoint"] = _fx.Endpoint,      // like "127.0.0.1:54321"
                ["MinIO:AccessKey"] = _fx.AccessKey,
                ["MinIO:SecretKey"] = _fx.SecretKey,
                ["MinIO:Bucket"] = _fx.Bucket,
                ["MinIO:UseSSL"] = "false",
            })
            .Build();

        _storage = new MinioStorage(cfg);
    }

    [Fact]
    public async Task Put_Get_Remove_Object_Roundtrip()
    {
        // Arrange
        var objectName = $"test/{Guid.NewGuid():N}.pdf";
        var payload = Encoding.UTF8.GetBytes("%PDF-1.4\n%…(tiny fake pdf payload)…");
        using var ms = new MemoryStream(payload);

        // Act: upload
        await _storage.PutObjectAsync(ms, objectName, "application/pdf");

        // Assert: download matches
        using var downloaded = await _storage.GetObjectAsync(objectName);
        var bytes = ((MemoryStream)downloaded).ToArray();
        bytes.Should().Equal(payload);

        // Presigned URL sanity (not fetching, just ensure it generates)
        var url = _storage.GetPresignedGetUrl(objectName, TimeSpan.FromMinutes(1));
        url.Should().NotBeNullOrWhiteSpace();
        url.Should().Contain(objectName);

        // Remove and verify failure on next get
        await _storage.RemoveObjectAsync(objectName);
        Func<Task> act = async () => await _storage.GetObjectAsync(objectName);
        await act.Should().ThrowAsync<Minio.Exceptions.MinioException>();
    }
}
