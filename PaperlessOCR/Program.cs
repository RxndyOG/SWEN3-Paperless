using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;
using PaperlessOCR.Abstractions;
using PaperlessOCR.Services;
using Paperless.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "RABBITMQ_");
builder.Configuration.AddEnvironmentVariables(prefix: "MINIO__");


builder.Services.Configure<RabbitOptions>(opts =>
{
    var cfg = builder.Configuration;
    opts.Host = cfg["HOST"] ?? "rabbitmq";
    opts.User = cfg["USER"] ?? "user";
    opts.Pass = cfg["PASS"] ?? "pass";
    opts.InputQueue = cfg["INPUTQUEUE"] ?? QueueNames.Documents;
    opts.OutputQueue = cfg["OCR_FINISHED_QUEUE"] ?? QueueNames.OcrFinished;
});

builder.Services.Configure<MinioOptions>(opts =>
{
    var cfg = builder.Configuration;
    opts.Endpoint = cfg["ENDPOINT"]!;
    opts.AccessKey = cfg["ACCESSKEY"]!;
    opts.SecretKey = cfg["SECRETKEY"]!;
    opts.UseSSL = (cfg["USESSL"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);
});

builder.Services.AddSingleton<IObjectFetcher, MinioObjectFetcher>();
builder.Services.AddSingleton<IOcrEngine, CliOcrEngine>();
builder.Services.AddSingleton<IOcrResultSink, OcrMqResultSink>();
builder.Services.AddHostedService<OcrConsumerService>();

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
