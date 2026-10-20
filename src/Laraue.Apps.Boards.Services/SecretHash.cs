using System.Security.Cryptography;
using System.Text;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// How a stored secret (an API key, an agent sign-in's device code) is kept: only its SHA-256, so the database
/// never holds the secret itself. The secrets are long and random, so a plain hash is enough - no salt or slow hash.
/// </summary>
public static class SecretHash
{
    public static string Compute(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(bytes);
    }
}
