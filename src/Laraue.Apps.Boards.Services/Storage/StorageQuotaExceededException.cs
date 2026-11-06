using System.Net;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// An upload refused because the organization's storage is full (BRD-340): Storage stored nothing. A 402, like
/// the other plan limits, with a message that says how much is used and how to get more, so REST and MCP callers
/// get it as is. <see cref="ErrorKey"/> in the errors lets the web app tell it from the other 402s and word it in
/// the user's language.
/// </summary>
public sealed class StorageQuotaExceededException(long usageBytes, long quotaBytes, long requiredBytes)
    : HttpExceptionWithErrors(
        string.Format(
            ErrorMessages.StorageQuotaExceeded,
            StorageSize.Format(usageBytes),
            StorageSize.Format(quotaBytes),
            StorageSize.Format(requiredBytes)),
        HttpStatusCode.PaymentRequired,
        new Dictionary<string, string[]>
        {
            [ErrorKey] = [usageBytes.ToString(), quotaBytes.ToString(), requiredBytes.ToString()],
        })
{
    /// <summary>
    /// The key of the error entry: usage, quota and the bytes the upload needed, in that order.
    /// </summary>
    public const string ErrorKey = "storageQuota";

    public long UsageBytes { get; } = usageBytes;

    public long QuotaBytes { get; } = quotaBytes;

    public long RequiredBytes { get; } = requiredBytes;
}
