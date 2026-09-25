using System.Collections.Specialized;
using System.Text.Json;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;

namespace Laraue.Apps.Boards.WebApiHost.Controllers;

[ApiController]
[Route("/api/test")]
public class TestController(
    ITelegramAuthService telegramAuthService,
    DatabaseContext context,
    IHostEnvironment environment) : ControllerBase
{
    [HttpPost("user")]
    public async Task<Guid> CreateTestUser(
        [FromBody] MiniAppUser user,
        CancellationToken ct)
    {
        EnsureTestEnvironmentOrThrow();

        return await telegramAuthService.RegisterUser(user, ct);
    }
    
    [HttpGet("user/{id}")]
    public async Task<string> GetUserBearer(
        Guid id,
        CancellationToken ct)
    {
        EnsureTestEnvironmentOrThrow();
        
        var dbUser = await context.Users.FirstOrThrowNotFoundEFAsync(x => x.Id == id, "No user found", ct);
        
        var user = new MiniAppUser
        {
            Id = dbUser.TelegramId
                ?? throw new BadRequestException(nameof(id), "Test bearer can be built only for a Telegram user"),
            FirstName = dbUser.DisplayName,
        };
        
        var userJson = JsonSerializer.Serialize(user, JsonBotAPI.Options);
        var parameters = new NameValueCollection
        {
            ["user"] = userJson,
            ["chat_instance"] = "-2324326728223666222",
            ["chat_type"] = "sender",
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["signature"] = "r8Mj6X8tAIfO5jrTsRdgVkjTNWqVJaBAIq1Z3qoZkugfCfBtatRYPvXzTiJYjUG3i188igSnL2dlIDf8Hye4Re",
        }; 

        var hash = telegramAuthService.BuildHash(parameters);
        parameters["hash"] = hash;
        
        var queryString = string.Join("&", parameters.AllKeys
            .Select(key => $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(parameters[key]!)}"));
        return queryString;
    }
    
    private void EnsureTestEnvironmentOrThrow()
    {
        if (!environment.IsDevelopment())
            throw new NotFoundException(string.Empty);
    }
}