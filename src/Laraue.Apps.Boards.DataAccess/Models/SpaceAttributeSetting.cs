namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// How one attribute is used in a space with its own attribute settings, instead of the organization's defaults on
/// <see cref="Attribute"/> (BRD-179). A space has its own settings when it has any of these rows; an attribute without
/// a row there is off - one added to the organization after the settings were last saved.
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
}
