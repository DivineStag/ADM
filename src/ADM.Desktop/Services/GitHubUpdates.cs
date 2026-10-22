using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ADM.Desktop.Services;

public sealed record ReleaseUpdate(string Version, Uri Page, Uri Installer, string Sha256, long Size);

/// <summary>Public release metadata only. No account, flight, chat or local file content is transmitted.</summary>
public sealed partial class GitHubUpdates(HttpClient http, string repository, string currentVersion) : IDisposable
{
    public void Dispose() => http.Dispose();
    public const string AssetName = "ADM-Setup.exe";
    public static string ConfiguredRepository => typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "GitHubRepository")?.Value ?? "";
    public bool HasPublishedRelease { get; private set; }
    public bool Configured => RepositoryPattern().IsMatch(repository);
    public Uri ReleasesPage => new($"https://github.com/{repository}/releases");

    public async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!Configured) return null;
        using var response = await GetAsync(new($"https://api.github.com/repos/{repository}/releases/latest"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) { HasPublishedRelease = false; return null; }
        response.EnsureSuccessStatusCode();
        HasPublishedRelease = true;
        byte[] bytes = await ReadLimitedAsync(response.Content, 1_048_576, cancellationToken);
        using var json = JsonDocument.Parse(bytes);
        return Parse(json.RootElement, repository, currentVersion);
    }

    public static ReleaseUpdate? Parse(JsonElement release, string repository, string currentVersion)
    {
        if (!RepositoryPattern().IsMatch(repository)) throw new ArgumentException("Invalid release repository.");
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!VersionPattern().IsMatch(tag) || Compare(tag, currentVersion) <= 0) return null;
        var assets = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == AssetName).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("A single ADM installer is required in the release.");
        var asset = assets[0];
        string? digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
        long size = asset.GetProperty("size").GetInt64();
        if (digest is null || !DigestPattern().IsMatch(digest) || size <= 0 || size > 524_288_000)
            throw new InvalidDataException("The release needs a SHA-256 digest and a valid installer size.");
        string expectedPath = $"/{repository}/releases/download/{tag}/{AssetName}";
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            Uri.UnescapeDataString(uri.AbsolutePath) != expectedPath || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("The installer must belong to the configured GitHub release.");
        return new(tag.TrimStart('v'), new($"https://github.com/{repository}/releases/tag/{tag}"), uri, digest[7..], size);
    }

    public async Task<string> DownloadAsync(ReleaseUpdate update, string cacheRoot, CancellationToken cancellationToken)
    {
        // Revalidate externally supplied release objects before any network or filesystem work.
        if (!Configured || update.Installer.Scheme != "https" || update.Installer.Host != "github.com" || !update.Installer.IsDefaultPort ||
            update.Installer.UserInfo.Length != 0 || update.Installer.Query.Length != 0 || update.Installer.Fragment.Length != 0 ||
            !VersionPattern().IsMatch(update.Version) || !DigestPattern().IsMatch("sha256:" + update.Sha256) ||
            update.Size <= 0 || update.Size > 524_288_000 ||
            !new[] { $"/{repository}/releases/download/v{update.Version}/{AssetName}", $"/{repository}/releases/download/{update.Version}/{AssetName}" }.Contains(update.Installer.AbsolutePath, StringComparer.Ordinal))
            throw new InvalidDataException("Invalid ADM update source.");
        string folder = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, AssetName), partial = target + ".partial";
        try
        {
            using var response = await GetAsync(update.Installer, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != update.Size) throw new InvalidDataException("Installer size changed.");
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                byte[] buffer = new byte[81920]; long total = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > update.Size) throw new InvalidDataException("Installer exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total != update.Size) throw new InvalidDataException("Installer download is incomplete.");
            }
            await using var check = File.OpenRead(partial);
            byte[] actual = await SHA256.HashDataAsync(check, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(update.Sha256))) throw new InvalidDataException("Installer verification failed.");
            check.Close();
            File.Move(partial, target);
            return target;
        }
        catch { if (File.Exists(partial)) File.Delete(partial); throw; }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri address, CancellationToken cancellationToken)
    {
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (address.Scheme != "https" || !address.IsDefaultPort || address.UserInfo.Length != 0 ||
                address.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Unexpected update download host.");
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd("ADM-UpdateCheck/0.8");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            Uri? location = response.Headers.Location; response.Dispose();
            address = location is null ? throw new InvalidDataException("Invalid update redirect.") : new Uri(address, location);
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(); byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > limit) throw new InvalidDataException("Release metadata is too large.");
            output.Write(buffer, 0, count);
        }
    }

    public static int Compare(string left, string right)
    {
        static (Version Core, string? Pre) Split(string value)
        {
            if (!VersionPattern().IsMatch(value)) throw new InvalidDataException("Invalid release version.");
            string[] parts = value.TrimStart('v').Split('-', 2);
            return (Version.Parse(parts[0]), parts.Length == 2 ? parts[1] : null);
        }
        var a = Split(left); var b = Split(right);
        int core = a.Core.CompareTo(b.Core); if (core != 0) return core;
        if (a.Pre is null || b.Pre is null) return a.Pre is null ? b.Pre is null ? 0 : 1 : -1;
        string[] x = a.Pre.Split('.'), y = b.Pre.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xn = long.TryParse(x[i], out long nx), yn = long.TryParse(y[i], out long ny);
            int result = xn && yn ? nx.CompareTo(ny) : xn != yn ? xn ? -1 : 1 : string.CompareOrdinal(x[i], y[i]);
            if (result != 0) return result;
        }
        return x.Length.CompareTo(y.Length);
    }
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
    [GeneratedRegex(@"^v?(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"^sha256:[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
