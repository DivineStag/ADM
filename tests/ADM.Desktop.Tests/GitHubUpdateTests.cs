using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;
namespace ADM.Desktop.Tests;

public sealed class GitHubUpdateTests
{
    private const string Repository = "adm-owner/ADM";
    private static string Json(string digest, string url = "https://github.com/adm-owner/ADM/releases/download/v0.9.0/ADM-Setup.exe", bool prerelease = false) =>
        JsonSerializer.Serialize(new { tag_name = "v0.9.0", draft = false, prerelease, assets = new[] { new { name = "ADM-Setup.exe", browser_download_url = url, digest, size = 5 } } });
    [Theory]
    [InlineData("0.8.0-beta.2", "0.8.0-beta.1", 1)]
    [InlineData("0.8.0-beta.10", "0.8.0-beta.2", 1)]
    [InlineData("0.8.0", "0.8.0-beta.2", 1)]
    [InlineData("0.7.9", "0.8.0-beta.2", -1)]
    [InlineData("v0.8.0", "0.8.0", 0)]
    public void Version_ordering_handles_stable_and_numbered_beta_versions(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(GitHubUpdates.Compare(left, right)));
    [Fact]
    public void Metadata_requires_a_same_repository_HTTPS_installer_and_digest()
    {
        string digest = "sha256:" + new string('A', 64);
        using var valid = JsonDocument.Parse(Json(digest));
        Assert.NotNull(GitHubUpdates.Parse(valid.RootElement, Repository, "0.8.0-beta.2"));
        Assert.Null(GitHubUpdates.Parse(valid.RootElement, Repository, "0.9.0"));
        using var preview = JsonDocument.Parse(Json(digest, prerelease: true));
        Assert.Null(GitHubUpdates.Parse(preview.RootElement, Repository, "0.8.0-beta.2"));
        foreach (string url in new[] { "https://evil.example/ADM-Setup.exe", "http://github.com/adm-owner/ADM/releases/download/v0.9.0/ADM-Setup.exe", "https://github.com/other/ADM/releases/download/v0.9.0/ADM-Setup.exe" })
        {
            using var bad = JsonDocument.Parse(Json(digest, url));
            Assert.Throws<InvalidDataException>(() => GitHubUpdates.Parse(bad.RootElement, Repository, "0.8.0-beta.2"));
        }
        using var missing = JsonDocument.Parse(Json(""));
        Assert.Throws<InvalidDataException>(() => GitHubUpdates.Parse(missing.RootElement, Repository, "0.8.0-beta.2"));
    }
    [Fact]
    public async Task Download_checks_length_hash_and_redirect_host_and_removes_incomplete_files()
    {
        string folder = Path.Combine(Path.GetTempPath(), "adm-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("setup");
            var update = new ReleaseUpdate("0.9.0", new("https://github.com/adm-owner/ADM/releases/tag/v0.9.0"), new("https://github.com/adm-owner/ADM/releases/download/v0.9.0/ADM-Setup.exe"), Convert.ToHexString(SHA256.HashData(payload)), 5);
            using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
            var service = new GitHubUpdates(http, Repository, "0.8.0-beta.2");
            string downloaded = await service.DownloadAsync(update, folder, CancellationToken.None);
            Assert.Equal(payload, File.ReadAllBytes(downloaded));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(update with { Sha256 = new string('0', 64) }, folder, CancellationToken.None));
            Assert.Empty(Directory.EnumerateFiles(folder, "*.partial", SearchOption.AllDirectories));
            using var redirect = new HttpClient(new Handler(_ => { var result = new HttpResponseMessage(HttpStatusCode.Redirect); result.Headers.Location = new("https://evil.example/setup.exe"); return result; }));
            await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdates(redirect, Repository, "0.8.0").DownloadAsync(update, folder, CancellationToken.None));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task Offline_or_unconfigured_updates_do_not_start_an_installer_and_active_handling_blocks_install()
    {
        int calls = 0;
        using var http = new HttpClient(new Handler(_ => { calls++; return new(HttpStatusCode.OK) { Content = new StringContent(Json("sha256:" + new string('A', 64))) }; }));
        var disabled = new GitHubUpdates(http, "", "0.8.0");
        Assert.Null(await disabled.CheckAsync(CancellationToken.None)); Assert.Equal(0, calls);
        bool active = true;
        var vm = new UpdatesViewModel(new(http, Repository, "0.8.0"), () => active);
        await vm.CheckAsync(); Assert.True(vm.Available); Assert.False(vm.CanInstall); Assert.False(vm.InstallCommand.CanExecute(null));
        active = false; vm.Refresh(); Assert.True(vm.InstallCommand.CanExecute(null));
    }
    [Fact]
    public async Task An_empty_repository_does_not_falsely_report_that_the_beta_is_a_published_release()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        var service = new GitHubUpdates(http, "DivineStag/ADM", "0.8.0-beta.4");
        var vm = new UpdatesViewModel(service, () => false);
        await vm.CheckAsync();
        Assert.False(service.HasPublishedRelease); Assert.False(vm.Available);
        Assert.Contains("No public release", vm.Status); Assert.False(vm.InstallCommand.CanExecute(null));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
