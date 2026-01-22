using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Paperless.Contracts;
using Paperless.Contracts.SharedServices;
using PaperlessREST.Data;
using PaperlessREST.Services;
using System.Net;
using Testcontainers.PostgreSql;

public sealed class TestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly IContainer _minio;

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string MinioEndpoint { get; private set; } = default!;

    public TestWebAppFactory()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("paperless")
            .WithUsername("test")
            .WithPassword("fhtw")
            .Build();

        // MinIO container (S3-compatible)
        _minio = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithName($"minio-it-{Guid.NewGuid():N}")
            .WithEnvironment("MINIO_ROOT_USER", "paperless")
            .WithEnvironment("MINIO_ROOT_PASSWORD", "paperlesssecret123")
            .WithPortBinding(0, 9000)  // random host port -> 9000 in container
            .WithPortBinding(0, 9001)  // console
            .WithCommand("server", "/data", "--console-address", ":9001")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(9000).ForPath("/minio/health/ready").ForStatusCode(HttpStatusCode.OK)))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _minio.StartAsync();

        var hostPort9000 = _minio.GetMappedPublicPort(9000);
        MinioEndpoint = $"localhost:{hostPort9000}";
    }

    public new async Task DisposeAsync()
    {
        await _minio.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = PostgresConnectionString,

                ["MinIO:Endpoint"] = MinioEndpoint,
                ["MinIO:AccessKey"] = "paperless",
                ["MinIO:SecretKey"] = "paperlesssecret123",
                ["MinIO:UseSSL"] = "false",
                ["MinIO:Bucket"] = "documents"
            };

            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(PostgresConnectionString));

            services.RemoveAll(typeof(IMessageQueueService));
            services.AddSingleton<IMessageQueueService, NoOpMessageQueueService>();

            services.RemoveAll(typeof(IElasticService));
            services.AddSingleton<IElasticService, ElasticServiceStub>();
        });
    }


    // --- stubs ---
    public sealed class NoOpMessageQueueService : IMessageQueueService
    {
        public void PublishTo(string message, string queueName) { /* no-op */ }
    }

    private sealed class ElasticServiceStub : IElasticService
    {
        public Task IndexAsync(MessageTransferObject doc, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<IEnumerable<MessageTransferObject>> SearchAsync(string term)
        {
            return Task.FromResult<IEnumerable<MessageTransferObject>>(
                Array.Empty<MessageTransferObject>());
        }
    }
}
