var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/healthz", () => new { status = "ok" });

await app.RunAsync();
