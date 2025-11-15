using PaperlessAI;
using RabbitMQ.Client;

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

builder.Services.AddHostedService<AiConsumerService>();

var host = builder.Build();
host.Run();


public class RabbitOptions
{
    public string Host { get; set; } = "rabbitmq";
    public string User { get; set; } = "user";
    public string Pass { get; set; } = "pass";
    public string QueueName { get; set; } = "documents";
}