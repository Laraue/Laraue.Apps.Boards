using System.Text.RegularExpressions;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.Services;

public struct IssueKey
{
    private const string ErrorMessage = "Issue Key should match format like SPA-12345";
    private static readonly Regex IssueFormat = new(@"(\w{3})\-(\d{1,5})", RegexOptions.Compiled);

    public IssueKey(string key)
    {
        var match = IssueFormat.Match(key);
        if (!match.Success)
            throw new BadRequestException(nameof(key), ErrorMessage);
        
        SpaceKey = match.Groups[1].Value; 
        Number = int.Parse(match.Groups[2].Value); 
    }

    public IssueKey(string spaceKey, int number)
    {
        SpaceKey = spaceKey;
        Number = number;
    }

    public int Number { get; }
    public string SpaceKey { get; }

    public override string ToString()
    {
        return $"{SpaceKey}-{Number}";
    }
}