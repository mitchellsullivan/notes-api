using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NotesService.Data;

namespace NotesService.Tests;

/// <summary>
/// Boots the real application (Program.cs, real middleware pipeline, real
/// database) against a throwaway database per fixture instance, so test
/// classes are isolated from each other; tests within a class isolate
/// themselves by creating their own users.
///
/// Default: a temp-file SQLite database — no external dependencies, works
/// anywhere the .NET 6 SDK does. If POSTGRES_TEST_CONNECTION is set (see
/// README "Tests"), each fixture instead creates a uniquely named database
/// on that Postgres server and drops it on dispose, exercising the exact
/// provider the docker-compose deployment uses.
/// </summary>
public sealed class NotesApiFactory : WebApplicationFactory<Program>
{
    private static readonly string? PostgresBase =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_CONNECTION");

    private readonly string sqlitePath = Path.Combine(
        Path.GetTempPath(),
        $"notes-tests-{Guid.NewGuid():N}.db");

    private readonly string? postgresConnection;

    public NotesApiFactory()
    {
        if (!string.IsNullOrWhiteSpace(PostgresBase))
        {
            postgresConnection = new NpgsqlConnectionStringBuilder(PostgresBase)
            {
                // Unique database per fixture; the app's EnsureCreated
                // creates it (and the schema) on startup.
                Database = $"notes_test_{Guid.NewGuid():N}"
            }.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (postgresConnection is not null)
        {
            builder.UseSetting("POSTGRES_CONNECTION", postgresConnection);
        }
        else
        {
            // Program.cs reads DATA_FILE from configuration; UseSetting
            // flows into configuration.
            builder.UseSetting("DATA_FILE", sqlitePath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && postgresConnection is not null)
        {
            // Drop the per-fixture database while the host still exists.
            // Best-effort: a stray notes_test_* database is an annoyance,
            // not a correctness problem.
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider
                    .GetRequiredService<NotesDbContext>()
                    .Database.EnsureDeleted();
            }
            catch
            {
                // ignored
            }
        }

        base.Dispose(disposing);

        if (disposing && postgresConnection is null)
        {
            // Microsoft.Data.Sqlite pools connections; clear them so the
            // files are actually deletable. Best-effort as above.
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                try
                {
                    File.Delete(sqlitePath + suffix);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
