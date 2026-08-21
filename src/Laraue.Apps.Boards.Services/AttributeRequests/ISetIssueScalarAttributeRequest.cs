using Laraue.Apps.Boards.Services.AttributeUpdaters;

namespace Laraue.Apps.Boards.Services.AttributeRequests;

/// <summary>
/// Implemented by <see cref="SetIssueAttributeRequest"/> subtypes for scalar attribute types
/// (Text, Integer, Decimal, Date, DateTime), so they can share
/// <see cref="ScalarAttributeUpdater{TEntity,TValue,TRequest}"/>.
/// </summary>
public interface ISetIssueScalarAttributeRequest<out TValue>
{
    long Id { get; }
    TValue Value { get; }
}
