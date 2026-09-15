using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// The unit that can has spaces, boards, issues.
/// </summary>
public class Organization
{
    public long Id { get; set; }
    
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(7)]
    public required string Color { get; set; }
    
    /// <summary>
    /// Who is the owner of organization. That can be transferred.
    /// </summary>
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }
    
    /// <summary>
    /// Type. Organization can be a personal (single user) or organization (multiple users).
    /// </summary>
    public OrganizationType Type { get; set; }

    /// <summary>
    /// Stable identity this organization is billed under in the Billing service (its
    /// <c>PaidEntityId</c>) - generated once on creation and never reused, unlike <see cref="OwnerId"/>
    /// which can be transferred to a different user. Null for a personal organization - its one
    /// user bills under their own <see cref="User.Id"/> instead, since that's already a stable
    /// per-user Guid and a personal org has no separate billing identity of its own.
    /// </summary>
    public Guid? BillingId { get; set; }
    
    /// <summary>
    /// Epic creation date.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Epic attribute update date.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Spaces linked to the organization.
    /// </summary>
    public IList<Space>? Spaces { get; set; }
    
    /// <summary>
    /// Organization code to join an organization.
    /// </summary>
    [MaxLength(8)]
    public string? JoinCode { get; set; }

    /// <summary>
    /// Organization slug. The public name to access this organization. 
    /// </summary>
    [MaxLength(64)]
    public string Slug { get; set; } = string.Empty;
    
    /// <summary>
    /// Postfix to allow slugs be ununique and make it harder to access something else organization.
    /// </summary>
    [MaxLength(4)]
    public string SlugPostfix { get; set; } = string.Empty;

    /// <summary>
    /// Users of organization.
    /// </summary>
    public IList<OrganizationUser>? Users { get; set; }
    
    public IList<Attribute>? Attributes { get; set; }
}

public enum OrganizationType
{
    Organization = 0,
    Personal = 1,
}