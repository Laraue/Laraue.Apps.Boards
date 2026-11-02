namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// How one attribute is used in a space that overrides the attribute settings
/// (<see cref="Space.HasAttributeOverride"/>). An attribute without a row in such a space is off there - e.g. one
/// added to the organization after the override was made (BRD-179).
/// </summary>
public class SpaceAttributeSetting
{
    public long SpaceId { get; set; }
    public Space? Space { get; set; }

    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }

    /// <inheritdoc cref="Models.Attribute.IsEnabled"/>
    public bool IsEnabled { get; set; }

    /// <inheritdoc cref="Models.Attribute.ShowOnCard"/>
    public bool ShowOnCard { get; set; }

    /// <inheritdoc cref="Models.Attribute.SortOrder"/>
    public int SortOrder { get; set; }
}
