using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using System.Net;

public sealed class MinioFixture : IAsyncLifetime
{
    public IContainer Container { get; private set; } = default!;
    public string Endpoint { get; private set; } = default!;
    public string AccessKey { get; } = "testminio";
    public string SecretKey { get; } = "testsecret";
    public string Bucket { get; } = "documents";

    public async Task InitializeAsync()
    {
        // Map random host ports → avoid conflicts.
        var minio = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithName($"minio-tests-{Guid.NewGuid():N}")
            .WithEnvironment("MINIO_ROOT_USER", AccessKey)
            .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
            .WithPortBinding(0, 9000) // S3 API
            .WithCommand("server", "/data", "--console-address", ":9001")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9000))
            .Build();

        Container = minio;
        await minio.StartAsync();

        // Use "localhost" as the host for the container's mapped port.
        var host = "localhost";
        var apiPort = minio.GetMappedPublicPort(9000);
        Endpoint = $"{host}:{apiPort}";
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}
