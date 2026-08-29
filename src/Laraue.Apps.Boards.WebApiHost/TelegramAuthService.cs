using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Laraue.Apps.Boards.Auth;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Laraue.Apps.Boards.WebApiHost;

public interface ITelegramAuthService
{
    Task<string> Authenticate(
        AuthenticateViaStringInitDataRequest request,
        CancellationToken cancellationToken);
    
    Task<string> Authenticate(
        TelegramWidgetAuthRequest request,
        CancellationToken cancellationToken);

    Task<Guid> RegisterUser(
        MiniAppUser user,
        CancellationToken cancellationToken);

    string BuildHash(NameValueCollection collection);
}
    
public class TelegramAuthService(
    IOptions<TelegramOptions> options,
    DatabaseContext context,
    IAuthService authService,
    IDateTimeProvider dateTimeProvider,
    ICoreUserService coreUserService)
    : ITelegramAuthService
{
    public Task<string> Authenticate(
        AuthenticateViaStringInitDataRequest request,
        CancellationToken cancellationToken)
    {
        var userData = ValidateInitData(request.InitData);
        return CreateBearerToken(userData, cancellationToken);
    }

    public Task<string> Authenticate(
        TelegramWidgetAuthRequest request,
        CancellationToken cancellationToken)
    {
        var userData = ValidateWidgetData(request);
        return CreateBearerToken(userData, cancellationToken);
    }

    private async Task<string> CreateBearerToken(MiniAppUser userData, CancellationToken cancellationToken)
    {
        var data = await context.Users
            .Where(x => x.TelegramId == userData.Id)
            .Select(x => new
            {
                x.Id
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (data is not null)
            return authService.CreateUserToken(data.Id);

        var newUserId = await RegisterUser(userData, cancellationToken);
        return authService.CreateUserToken(newUserId);
    }

    private MiniAppUser ValidateWidgetData(TelegramWidgetAuthRequest request)
    {
        // Reject stale auth — replay attack protection
        var authAge = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(request.AuthDate);
        if (authAge > TimeSpan.FromHours(24))
            throw new ForbiddenException("Auth is expired");

        // Build data-check-string: only fields that are actually present,
        // sorted alphabetically, joined with \n, hash excluded
        var fields = new SortedDictionary<string, string>
        {
            ["auth_date"] = request.AuthDate.ToString(),
            ["first_name"] = request.FirstName,
            ["id"] = request.Id.ToString(),
        };

        if (request.LastName is not null)  
            fields["last_name"] = request.LastName;
        if (request.Username is not null)  
            fields["username"]  = request.Username;
        if (request.PhotoUrl is not null)  
            fields["photo_url"] = request.PhotoUrl;

        var dataCheckString = string.Join("\n",
            fields.Select(kv => $"{kv.Key}={kv.Value}"));

        // Secret key = SHA256(botToken) — plain hash, not HMAC
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Token));

        // Signature = HMAC-SHA256(dataCheckString, secretKey)
        var computedHash = Convert.ToHexString(
            HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLower();

        if (computedHash != request.Hash)
            throw new ForbiddenException("Authorization Failed");

        return new MiniAppUser
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Id = request.Id,
            Username = request.Username,
            LanguageCode = null,
        };
    }

    private MiniAppUser ValidateInitData(string initData)
    {
        var parsedData = HttpUtility.ParseQueryString(initData);
        var receivedHash = parsedData["hash"];
        parsedData.Remove("hash");
        
        if (string.IsNullOrEmpty(receivedHash))
            throw new ForbiddenException("Hash is missing");
        
        var generatedHash = BuildHash(parsedData);
        var result = generatedHash.Equals(receivedHash, StringComparison.OrdinalIgnoreCase);
        if (!result)
            throw new ForbiddenException("Hash mismatch");
        
        var user = parsedData["user"];
        return JsonSerializer.Deserialize<MiniAppUser>(user!, JsonBotAPI.Options)!;
    }

    public string BuildHash(NameValueCollection collection)
    {
        var sortedKeys = collection.AllKeys.OrderBy(key => key, StringComparer.Ordinal).ToList();
        var dataCheckStrings = sortedKeys.Select(key => $"{key}={collection[key]}");
        var dataCheckString = string.Join("\n", dataCheckStrings);
        
        var secretKey = HMACSHA256.HashData(
            "WebAppData"u8.ToArray(),
            Encoding.UTF8.GetBytes(options.Value.Token));
        
        var generatedHashBytes = HMACSHA256.HashData(
            secretKey,
            Encoding.UTF8.GetBytes(dataCheckString));
        
        var generatedHash = Convert.ToHexString(generatedHashBytes).ToLower();

        return generatedHash;
    }
    
    public Task<Guid> RegisterUser(
        MiniAppUser user,
        CancellationToken cancellationToken)
    {
        var newUser = new User
        {
            CreatedAt = dateTimeProvider.UtcNow,
            TelegramId = user.Id,
            TelegramLanguageCode = user.LanguageCode,
            TelegramUserName = user.Username,
            TelegramFirstName = user.FirstName,
            TelegramLastName = user.LastName,
            Color = Palette.RandomColor(),
        };

        return coreUserService.CreateIfTelegramIdNotExists(newUser, cancellationToken);
    }
}

public class MiniAppUser
{
    public long Id { get; set; }
    public string? Username { get; set; }
    public string? LanguageCode { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}