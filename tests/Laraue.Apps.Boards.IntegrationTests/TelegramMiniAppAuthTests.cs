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
using Telegram.Bot;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class TelegramMiniAppAuthTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task Authenticate_ShouldReturnToken_WhenInitDataIsFresh()
    {
        using var testScope = host.CreateTestScope();
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var token = await AuthenticateAsync(SignedInitData(701, authDate));

        Assert.False(string.IsNullOrEmpty(token));
        Assert.True(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 701));
    }

    [Fact]
    public async Task Authenticate_ShouldReturnForbidden_WhenInitDataIsOlderThanADay()
    {
        using var testScope = host.CreateTestScope();
        var authDate = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => AuthenticateAsync(SignedInitData(702, authDate)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.False(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 702));
    }

    [Fact]
    public async Task Authenticate_ShouldReturnForbidden_WhenInitDataHasNoAuthDate()
    {
        using var testScope = host.CreateTestScope();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => AuthenticateAsync(SignedInitData(703, authDate: null)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.False(await testScope.Database.Users.AnyAsync(x => x.TelegramId == 703));
    }

    private Task<string?> AuthenticateAsync(string initData)
    {
        return host.Controller<TelegramAuthController>()
            .Execute(x => x.Authenticate(new AuthenticateViaStringInitDataRequest { InitData = initData }, default));
    }

    /// <summary>
    /// Builds init data the way Telegram does: the data-check-string is signed with
    /// HMAC-SHA256 whose key is HMAC-SHA256("WebAppData" as the key, the bot token as the data).
    /// </summary>
    private string SignedInitData(long telegramId, long? authDate)
    {
        var botToken = host.Services.GetRequiredService<IConfiguration>()["Telegram:Token"]!;
        var user = JsonSerializer.Serialize(new MiniAppUser { Id = telegramId, FirstName = "Ada" }, JsonBotAPI.Options);

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["user"] = user,
            ["chat_type"] = "sender",
            ["chat_instance"] = "-2324326728223666222",
        };

        if (authDate is not null)
            fields["auth_date"] = authDate.Value.ToString();

        var dataCheckString = string.Join("\n", fields.Select(x => $"{x.Key}={x.Value}"));
        var secretKey = HMACSHA256.HashData("WebAppData"u8.ToArray(), Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        fields["hash"] = hash;
        return string.Join("&", fields.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }
}
