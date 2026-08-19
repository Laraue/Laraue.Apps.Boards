using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Laraue.Apps.Boards.Services;

public interface ICoreUserService
{
    Task UpdatePreferences(
        Guid userId,
        Action<UpdateSettersBuilder<UserPreferences>> updateSetters,
        CancellationToken cancellationToken);
    
    Task<UserPreferencesResponse> GetPreferences(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Guid> CreateIfTelegramIdNotExists(User user, CancellationToken cancellationToken);
}

public class CoreUserService(DatabaseContext context, IDateTimeProvider dateTimeProvider) : ICoreUserService
{
    public async Task UpdatePreferences(
        Guid userId,
        Action<UpdateSettersBuilder<UserPreferences>> updateSetters,
        CancellationToken cancellationToken)
    {
        var updatedCount = await context.UserPreferences
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(updateSetters, cancellationToken);
        
        if (updatedCount > 0)
            return;
        
        // The first settings setup
        var preferences = GetDefaultPreferences(userId);
        context.Add(preferences);
        
        await context.SaveChangesAsync(cancellationToken);
        await context.UserPreferences
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(updateSetters, cancellationToken);
    }

    public async Task<UserPreferencesResponse> GetPreferences(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await context.UserPreferences
            .Where(x => x.UserId == userId)
            .FirstOrDefaultAsyncEF(cancellationToken)
            ?? GetDefaultPreferences(userId);

        return new UserPreferencesResponse
        {
            EpicSortOrder = preferences.EpicSortOrder,
        };
    }

    public async Task<Guid> CreateIfTelegramIdNotExists(User user, CancellationToken cancellationToken)
    {
        var timestamp = dateTimeProvider.UtcNow;
        
        user.Color = Palette.RandomColor();
        user.Id = Guid.NewGuid();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        
        var insertedCount = await context.Users
            .Merge()
            .Using([user])
            .On((t, s) => t.TelegramId == s.TelegramId)
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);

        if (insertedCount > 0)
        {
            var organization = OrganizationDefaults.GetNewOrganizationEntity(
                user.Id,
                OrganizationDefaults.GetPersonalOrganizationSlug(user.TelegramUserName),
                OrganizationDefaults.GetPersonalOrganizationName(user.TelegramLanguageCode),
                Palette.RandomColor(),
                timestamp,
                isPersonal: true);

            var defaultStatus = organization.Spaces!.Single().Epics!.Single().Statuses!.Single();

            context.Organizations.Add(organization);
            context.LinkedTelegramChats.Add(new LinkedTelegramChat
            {
                ExternalChatId = user.TelegramId,
                Title = user.TelegramUserName ?? user.TelegramFirstName,
                Status = defaultStatus,
                OwnerId = user.Id,
                SaveMode = SaveMode.EachMessage,
                LinkedAt = timestamp,
            });

            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return user.Id;
    }

    private static UserPreferences GetDefaultPreferences(Guid userId)
    {
        return new UserPreferences
        {
            UserId = userId,
            EpicSortOrder = EpicSortOrder.LastTouched
        };
    }
}

public record UserPreferencesResponse
{
    public EpicSortOrder EpicSortOrder { get; init; }
}
