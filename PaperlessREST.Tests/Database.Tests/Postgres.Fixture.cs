using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using System;
using PaperlessREST.Data;
using System.ComponentModel;
using Testcontainers.PostgreSql;

public class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; private set; } = default!;
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        Container = new PostgreSqlBuilder()
            .WithImage("postgres:15")
            .WithUsername("test")
            .WithPassword("testpw")
            .WithDatabase("paperless_test")
            .Build();

        await Container.StartAsync();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString).Options;

        using var db = new AppDbContext(opts);
        db.Database.Migrate();
    }

    public async Task DisposeAsync() => await Container.DisposeAsync();
}