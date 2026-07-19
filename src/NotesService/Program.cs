using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotesService.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connectionString = ResolveConnectionString(
    builder.Configuration,
    builder.Environment.ContentRootPath);
EnsureDataDirectory(connectionString);
builder.Services.AddDbContext<NotesDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

app.MapGet("/healthz", () => new { status = "ok" });
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await app.RunAsync();

static string ResolveConnectionString(IConfiguration configuration, string contentRoot)
{
    var dataFile = configuration["DATA_FILE"];
    var raw = !string.IsNullOrWhiteSpace(dataFile)
        ? new SqliteConnectionStringBuilder
        {
            DataSource = dataFile,
            ForeignKeys = true,
            Cache = SqliteCacheMode.Shared
        }.ToString()
        : configuration.GetConnectionString("Notes")
          ?? "Data Source=./data/notes.db;Foreign Keys=True;Cache=Shared";

    var builder = new SqliteConnectionStringBuilder(raw);
    if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
        builder.DataSource != ":memory:" &&
        !Path.IsPathRooted(builder.DataSource))
    {
        builder.DataSource = Path.GetFullPath(builder.DataSource, contentRoot);
    }

    return builder.ToString();
}

static void EnsureDataDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
    {
        return;
    }

    var directory = Path.GetDirectoryName(dataSource);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

// Exposed so the test project can reference Program via WebApplicationFactory<Program>.
public partial class Program { }
