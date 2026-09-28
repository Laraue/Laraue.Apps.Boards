namespace Laraue.Apps.Boards.Services;

public static class Palette
{
    public const string DefaultStatusColor = "#dda61b";
    public const string DefaultBacklogColor = "#2f81f7";
    
    private static readonly Random Random = new (); 
    public static readonly string[] Colors =
    [
        "#e3b341",
        "#ffa657",
        "#d29922",
        "#56d364",
        "#39d353",
        "#3fb950",
        "#79c0ff",
        "#58a6ff",
        "#2f81f7",
        "#a371f7",
        "#f778ba",
        "#ff7b72",
        "#41b6d2",
    ];

    public static string FirstColor => Colors[0];
    public static string RandomColor() => Colors[Random.Next(0, Colors.Length)];
}