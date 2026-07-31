using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;
using File = Laraue.Apps.Boards.DataAccess.Models.File;
using Models_Status = Laraue.Apps.Boards.DataAccess.Models.Status;
using Status = Laraue.Apps.Boards.DataAccess.Models.Status;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class OrganizationInitializer(
    DatabaseContext context,
    ICoreOrganizationsService coreOrganizationsService,
    Guid ownerId)
{
    private readonly Dictionary<Guid, Action<PermissionBuilder>> _organizationUsers = new();
    
    private string _organizationName = "TestOrganization";
    private string _organizationColor = "#ffffff";
    private DateTime _timestamp = DateTime.UtcNow;
    private bool _isPersonal;
    private readonly List<TestAttribute> _attributes = new ();

    private readonly List<SpaceBuilder> _spaces =
    [
        new SpaceBuilder(ownerId, "DEF", DateTime.UtcNow)
            .WithName("Default Space")
            .SetAsDefault()
            .AddEpic(ownerId, e => e
                .AddStatus(s => s.WithName("New"))
                .SetAsDefault()
                .WithName("Backlog"))
    ];
    
    public OrganizationInitializer WithName(string name)
    {
        _organizationName = name;

        return this;
    }

    public OrganizationInitializer WithColor(string color)
    {
        _organizationColor = color;

        return this;
    }

    public OrganizationInitializer WithTimestamp(DateTime timestamp)
    {
        _timestamp = timestamp;

        return this;
    }

    public OrganizationInitializer SetIsPersonal(bool isPersonal)
    {
        _isPersonal = isPersonal;

        return this;
    }

    public OrganizationInitializer AddTextAttribute(string name)
    {
        _attributes.Add(new TextTestAttribute(name));

        return this;
    }

    public OrganizationInitializer AddListAttribute(string name, string[] possibleValues)
    {
        _attributes.Add(new ListTestAttribute(name, possibleValues));

        return this;
    }

    public async Task<Organization> Initialize()
    {
        var organization = OrganizationDefaults.GetNewOrganizationEntity(
            ownerId,
            "slug",
            _organizationName,
            _organizationColor,
            _timestamp,
            _isPersonal);

        organization.Spaces = new List<Space>(); // Add all children manually
        
        organization.Attributes = new List<Attribute>();
        foreach (var attribute in _attributes)
        {
            organization.Attributes!.Add(new Attribute
            {
                AttributeType = attribute.AttributeType,
                Color = "#ffffff",
                Name = attribute.Name,
                AttributeListValues = attribute is ListTestAttribute listTestAttribute
                    ? listTestAttribute.PossibleValues
                        .Select(x => new AttributeListValue { Value = x })
                        .ToList()
                    : null
            });
        }

        var spaceCounters = new List<(Space, int)>();
        for (var index = 0; index < _spaces.Count; index++)
        {
            var space = _spaces[index];
            var spaceEntity = new Space
            {
                Name = space.SpaceName,
                Color = space.SpaceColor,
                CreatedAt = space.Timestamp,
                UpdatedAt = space.Timestamp,
                CreatorId = space.CreatorId,
                IsDefault = space.IsDefault,
                Key = space.SpaceKey,
                Epics = index == 0 ? new List<Epic>() : new List<Epic>
                {
                    OrganizationDefaults.GetNewBacklogEpicEntity(space.CreatorId, space.Timestamp)
                }
            };

            var lastIssueNumber = 0;
            foreach (var epic in space.Epics)
            {
                var epicEntity = new Epic
                {
                    Name = epic.EpicName,
                    Color = epic.EpicColor,
                    CreatedAt = epic.Timestamp,
                    UpdatedAt = epic.Timestamp,
                    TouchedAt = epic.Timestamp,
                    UserId = epic.CreatorId,
                    IsDefault = epic.IsDefault,
                    Statuses = index == 0 ? new List<Status>() : new List<Status>
                    {
                        OrganizationDefaults.GetNewStatusEntity(),
                    }
                };

                foreach (var status in epic.Statuses)
                {
                    var statusEntity = new Models_Status()
                    {
                        Name = status.StatusName,
                        Color = status.StatusColor,
                    };

                    epicEntity.Statuses.Add(statusEntity);
                }


                foreach (var issuesByStatusIndex in epic.Issues)
                {
                    var statusForIssue = epicEntity.Statuses[issuesByStatusIndex.Key];
                    statusForIssue.Issues ??= new List<Issue>();
                    foreach (var issue in issuesByStatusIndex.Value)
                    {
                        var attributes = organization.Attributes
                            .Select((attribute, i) => (i, attribute))
                            .Join(
                                issue.AttributeValues,
                                attribute => attribute.i,
                                attributeValue => attributeValue.Key,
                                (entity, pair) => new { entity, pair })
                            .ToArray();

                        var textAttributes = attributes
                            .Where(x => x.entity.attribute.AttributeType == AttributeType.Text)
                            .Select(x => new IssueAttributeTextValue
                            {
                                Attribute = x.entity.attribute,
                                Text = x.pair.Value.ToString(),
                            })
                            .ToList();
                        
                        var listAttributes = attributes
                            .Where(x => x.entity.attribute.AttributeType == AttributeType.List)
                            .Select(x => new IssueAttributeListValue
                            {
                                Attribute = x.entity.attribute,
                                AttributeListValue = x.entity.attribute.AttributeListValues!.ElementAt((int)x.pair.Value),
                            })
                            .ToList();
                        
                        statusForIssue.Issues.Add(new Issue
                        {
                            Content = issue.Content,
                            OwnerId = issue.CreatorId,
                            AssigneeId = issue.CreatorId,
                            CreatedAt = issue.Timestamp,
                            UpdatedAt = issue.Timestamp,
                            IssueNumber = new IssueNumber
                            {
                                Space = spaceEntity,
                                Number = ++lastIssueNumber,
                            },
                            TextAttributes = textAttributes,
                            ListAttributes = listAttributes,
                            LexoRank = issue.LexoRank.ToString(),
                            IssueAttachments = issue.Attachments
                                .Select(x => new IssueAttachment
                                {
                                    Attachment = ToAttachment(x, ownerId)
                                })
                                .ToList(),
                            IssueComments = issue.Comments
                                .Select(ic => new IssueComment
                                {
                                    Text = ic.Comment,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow,
                                    OwnerId = ic.CreatorId,
                                    Attachments = ic.Attachments
                                        .Select(x => new IssueCommentAttachment
                                        {
                                            Attachment = ToAttachment(x, ownerId)
                                        })
                                        .ToList(),
                                })
                                .ToList(),
                        });
                    }
                }

                spaceEntity.Epics.Add(epicEntity);
            }

            organization.Spaces!.Add(spaceEntity);
            spaceCounters.Add((spaceEntity, lastIssueNumber));
        }

        context.Add(organization);
        await context.SaveChangesAsync();

        foreach (var spaceCounter in spaceCounters)
        {
            context.Add(new SpaceCounter
            {
                SpaceId = spaceCounter.Item1.Id,
                LastNumber = spaceCounter.Item2
            });
        }
        
        await context.SaveChangesAsync();

        foreach (var user in _organizationUsers)
        {
            var builder = new PermissionBuilder();
            user.Value.Invoke(builder);

            var userPermissions = new UserPermissions
            {
                Global = builder.Permissions.GlobalAccessLevels,
                Admin = builder.Permissions.Administrative,
                Direct = builder.Permissions.DirectAccessLevels
                    .ToDictionary(
                        x => organization.Spaces![x.Key].Key,
                        x => x.Value)
            };
            
            var organizationUserId = await coreOrganizationsService.AddMember(organization.Id, user.Key, CancellationToken.None);
            await coreOrganizationsService.SetUserPermissions(
                organizationUserId,
                userPermissions,
                CancellationToken.None);
        }
        
        return organization;
    }

    private static Attachment ToAttachment(AttachmentData data, Guid ownerId)
    {
        return new Attachment
        {
            File = new File
            {
                MimeType = data.AttachmentType switch
                {
                    AttachmentType.Image => "image/jpg",
                    AttachmentType.Video => "video/mp4",
                    _ => throw new InvalidOperationException()
                },
                Name = data.Name,
                Size = 100,
                TelegramFile = new TelegramFile
                {
                    ExternalFileUniqueId = Guid.NewGuid().ToString(),
                    ExternalFileId = Guid.NewGuid().ToString(),
                }
            },
            OwnerId = ownerId,
        };
    }

    public OrganizationInitializer AddUser(Guid userId)
    {
        return AddUser(userId, _ => {});
    }

    public OrganizationInitializer AddUser(Guid userId, Action<PermissionBuilder> permissionBuilder)
    {
        _organizationUsers[userId] = permissionBuilder;

        return this;
    }

    public OrganizationInitializer AddSpace(Guid creatorId)
    {
        return AddSpace(creatorId, "ADD", _ => {});
    }

    public OrganizationInitializer AddSpace(Guid creatorId, string key)
    {
        return AddSpace(creatorId, key, _ => {});
    }

    public OrganizationInitializer AddSpace(Guid creatorId, Action<SpaceBuilder> spaceBuilder)
    {
        return AddSpace(creatorId, "ADD", spaceBuilder);
    }

    public OrganizationInitializer AddSpace(Guid creatorId, string key, Action<SpaceBuilder> spaceBuilder)
    {
        var builder = new SpaceBuilder(creatorId, key, _timestamp);
        spaceBuilder(builder);
        
        _spaces.Add(builder);

        return this;
    }

    public OrganizationInitializer AddIssue(
        int spaceIndex,
        int epicIndex,
        int statusIndex,
        Guid creatorId,
        Action<IssueBuilder> setupIssue)
    {
        _spaces[spaceIndex].Epics[epicIndex].AddIssue(creatorId, statusIndex, setupIssue);

        return this;
    }
    
    public OrganizationInitializer AddIssueToDefaultStatus(
        Guid creatorId,
        Action<IssueBuilder> setupIssue)
    {
        return AddIssue(0, 0, 0, creatorId, setupIssue);
    }
    
    public OrganizationInitializer AddIssueToDefaultStatus(
        Guid creatorId)
    {
        return AddIssueToDefaultStatus(creatorId, _ => {});
    }

    public record TestUserPermissions
    {
        public GlobalAccessLevels GlobalAccessLevels { get; set; } = new();
        public Dictionary<int, DirectSpaceAccessLevel> DirectAccessLevels { get; set; } = new();
        public AdminAccessLevel Administrative { get; set; }
    }
    
    public class PermissionBuilder
    {
        public TestUserPermissions Permissions { get; set; } = new();
        
        public PermissionBuilder SetAdminAccessLevel(AdminAccessLevel adminAccessLevel)
        {
            Permissions.Administrative = adminAccessLevel;
            return this;
        }
    
        public PermissionBuilder SetGlobalAccessLevel(Action<GlobalAccessLevels> action)
        {
            action(Permissions.GlobalAccessLevels);
            
            return this;
        }
    
        public PermissionBuilder SetSpaceAccessLevel(int index, Action<DirectSpaceAccessLevel> action)
        {
            var levels = GetDirectSpacesLevels(index);

            action(levels);
            
            return this;
        }

        private DirectSpaceAccessLevel GetDirectSpacesLevels(int spaceIndex)
        {
            if (!Permissions.DirectAccessLevels.ContainsKey(spaceIndex))
                Permissions.DirectAccessLevels[spaceIndex] = new DirectSpaceAccessLevel();
            
            return Permissions.DirectAccessLevels[spaceIndex];
        }
    }

    public class SpaceBuilder(Guid creatorId, string key, DateTime timestamp)
    {
        public string SpaceKey { get; private set; } = key;
        public string SpaceName { get; private set; } = "AdditionalSpace";
        public string SpaceColor { get; private set; }  = "#ffffff";
        public DateTime Timestamp { get; private set; }  = timestamp;
        public Guid CreatorId { get; private set; }  = creatorId;
        public List<EpicBuilder> Epics { get;} = new();
        public bool IsDefault { get; private set; }

        public SpaceBuilder WithName(string name)
        {
            SpaceName = name;

            return this;
        }
        
        public SpaceBuilder WithColor(string color)
        {
            SpaceColor = color;

            return this;
        }

        public SpaceBuilder SetAsDefault()
        {
            IsDefault = true;
            return this;
        }
        
        public SpaceBuilder WithTimestamp(DateTime timestamp)
        {
            Timestamp = timestamp;

            return this;
        }

        public SpaceBuilder AddEpic(Guid creatorId)
        {
            return AddEpic(creatorId, _ => {});
        }

        public SpaceBuilder AddEpic(Guid creatorId, Action<EpicBuilder> epicBuilder)
        {
            var builder = new EpicBuilder(creatorId, Timestamp);
            epicBuilder(builder);
            Epics.Add(builder);

            return this;
        }
    }

    public abstract record TestAttribute(AttributeType AttributeType, string Name);
    public record TextTestAttribute(string Name) : TestAttribute(AttributeType.Text, Name);
    public record ListTestAttribute(string Name, string[] PossibleValues) : TestAttribute(AttributeType.List, Name);
    
    public class EpicBuilder(Guid creatorId, DateTime timestamp)
    {
        public string EpicName { get; private set; } = "AdditionalEpic";
        public string EpicColor { get; private set; }  = "#121212";
        public DateTime Timestamp { get; private set; }  = timestamp;
        public Guid CreatorId { get; private set; }  = creatorId;
        public bool IsDefault { get; private set; }
        public List<StatusBuilder> Statuses  { get; private set; } = new ();
        public Dictionary<int, List<IssueBuilder>> Issues  { get; private set; } = new ();
        
        public EpicBuilder WithName(string name)
        {
            EpicName = name;

            return this;
        }
        
        public EpicBuilder WithColor(string color)
        {
            EpicColor = color;

            return this;
        }
        
        public EpicBuilder WithTimestamp(DateTime value)
        {
            Timestamp = value;

            return this;
        }
        
        public EpicBuilder AddStatus(Action<StatusBuilder> statusBuilder)
        {
            var builder = new StatusBuilder();
            statusBuilder(builder);
            
            Statuses.Add(builder);

            return this;
        }
        
        public EpicBuilder AddStatus()
        {
            return AddStatus(_ => { });
        }

        public EpicBuilder SetAsDefault()
        {
            IsDefault = true;
            return this;
        }

        public EpicBuilder AddIssue(Guid creatorId, int statusIndex)
        {
            return AddIssue(creatorId, statusIndex, _ => { });
        }

        public EpicBuilder AddIssue(Guid creatorId, int statusIndex, Action<IssueBuilder> issueBuilder)
        {
            var builder = new IssueBuilder(creatorId);
            issueBuilder(builder);

            if (!Issues.ContainsKey(statusIndex))
                Issues[statusIndex] = [];
            Issues[statusIndex].Add(builder);

            return this;
        }
    }

    public class StatusBuilder
    {
        public string StatusName { get; private set; } = "AdditionalStatus";
        public string StatusColor { get; private set; } = "#ffffff";
        
        public StatusBuilder WithName(string name)
        {
            StatusName = name;

            return this;
        }
        
        public StatusBuilder WithColor(string color)
        {
            StatusColor = color;

            return this;
        }
    }

    public class IssueBuilder(Guid creatorId)
    {
        public Guid CreatorId { get; } = creatorId;
        public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
        public string Content { get; private set; } = "IssueContent";
        public List<AttachmentData> Attachments { get; } = new ();
        public List<IssueCommentBuilder> Comments { get; } = new ();
        public LexoRank LexoRank { get; private set; } = LexoRank.Middle();

        public Dictionary<int, object> AttributeValues { get; set; } = new();
        
        public IssueBuilder WithContent(string name)
        {
            Content = name;

            return this;
        }
        
        public IssueBuilder WithLexoRank(LexoRank lexoRank)
        {
            LexoRank = lexoRank;

            return this;
        }
        
        public IssueBuilder WithTimestamp(DateTime timestamp)
        {
            Timestamp = timestamp;

            return this;
        }
        
        public IssueBuilder WithAttributeValue(int index, object value)
        {
            AttributeValues[index] = value;

            return this;
        }
        
        public IssueBuilder AddAttachment(string name, AttachmentType attachmentType)
        {
            Attachments.Add(new AttachmentData(name, attachmentType));

            return this;
        }
        
        public IssueBuilder AddComment(Guid ownerId, string comment, Action<IssueCommentBuilder>? setupComment = null)
        {
            var entity = new IssueCommentBuilder(ownerId, comment);
            
            setupComment?.Invoke(entity);
            
            Comments.Add(entity);

            return this;
        }
    }

    public class IssueCommentBuilder(Guid creatorId, string comment)
    {
        public Guid CreatorId { get; } = creatorId;
        public string Comment { get; } = comment;

        public List<AttachmentData> Attachments { get; } = new ();
        
        public IssueCommentBuilder AddAttachment(string name, AttachmentType attachmentType)
        {
            Attachments.Add(new AttachmentData(name, attachmentType));

            return this;
        }
    }

    public record AttachmentData(string Name, AttachmentType AttachmentType);
}