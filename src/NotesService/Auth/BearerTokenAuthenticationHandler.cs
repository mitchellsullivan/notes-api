using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotesService.Data;
using NotesService.Errors;

namespace NotesService.Auth;

public sealed class BearerTokenAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";

    private readonly NotesDbContext db;

    public BearerTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        NotesDbContext db)
        : base(options, logger, encoder, clock)
    {
        this.db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return AuthenticateResult.NoResult();
        }

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || header.Length == "Bearer ".Length)
        {
            return AuthenticateResult.Fail("Malformed Authorization header");
        }

        var token = header["Bearer ".Length..];
        var tokenHash = TokenService.HashToken(token);
        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, Context.RequestAborted);

        if (user is null)
        {
            return AuthenticateResult.Fail("Invalid token");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Name)
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = SchemeName;
        await Response.WriteAsJsonAsync(
            ApiErrors.Envelope("unauthorized", "missing or invalid bearer token"));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(
            ApiErrors.Envelope("forbidden", "you do not have permission to perform this action"));
    }
}
