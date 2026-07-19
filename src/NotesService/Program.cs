using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotesService;
using NotesService.Auth;
using NotesService.Data;
using NotesService.Errors;
using NotesService.Serialization;
using NotesService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ApiLimits.MaxBodyBytes;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
});

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<RejectUnknownJsonFieldsFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance;
        options.JsonSerializerOptions.DictionaryKeyPolicy = SnakeCaseNamingPolicy.Instance;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(
        ApiErrors.Envelope("invalid_request", "request body is missing, malformed, or contains unsupported fields"));
});

var connectionString = ResolveConnectionString(
    builder.Configuration,
    builder.Environment.ContentRootPath);
EnsureDataDirectory(connectionString);
builder.Services.AddDbContext<NotesDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<NoteAccessService>();

builder.Services
    .AddAuthentication(BearerTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(
        BearerTokenAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
app.MapGet("/healthz", () => new { status = "ok" });
app.UseAuthentication();
app.UseAuthorization();
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
