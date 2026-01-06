using Microsoft.Extensions.Options;
using Paperless.Contracts;
using Paperless.Contracts.SharedServices;
using PaperlessAI;
using PaperlessAI.Abstractions;
using PaperlessAI.Services;
using RabbitMQ.Client;
using System.Runtime.InteropServices;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Text;
using Paperless.AI.Abstractions;
using Paperless.AI.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "RABBITMQ_");

builder.Services.Configure<RabbitOptions>(opts =>
{
    var cfg = builder.Configuration;
    opts.Host = cfg["HOST"] ?? "rabbitmq";
    opts.User = cfg["USER"] ?? "user";
    opts.Pass = cfg["PASS"] ?? "pass";
    opts.InputQueue = cfg["INPUTQUEUE"] ?? QueueNames.OcrFinished;
    opts.OutputQueue = cfg["GENAI_FINISHED"] ?? QueueNames.GenAiFinished;
});

builder.Services.Configure<GenAiOptions>(opts =>
{
    var cfg = builder.Configuration;
    opts.ApiKey = cfg["GEMINI_API_KEY"] ?? "dummy";
});

builder.Services.Configure<ElasticOptions>(cfg =>
{
    var c = builder.Configuration;
    cfg.Uri = c["Elastic:Uri"] ?? "https://es01:9200";
    cfg.Index = c["Elastic:Index"] ?? "paperless-documents";
    cfg.Username = c["Elastic:User"] ?? "elastic";
    cfg.Password = c["ELASTIC_PASSWORD"] ?? "";
});

builder.Services.AddHttpClient<IElasticService, ElasticService>()
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
        client.BaseAddress = new Uri(opts.Uri);

        // Basic auth – good enough for sprint demo
        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{opts.Username}:{opts.Password}"));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
    })
    .ConfigurePrimaryHttpMessageHandler(sp =>
        new HttpClientHandler
        {
            // for your dev self-signed certs; in prod you’d validate properly
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });

builder.Services.AddSingleton<IConnection>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<RabbitOptions>>().Value;

    var factory = new ConnectionFactory
    {
        HostName = opts.Host,
        UserName = opts.User,
        Password = opts.Pass,
        DispatchConsumersAsync = true
    };

    return factory.CreateConnection();
});

builder.Services.AddSingleton<IGenAiEngine, GenAiEngine>();
builder.Services.AddSingleton<IGenAiResultSink, AiMqResultSink>();
builder.Services.AddSingleton<IVersionTextClient, VersionTextClient>();
builder.Services.AddHostedService<AiConsumerService>();

var host = builder.Build();
host.Run();

public class GenAiOptions
{
    public string ApiKey { get; set; } = "dummy";
}