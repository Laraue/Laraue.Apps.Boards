using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Signs a user up the way the hosts do: resolve the identity outside a transaction, then create the
/// user within one.
/// </summary>
public static class CoreUserServiceTestExtensions
{
    public static async Task<Guid> SignUpAsync(
        this ICoreUserService service,
        DatabaseContext database,
        TelegramUserProfile profile)
    {
        var identity = await service.ResolveTelegramIdentity(profile, CancellationToken.None);

        await using var transaction = await database.Database.BeginTransactionAsync();
        var userId = await service.CreateIfTelegramIdNotExists(profile, identity, CancellationToken.None);
        await transaction.CommitAsync();

        return userId;
    }

    public static async Task<Guid> SignUpAsync(
        this ICoreUserService service,
        DatabaseContext database,
        GoogleUserProfile profile)
    {
        var identity = await service.ResolveGoogleIdentity(profile, CancellationToken.None);

        await using var transaction = await database.Database.BeginTransactionAsync();
        var userId = await service.CreateIfGoogleSubjectNotExists(profile, identity, CancellationToken.None);
        await transaction.CommitAsync();

        return userId;
    }
}
