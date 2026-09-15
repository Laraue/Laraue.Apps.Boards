namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Thrown by <see cref="IBillingTokenClient.ReserveTokensAsync"/> when Billing rejects a
/// reservation for insufficient balance (<c>StatusCode.FailedPrecondition</c> - Billing's
/// <c>TokenGrpcService</c> throws this directly rather than via an <c>HttpException</c>, so it
/// doesn't round-trip through <c>Laraue.Grpc.Client</c>'s <c>ExceptionTranslationInterceptor</c>
/// like <see cref="Laraue.Core.Exceptions.Web.NotFoundException"/>/etc. do). Same "core exception,
/// host-specific translation" shape as <see cref="Ai.AiContentSummarizationException"/> - each host
/// catches this and translates it into its own surface's error convention.
/// </summary>
public sealed class InsufficientTokenBalanceException(string message) : Exception(message);
