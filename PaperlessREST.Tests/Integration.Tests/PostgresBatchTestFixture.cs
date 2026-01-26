using Testcontainers.PostgreSql;
using Xunit;
using Npgsql;

public sealed class PostgresBatchFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; private set; } = default!;
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        Container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("paperless_test")
            .WithUsername("test")
            .WithPassword("testpw")
            .Build();

        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Container != null)
            await Container.DisposeAsync();
    }
}

public static class BatchSchema
{
    public static async Task CreateMinimalSchemaAsync(string cs, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        //raw sql
        var sql = @"
CREATE TABLE IF NOT EXISTS ""Documents"" (
  ""Id"" SERIAL PRIMARY KEY,
  ""FileName"" TEXT NOT NULL,
  ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  ""CurrentVersionId"" INT NULL
);

CREATE TABLE IF NOT EXISTS ""AccessStatistics"" (
  ""documentId"" INT NOT NULL,
  ""accessDate"" DATE NOT NULL,
  ""accessCount"" INT NOT NULL,
  CONSTRAINT ""FK_AccessStatistics_Documents"" FOREIGN KEY (""documentId"") REFERENCES ""Documents"" (""Id"") ON DELETE CASCADE,
  CONSTRAINT ""PK_AccessStatistics"" PRIMARY KEY (""documentId"", ""accessDate"")
);
";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<int> InsertDocumentAsync(string cs, string fileName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO ""Documents"" (""FileName"", ""CreatedAt"") VALUES (@fn, NOW()) RETURNING ""Id"";",
            conn);
        cmd.Parameters.AddWithValue("fn", fileName);
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        return id;
    }

    public static async Task<int?> GetAccessCountAsync(string cs, int docId, DateTime date, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            @"SELECT ""accessCount"" FROM ""AccessStatistics"" WHERE ""documentId""=@id AND ""accessDate""=@d;",
            conn);
        cmd.Parameters.AddWithValue("id", docId);
        cmd.Parameters.AddWithValue("d", date.Date);

        var val = await cmd.ExecuteScalarAsync(ct);
        return val == null || val is DBNull ? null : (int)val;
    }
}