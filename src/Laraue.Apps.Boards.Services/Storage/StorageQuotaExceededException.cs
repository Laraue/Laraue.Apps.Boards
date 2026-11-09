using System.Net;
using Laraue.Apps.Boards.Services.Resources;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// An upload refused because the organization's storage is full (BRD-340): Storage stored nothing. A 402, like the
/// other plan limits, whose message says how much is used, how much the file needs and how to get more, so REST and
/// MCP callers get it as is.
/// </summary>
public sealed class StorageQuotaExceededException(long usageBytes, long quotaBytes, long requiredBytes)
    : HttpException(
        HttpStatusCode.PaymentRequired,
        string.Format(
            ErrorMessages.StorageQuotaExceeded,
            StorageSize.Format(usageBytes),
            StorageSize.Format(quotaBytes),
            StorageSize.Format(requiredBytes)));
