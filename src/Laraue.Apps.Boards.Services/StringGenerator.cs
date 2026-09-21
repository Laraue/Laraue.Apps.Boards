using System.Security.Cryptography;

namespace Laraue.Apps.Boards.Services;

public static class StringGenerator
{
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string GenerateJoinCode()
    {
        return GenerateRandomString(8);
    }
    
    public static string GenerateOrganizationPostfix()
    {
        return GenerateRandomString(4);
    }

    /// <summary>
    /// A raw API key secret - a static, greppable prefix (so a leaked key is easy to recognize in
    /// logs/scans, same convention as e.g. GitHub's <c>ghp_</c>) plus 32 random characters.
    /// </summary>
    public static string GenerateApiKey()
    {
        return "brdk_" + GenerateRandomString(32);
    }

    private static string GenerateRandomString(int length)
    {
        return string.Create(length, (chars: Chars, length), (span, state) =>
        {
            var charsArr = state.chars;
            for (var i = 0; i < state.length; i++)
                span[i] = charsArr[RandomNumberGenerator.GetInt32(charsArr.Length)];
        });
    }
}