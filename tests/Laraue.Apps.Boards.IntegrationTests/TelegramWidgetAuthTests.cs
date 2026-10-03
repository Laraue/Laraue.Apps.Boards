using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class TelegramWidgetAuthTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task Authenticate_ShouldReturnToken_WhenWidgetDataIsFresh()
    {
        using var testScope = host.CreateTestScope();

        var token = await AuthenticateAsync(SignedWidgetData(801, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 801));
    }

    [Fact]
    public async Task Authenticate_ShouldReturnToken_WhenTelegramSignedAFieldTheBackendDoesNotKnow()
    {
        using var testScope = host.CreateTestScope();
        var signed = SignedWidgetData(
            802,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            signedExtraFields: new Dictionary<string, string> { ["new_field"] = "value" },
            sentExtraFields: new Dictionary<string, string> { ["new_field"] = "value" });

        var token = await AuthenticateAsync(signed);

        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 802));
    }

    [Fact]
    public async Task Authenticate_ShouldReturnForbidden_WhenAnExtraFieldIsNotSigned()
    {
        using var testScope = host.CreateTestScope();
        var signed = SignedWidgetData(
            803,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            sentExtraFields: new Dictionary<string, string> { ["new_field"] = "value" });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => AuthenticateAsync(signed));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.False(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 803));
    }

    [Fact]
    public async Task Authenticate_ShouldReturnForbidden_WhenWidgetDataIsOlderThanADay()
    {
        using var testScope = host.CreateTestScope();
        var authDate = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => AuthenticateAsync(SignedWidgetData(804, authDate)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.False(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 804));
    }

    private Task<string?> AuthenticateAsync(TelegramWidgetAuthRequest request)
    {
        return host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(request, default));
    }

    /// <summary>
    /// Signs the data the way Telegram does for the login widget: the key is SHA256 of the bot
    /// token, the signature is HMAC-SHA256 of all the fields sorted alphabetically as key=value
    /// lines, the hash is not included.
    /// </summary>
    private TelegramWidgetAuthRequest SignedWidgetData(
        long telegramId,
        long authDate,
        Dictionary<string, string>? signedExtraFields = null,
        Dictionary<string, string>? sentExtraFields = null)
    {
        var botToken = host.Services.GetRequiredService<IConfiguration>()["Telegram:Token"]!;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDate.ToString(),
            ["first_name"] = "Ada",
            ["id"] = telegramId.ToString(),
        };

        foreach (var (name, value) in signedExtraFields ?? [])
            fields[name] = value;

        var dataCheckString = string.Join("\n", fields.Select(x => $"{x.Key}={x.Value}"));
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        return new TelegramWidgetAuthRequest
        {
            Id = telegramId,
            FirstName = "Ada",
            AuthDate = authDate,
            Hash = hash,
            AdditionalFields = sentExtraFields?.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)),
        };
    }
}
