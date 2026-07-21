using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using NotesService;
using NotesService.Auth;
using NotesService.Data;
using NotesService.Errors;
using NotesService.Serialization;
using NotesService.Services;
using Swashbuckle.AspNetCore.SwaggerGen;

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

// Postgres when a connection is configured (e.g. by docker-compose);
// zero-dependency SQLite otherwise. The model is provider-agnostic, so
// this is the only place the choice exists.
var postgresConnection = builder.Configuration["POSTGRES_CONNECTION"]
    ?? builder.Configuration.GetConnectionString("Postgres");

if (!string.IsNullOrWhiteSpace(postgresConnection))
{
    builder.Services.AddDbContext<NotesDbContext>(options => options.UseNpgsql(postgresConnection));
}
else
{
    var connectionString = ResolveConnectionString(
        builder.Configuration,
        builder.Environment.ContentRootPath);
    EnsureDataDirectory(connectionString);
    builder.Services.AddDbContext<NotesDbContext>(options => options.UseSqlite(connectionString));
}

builder.Services.AddScoped<NoteAccessService>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

builder.Services
    .AddAuthentication(BearerTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(
        BearerTokenAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Notes Service",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the token returned by POST /v1/users (shown exactly once)."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    options.SchemaFilter<HideUnmappedFieldsSchemaFilter>();
});

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
// Deliberately enabled in all environments: containers default to
// ASPNETCORE_ENVIRONMENT=Production, and reviewers running via Docker
// should get the interactive docs too. A real deployment would gate this.
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var status = report.Status == HealthStatus.Healthy ? "ok" : "unavailable";
        await context.Response.WriteAsJsonAsync(new { status });
    }
});
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

public sealed class HideUnmappedFieldsSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        schema.Properties?.Remove("unmappedFields");
        schema.Properties?.Remove("unmapped_fields");
        schema.AdditionalPropertiesAllowed = false;
        schema.AdditionalProperties = null;
    }
}

public partial class Program { }
