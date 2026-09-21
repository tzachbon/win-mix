using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mix.Platform;

sealed class UpdateClient : IDisposable
{
    internal const long MaxInstallerBytes = 256L * 1024 * 1024;
    const int MaxMetadataBytes = 1024 * 1024;
    internal static readonly Uri Latest = new("https://api.github.com/repos/tzachbon/win-mix/releases/latest");
    readonly HttpClient http;

    public UpdateClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false });
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WinMix-Updater/1.0");
    }

    internal static Version ParseVersion(string? tag)
    {
        if (tag == null || !Regex.IsMatch(tag, @"\Av(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z") ||
            !Version.TryParse(tag[1..], out var version) || version.Major > 65535 || version.Minor > 65535 || version.Build > 65535)
            throw new InvalidDataException("The release version is invalid.");
        return version;
    }

    internal static UpdateRelease? ParseRelease(byte[] json, Version installed)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("This is not a published stable release.");
        var version = ParseVersion(root.GetProperty("tag_name").GetString());
        if (version <= new Version(installed.Major, installed.Minor, installed.Build)) return null;
        var name = $"win-mix-Setup-{version}-x64.exe";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The release installer is missing or ambiguous. Try again later.");
        var asset = assets[0];
        var size = asset.GetProperty("size").GetInt64();
        var digest = asset.GetProperty("digest").GetString();
        var expectedUrl = $"https://github.com/tzachbon/win-mix/releases/download/v{version}/{name}";
        if (asset.GetProperty("state").GetString() != "uploaded" || size <= 0 || size > MaxInstallerBytes ||
            digest == null || !Regex.IsMatch(digest, @"\Asha256:[0-9a-fA-F]{64}\z") ||
            asset.GetProperty("browser_download_url").GetString() != expectedUrl)
            throw new InvalidDataException("The release installer could not be verified. Try again later.");
        return new UpdateRelease(version, new Uri(expectedUrl), size, digest[7..]);
    }

    public async Task<UpdateRelease?> CheckAsync(Version installed, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync(Latest, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if ((int)response.StatusCode is 403 or 429) throw new HttpRequestException("GitHub is limiting update checks. Try again later.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxMetadataBytes) throw new InvalidDataException("Release information is too large.");
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (data.Length + read > MaxMetadataBytes) throw new InvalidDataException("Release information is too large.");
            data.Write(buffer, 0, read);
        }
        return ParseRelease(data.ToArray(), installed);
    }

    static bool AllowedDownload(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.Host is "github.com" or "release-assets.githubusercontent.com";

    async Task<HttpResponseMessage> OpenDownload(Uri uri, CancellationToken cancellation)
    {
        for (var redirects = 0; ; redirects++)
        {
            if (!AllowedDownload(uri)) throw new InvalidDataException("The installer download was redirected outside GitHub.");
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (redirects == 5 || location == null) throw new InvalidDataException("The installer download has too many redirects.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
    }

    public async Task<string> DownloadAsync(UpdateRelease release, UpdateCache cache, Action<double> progress, Action verifying, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var partial = cache.CreatePartial();
        var ready = Path.ChangeExtension(partial, ".exe");
        try
        {
            using var response = await OpenDownload(release.DownloadUri, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("The installer download has an unexpected size.");
            using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                int read, reported = -1;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > release.Size || total > MaxInstallerBytes) throw new InvalidDataException("The installer download is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    var percent = (int)(total * 100 / release.Size);
                    if (percent != reported) { reported = percent; progress(percent); }
                }
            }
            token.ThrowIfCancellationRequested();
            verifying();
            if (total != release.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer checksum does not match. Nothing was installed.");
            var info = FileVersionInfo.GetVersionInfo(partial);
            if (new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart) !=
                new Version(release.Version.Major, release.Version.Minor, release.Version.Build, 0))
                throw new InvalidDataException("The installer version does not match the release.");
            token.ThrowIfCancellationRequested();
            File.Move(partial, ready);
            return ready;
        }
        catch { cache.Remove(partial); cache.Remove(ready); throw; }
    }

    public void Dispose() => http.Dispose();
}
