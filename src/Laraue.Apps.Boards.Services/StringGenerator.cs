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

    /// <summary>
    /// The runner's secret for polling an agent sign-in - as long and random as an API key.
    /// </summary>
    public static string GenerateAgentSignInDeviceCode()
    {
        return GenerateRandomString(43);
    }

    /// <summary>
    /// The short code a person compares between the terminal and the Allow page, e.g. <c>WXYZ-2345</c>: upper-case
    /// letters and digits without the look-alikes 0/O and 1/I.
    /// </summary>
    public static string GenerateAgentSignInUserCode()
    {
        var code = GenerateRandomString(8, UserCodeChars);
        return $"{code[..4]}-{code[4..]}";
    }

    private const string UserCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static string GenerateRandomString(int length, string chars = Chars)
    {
        return string.Create(length, (chars, length), (span, state) =>
        {
            var charsArr = state.chars;
            for (var i = 0; i < state.length; i++)
                span[i] = charsArr[RandomNumberGenerator.GetInt32(charsArr.Length)];
        });
    }
}