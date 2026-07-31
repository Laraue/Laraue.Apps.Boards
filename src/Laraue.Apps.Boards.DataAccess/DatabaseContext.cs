using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Telegram.NET.Interceptors.EFCore;
using Laraue.Telegram.NET.UpdatesQueue.EFCore;
using Laraue.Telegram.NET.UpdatesQueue.EFCore.DataAccess;
using Microsoft.EntityFrameworkCore;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;
using File = Laraue.Apps.Boards.DataAccess.Models.File;

namespace Laraue.Apps.Boards.DataAccess;

public class DatabaseContext : DbContext, IUpdatesQueueDbContext, IInterceptorsDbContext<Guid>
{
    public DatabaseContext(DbContextOptions options) 
        : base(options)
    {
    }
    
    public DbSet<Attachment> Attachments { get; set; }
    public DbSet<User> Users { get; init; }
    public DbSet<UserPreferences> UserPreferences { get; init; }
    public DbSet<UserOrganizationPreferences> UserOrganizationPreferences { get; init; }
    public DbSet<Issue> Issues { get; init; }
    public DbSet<IssueAttachment> IssueAttachments { get; init; }
    public DbSet<IssueUpdate> IssueUpdates { get; init; }
    public DbSet<IssueUpdateItem> IssueUpdateItems { get; init; }
    public DbSet<IssueNumber> IssueNumbers { get; init; }
    public DbSet<IssueComment> IssueComments { get; init; }
    public DbSet<IssueCommentAttachment> IssueCommentsAttachments { get; init; }
    public DbSet<Epic> Epics { get; init; }
    public DbSet<Space> Spaces { get; init; }
    public DbSet<File> Files { get; init; }
    public DbSet<SpaceCounter> SpaceCounters { get; init; }
    public DbSet<DirectSpacePermission> DirectSpacePermissions { get; init; }
    public DbSet<Organization> Organizations { get; init; }
    public DbSet<OrganizationUser> OrganizationUsers { get; init; }
    public DbSet<Status> Statuses { get; init; }
    public DbSet<TelegramFile> TelegramFiles { get; init; }
    public DbSet<TelegramMessage> TelegramMessages { get; init; }
    public DbSet<TelegramMediaGroup> TelegramMediaGroups { get; init; }
    
    public DbSet<Attribute> Attributes { get; set; }
    public DbSet<AttributeListValue> AttributeListValues { get; set; }
    public DbSet<IssueAttributeListValue> IssueAttributeListValues { get; set; }
    public DbSet<IssueAttributeTextValue> IssueAttributeTextValues { get; set; }

    public DbSet<Update> Updates { get; set; }
    public DbSet<FailedUpdate> FailedUpdates { get; set; }
    public DbSet<InterceptorStateModel<Guid>> InterceptorState { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("pg_trgm");
        
        modelBuilder.Entity<Issue>(entity =>
        {
            entity
                .HasIndex(x => x.Content)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            entity.HasIndex(x => x.AssigneeId);
            
            entity.Property(x => x.LexoRank)
                .HasMaxLength(34)
                .IsFixedLength()
                .UseCollation("C")
                .IsRequired();

            entity.HasIndex(x => x.LexoRank);
        });
        
        modelBuilder.Entity<IssueNumber>(entity =>
        {
            entity.HasKey(x => x.IssueId);
            
            entity
                .HasIndex(x => new { x.SpaceId, x.Number })
                .IsUnique();
        });
        
        modelBuilder.Entity<IssueAttributeTextValue>(entity =>
        {
            entity.HasKey(x => new { x.IssueId, x.AttributeId, });
            entity
                .HasIndex(x => x.Text)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");
        });
        
        modelBuilder.Entity<IssueAttributeListValue>(entity =>
        {
            entity.HasKey(x => new { x.IssueId, x.AttributeId });
            entity.HasIndex(x => x.AttributeListValueId);
        });
        
        modelBuilder.Entity<Space>(entity =>
        {
            entity
                .HasIndex(x => new { x.OrganizationId, x.Key })
                .IsUnique();
        });
        
        modelBuilder.Entity<SpaceCounter>(entity =>
        {
            entity.HasKey(x => x.SpaceId);
        });
        
        modelBuilder.Entity<TelegramMediaGroup>(entity =>
        {
            entity
                .HasIndex(x => x.ExternalId)
                .IsUnique();
        });
        
        modelBuilder.Entity<TelegramMessage>(entity =>
        {
            entity
                .HasIndex(x => new { x.ExternalMessageId, x.ExternalChatId })
                .IsUnique();
        });
        
        modelBuilder.Entity<UserPreferences>(entity =>
        {
            entity.HasKey(x => x.UserId);
        });
        
        modelBuilder.Entity<UserOrganizationPreferences>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.UserId });
        });
        
        modelBuilder.Entity<Organization>(entity =>
        {
            entity
                .HasIndex(x => new { x.OwnerId, x.Type })
                .HasFilter("type = 1")
                .IsUnique();
            
            entity
                .HasIndex(x => x.JoinCode)
                .IsUnique();
            
            entity
                .HasIndex(x => new { x.SlugPostfix, x.Slug })
                .IsUnique();
        });
        
        modelBuilder.Entity<User>(builder =>
        {
            builder.HasIndex(x => x.TelegramId).IsUnique();
        });
        
        modelBuilder.Entity<IssueAttachment>(builder =>
        {
            builder.HasKey(x => new { x.IssueId, x.AttachmentId });
        });
        
        modelBuilder.Entity<IssueCommentAttachment>(builder =>
        {
            builder.HasKey(x => new { x.CommentId, x.AttachmentId });
        });
        
        modelBuilder.Entity<IssueUpdate>(builder =>
        {
            builder
                .HasIndex(x => new { x.OrganizationId, x.CreatedAt })
                .IsDescending(false, true);
        });
    }
}