namespace Laraue.Apps.Boards.DataAccess.Enums;

[Flags]
public enum AdminAccessLevel : byte
{
    None = 0,
    Manage = 1,
    UpdateOrganization = 2,
    DeleteOrganization = 4,
    MassMove = 8,
    ManageAttributes = 16,
    LinkChats = 32,
    All = Manage | UpdateOrganization | DeleteOrganization | MassMove | ManageAttributes | LinkChats,
}