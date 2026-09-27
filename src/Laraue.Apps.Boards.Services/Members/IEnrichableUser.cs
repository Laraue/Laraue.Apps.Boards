namespace Laraue.Apps.Boards.Services.Members;

/// <summary>
/// A DTO showing a person - queries project just <see cref="UserId"/>, and
/// <see cref="IMemberProfileReader"/> fills in how that person is shown in the organization.
/// </summary>
public interface IEnrichableUser
{
    Guid UserId { get; }

    /// <summary>Takes the parts of the profile the DTO shows.</summary>
    void Enrich(MemberProfile profile);
}
