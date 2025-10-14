using Microsoft.EntityFrameworkCore;
using PaperlessREST.Data;
using PaperlessREST.Services;

var builder = WebApplication.CreateBuilder(args);

// PostgreSQL Connection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<MessageQueueService>();

builder.Services.AddControllers();
var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapControllers();
app.Run();