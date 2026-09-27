using System.Reflection;

namespace RadioPassthrough.Core;

public static class AppInfo
{
    public const string Name = "Radio Passthrough";
    public const string ExeName = "RadioPassthrough.exe";
    public const string RepositoryOwner = "cagboi5555555-ship-it";
    public const string RepositoryName = "radio-passthrough";

    public static Version Version { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly).GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 0, 0);

    public static string ReleasesPage => $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";
}
