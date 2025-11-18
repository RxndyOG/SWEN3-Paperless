using Microsoft.Extensions.Options;
using Paperless.Contracts;
using PaperlessAI;
using PaperlessAI.Abstractions;
using PaperlessAI.Services;
using RabbitMQ.Client;
using System.Runtime.InteropServices;

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
builder.Services.AddHostedService<AiConsumerService>();

var host = builder.Build();
host.Run();

public class GenAiOptions
{
    public string ApiKey { get; set; } = "dummy";
}