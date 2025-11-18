using Microsoft.EntityFrameworkCore;
using PaperlessREST.Data;
using PaperlessREST.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10 MB
});

// PostgreSQL Connection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<RestQueueService>();

builder.Services.AddSingleton<IObjectStorage>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var storage = new MinioStorage(cfg);
    storage.EnsureBucketAsync().GetAwaiter().GetResult();
    return storage;
});

builder.Services.AddHostedService<RestConsumerService>();


builder.Services.AddControllers();
var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapControllers();
app.Run();