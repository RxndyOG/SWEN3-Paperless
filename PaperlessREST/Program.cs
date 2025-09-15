// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(); // if you’re using Controllers
// builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(); // optional

var app = builder.Build();

// Comment this line if you won't set up certs in the container
// app.UseHttpsRedirection();

app.MapControllers(); // or define minimal APIs

// Respect ASPNETCORE_URLS (set in Docker)
app.Run();
