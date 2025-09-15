// Program.cs
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(); // if you’re using Controllers
// builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(); // optional

var app = builder.Build();


var connString = "Host=localhost;Port=5432;Database=paperless;Username=paperless;Password=paperless";


        using var conn = new NpgsqlConnection(connString);
        conn.Open();

        Console.WriteLine("Verbindung zu PostgreSQL hergestellt!");

        string sql = "SELECT id, name, path, mime_type, size_bytes, created_at FROM documents ORDER BY created_at DESC";

        using var cmd = new NpgsqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            string name = reader.GetString(1);
            string path = reader.GetString(2);
            string mime = reader.IsDBNull(3) ? null : reader.GetString(3);
            long size = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
            DateTime created = reader.GetDateTime(5);

            Console.WriteLine($"ID: {id}, Name: {name}, Path: {path}, Mime: {mime}, Size: {size}, Created: {created}");
        }

        conn.Close();
        Console.WriteLine("Fertig!");

        // Comment this line if you won't set up certs in the container
        // app.UseHttpsRedirection();

        app.MapControllers(); // or define minimal APIs

// Respect ASPNETCORE_URLS (set in Docker)
app.Run();
