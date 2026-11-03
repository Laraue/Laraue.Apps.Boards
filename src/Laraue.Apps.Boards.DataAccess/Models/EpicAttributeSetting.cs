namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// How one attribute is used in an epic with its own attribute settings, instead of its space's or the organization's
/// (BRD-179). An epic has its own settings when it has any of these rows; an attribute without a row there is off - one
/// added to the organization after the settings were last saved. The backlog (<see cref="Epic.IsDefault"/>) never has
/// any: it always follows its space.
/// </summary>
public class EpicAttributeSetting
{
    public long EpicId { get; set; }
    public Epic? Epic { get; set; }

    public long AttributeId { get; set; }
    public Attribute? Attribute { get; set; }

    /// <inheritdoc cref="Models.Attribute.IsEnabled"/>
    public bool IsEnabled { get; set; }

    /// <inheritdoc cref="Models.Attribute.ShowOnCard"/>
    public bool ShowOnCard { get; set; }
}
