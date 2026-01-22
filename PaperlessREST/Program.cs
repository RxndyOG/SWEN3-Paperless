using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Paperless.Contracts;
using Paperless.Contracts.SharedServices;
using PaperlessREST.Data;
using PaperlessREST.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
});

// PostgreSQL Connection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IMessageQueueService, RestQueueService>();

builder.Services.AddSingleton<IObjectStorage>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var storage = new MinioStorage(cfg);
    storage.EnsureBucketAsync().GetAwaiter().GetResult();
    return storage;
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

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<RestConsumerService>();
}

builder.Services.AddControllers();
var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapControllers();
app.Run();

public partial class Program { }
