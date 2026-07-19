using System.Security.Claims;

namespace NotesService.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string UserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no identifier claim");
}
