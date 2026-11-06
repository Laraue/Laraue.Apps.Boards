using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A call to Laraue.Apps.Storage that must follow Boards' own commit (an outbox row): saved in the same transaction
/// as the attachment change, then sent and retried by a background job until Storage accepts it. Confirm once an
/// attachment with a new upload is saved - an unconfirmed upload is deleted by Storage after a day; delete once an
/// attachment is removed.
/// </summary>
public class StorageReferenceOperation
{
    public long Id { get; set; }

    /// <summary>
    /// <see cref="StorageFile.ReferenceId"/>.
    /// </summary>
    public Guid ReferenceId { get; set; }

    public StorageReferenceOperationKind Kind { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// How many times sending failed.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// When to send it (again). Pushed back after each failure.
    /// </summary>
    public DateTime NextAttemptAt { get; set; }

    [MaxLength(1024)]
    public string? LastError { get; set; }
}

public enum StorageReferenceOperationKind
{
    Confirm = 0,
    Delete = 1,
}
