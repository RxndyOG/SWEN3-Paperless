namespace PaperlessREST
{
    using System;
    using Npgsql;

    class Program
    {
        static void Main()
        {
            var connString = "Host=localhost;Port=5432;Database=mydb;Username=myuser;Password=mypassword";

            using var conn = new NpgsqlConnection(connString);
            conn.Open();

            using var cmd = new NpgsqlCommand("SELECT version()", conn);
            var version = cmd.ExecuteScalar()?.ToString();

            Console.WriteLine($"PostgreSQL Version: {version}");
        }
    }
}
