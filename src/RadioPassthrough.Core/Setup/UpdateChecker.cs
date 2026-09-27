using System.Net.Http.Headers;
using System.Text.Json;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Core.Setup;

public sealed record UpdateInfo(Version Version, string PageUrl, string? DownloadUrl);

// Asks GitHub for the newest release. Nothing is sent except a plain request for that public page; if
// the releases aren't reachable (offline, private repository) it quietly reports nothing.
public static class UpdateChecker
{
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioPassthrough", AppInfo.Version.ToString(3)));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string url = $"https://api.github.com/repos/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases/latest";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return Parse(json.RootElement, AppInfo.Version);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Info($"Update check skipped: {e.Message}");
            return null;
        }
    }

    public static UpdateInfo? Parse(JsonElement release, Version current)
    {
        string? tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        if (version <= current) return null;

        string page = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? AppInfo.ReleasesPage : AppInfo.ReleasesPage;
        string? download = null;
        if (release.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    download = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    break;
                }
            }
        }
        return new UpdateInfo(version, page, download);
    }
}
