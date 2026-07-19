using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;
using Status = Laraue.Apps.Boards.DataAccess.Models.Status;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class OrganizationExtensions
{
    public record IssueData(string Key, Issue Issue);
    
    public static IssueData GetIssueData(
        this Organization organization,
        int spaceIndex,
        int epicIndex,
        int statusIndex,
        int issueIndex)
    {
        var space = organization.GetSpace(spaceIndex);
        
        var status = organization.GetStatus(spaceIndex, epicIndex, statusIndex);
        var issue = status.Issues![issueIndex];

        var issueKey = new IssueKey(space.Key, issue.IssueNumber!.Number).ToString();
        
        return new IssueData(issueKey, issue);
    }
    
    public static Status GetStatus(
        this Organization organization,
        int spaceIndex,
        int epicIndex,
        int statusIndex)
    {
        var epic = organization.GetEpic(spaceIndex, epicIndex);
        return epic.Statuses![statusIndex];
    }
    
    public static Epic GetEpic(
        this Organization organization,
        int spaceIndex,
        int epicIndex)
    {
        var space = organization.GetSpace(spaceIndex);
        return space.Epics![epicIndex];
    }
    
    public static Space GetSpace(
        this Organization organization,
        int spaceIndex)
    {
        return organization.Spaces![spaceIndex];
    }
    
    public static Attribute GetAttribute(
        this Organization organization,
        int attributeIndex)
    {
        return organization.Attributes![attributeIndex];
    }
    
    public static AttributeListValue GetListValue(
        this Attribute attribute,
        int valueIndex)
    {
        return attribute.AttributeListValues![valueIndex];
    }
}