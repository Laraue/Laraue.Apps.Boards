using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

public class Attachment
{
    public Guid Id { get; set; }
    
    [MaxLength(255)]
    public required string FileName { get; set; }
    
    [MaxLength(100)]
    public required string ContentType { get; set; }
    
    public long IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }
}