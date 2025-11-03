using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;
using PaperlessOCR.Abstractions;
using PaperlessOCR.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "RABBITMQ_");

builder.Services.Configure<RabbitOptions>(opts =>
{
    var cfg = builder.Configuration;
    opts.Host = cfg["HOST"] ?? "rabbitmq";
    opts.User = cfg["USER"] ?? "user";
    opts.Pass = cfg["PASS"] ?? "pass";
    opts.QueueName = cfg["QUEUE"] ?? "documents";
});

builder.Services.Configure<MinioOptions>(opts =>
{
    var cfg = builder.Configuration;
    opts.Endpoint = cfg["MINIO__ENDPOINT"] ?? "minio:9000";
    opts.AccessKey = cfg["MINIO__ACCESSKEY"] ?? "paperless";
    opts.SecretKey = cfg["MINIO__SECRETKEY"] ?? "paperlesssecret123";
    opts.UseSSL = (cfg["MINIO_USESSL"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
});

builder.Services.AddSingleton<IObjectFetcher, MinioObjectFetcher>();
builder.Services.AddSingleton<IOcrEngine, CliOcrEngine>();
builder.Services.AddSingleton<IOcrResultSink, LoggingOcrResultSink>();
builder.Services.AddHostedService<RabbitConsumerService>();

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var o = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
    return new MinioClient()
        .WithEndpoint(o.Endpoint)
        .WithCredentials(o.AccessKey, o.SecretKey)
        .WithSSL(o.UseSSL)
        .Build();
});

var host = builder.Build();
await host.RunAsync();



public class RabbitOptions
{
    public string Host { get; set; } = "rabbitmq";
    public string User { get; set; } = "user";
    public string Pass { get; set; } = "pass";
    public string QueueName { get; set; } = "documents";
}

public class MinioOptions
{
    public string Endpoint { get; set; } = "minio:9000";
    public string AccessKey { get; set; } = "paperless";
    public string SecretKey { get; set; } = "paperlesssecret123";
    public bool UseSSL { get; set; } = false;
}