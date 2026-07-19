using System.Security.Cryptography;
using System.Text;

namespace NotesService.Auth;

public static class TokenService
{
    public static string CreateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
