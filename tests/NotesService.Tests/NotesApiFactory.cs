using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace NotesService.Tests;

/// <summary>
/// Boots the real application (Program.cs, real middleware pipeline, real
/// database) against a throwaway SQLite file per fixture instance, so test
/// classes are isolated from each other.
/// </summary>
public sealed class NotesApiFactory : WebApplicationFactory<Program>
{
    private readonly string sqlitePath = Path.Combine(
        Path.GetTempPath(),
        $"notes-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs reads DATA_FILE from configuration; UseSetting
        // flows into configuration.
        builder.UseSetting("DATA_FILE", sqlitePath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            // Microsoft.Data.Sqlite pools connections; clear them so the
            // files are actually deletable. Best-effort.
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
