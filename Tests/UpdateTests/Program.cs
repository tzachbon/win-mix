using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mix.Platform;

internal static class Program
{
    const string Tag = "v1.0.2";
    static readonly Version ReleaseVersion = new(1, 0, 2);
    static readonly Uri InstallerUri = new("https://github.com/tzachbon/win-mix/releases/download/v1.0.2/win-mix-Setup-1.0.2-x64.exe");
    static int assertions;

    static async Task<int> Main()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"WinMix.UpdateTests.{Guid.NewGuid():N}");
        if (!Path.GetFullPath(tempRoot).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test directory escaped the temporary directory.");
        Directory.CreateDirectory(tempRoot);
        try
        {
            ReleaseMetadata();
            await MetadataSizeLimits();
            await DownloadRedirectPolicy(tempRoot);
            await DownloadValidation(tempRoot);
            await SuccessfulDownload(tempRoot);
            CacheOwnsOnlyItsFiles(tempRoot);
            await ControllerDuplicateAndCancel(tempRoot);
            await ControllerRetriesAndMapsTimeout(tempRoot);
            await ControllerCancelDownload(tempRoot);
            await InvalidDownloadNeverLaunches(tempRoot);
            await ControllerLaunchFailureCanRetry(tempRoot);
            Console.WriteLine($"Update checks passed ({assertions} assertions).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    static void ReleaseMetadata()
    {
        var release = UpdateClient.ParseRelease(Metadata(), new Version(1, 0, 1));
        Equal(ReleaseVersion, release!.Version, "newer release is offered");
        Equal(InstallerUri, release.DownloadUri, "installer URL is canonical");
        Equal(null, UpdateClient.ParseRelease(Metadata(), ReleaseVersion), "equal release is current");
        Equal(null, UpdateClient.ParseRelease(Metadata(), new Version(1, 0, 3)), "older release is current");

        foreach (var tag in new[] { "1.0.2", "v01.0.2", "v1.0.65536", "v1.0.2.0", "v1.0.2-rc.1" })
            Throws<InvalidDataException>(() => UpdateClient.ParseRelease(Metadata(tag: tag), new Version(1, 0, 1)), $"reject tag {tag}");

        Throws<InvalidDataException>(() => UpdateClient.ParseRelease(Metadata(draft: true), new Version(1, 0, 1)), "reject draft");
        Throws<InvalidDataException>(() => UpdateClient.ParseRelease(Metadata(prerelease: true), new Version(1, 0, 1)), "reject prerelease");

        foreach (var json in new[]
        {
            Metadata(assetCount: 0),
            Metadata(assetCount: 2),
            Metadata(downloadUrl: "https://example.com/setup.exe"),
            Metadata(state: "new"),
            Metadata(size: 0),
            Metadata(size: UpdateClient.MaxInstallerBytes + 1),
            Metadata(digest: "md5:bad"),
            Metadata(digest: "")
        })
            Throws<InvalidDataException>(() => UpdateClient.ParseRelease(json, new Version(1, 0, 1)), "reject unverified release asset");
    }

    static async Task MetadataSizeLimits()
    {
        var tooLarge = new byte[1024 * 1024 + 1];
        using (var client = new UpdateClient(new Handler((_, _) => Task.FromResult(Ok(new ByteArrayContent(tooLarge))))))
            await ThrowsAsync<InvalidDataException>(() => client.CheckAsync(new Version(1, 0, 1), default), "reject oversized metadata Content-Length");

        using (var client = new UpdateClient(new Handler((_, _) => Task.FromResult(Ok(new StreamContent(new ChunkStream(tooLarge)))))))
            await ThrowsAsync<InvalidDataException>(() => client.CheckAsync(new Version(1, 0, 1), default), "reject oversized streamed metadata");
    }

    static async Task DownloadRedirectPolicy(string root)
    {
        await RedirectRejected(root, "untrusted-redirect", "https://example.com/installer.exe", 1);
        await RedirectRejected(root, "insecure-redirect", "http://github.com/installer.exe", 1);

        var loop = new Handler((_, _) => Task.FromResult(Redirect(InstallerUri)));
        using var client = new UpdateClient(loop);
        await ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release(new byte[] { 1 }), new UpdateCache(Folder(root, "redirect-limit")), _ => { }, () => { }, default), "reject more than five redirects");
        Equal(6, loop.RequestCount, "redirect limit stops after six responses");

        var blocked = new Handler((_, _) => Task.FromResult(Ok(new ByteArrayContent(new byte[] { 1 }))));
        using var blockedClient = new UpdateClient(blocked);
        var insecure = Release(new byte[] { 1 }) with { DownloadUri = new Uri("http://github.com/installer.exe") };
        await ThrowsAsync<InvalidDataException>(() => blockedClient.DownloadAsync(insecure, new UpdateCache(Folder(root, "insecure-start")), _ => { }, () => { }, default), "reject insecure initial download URL");
        Equal(0, blocked.RequestCount, "insecure initial URL is never requested");
    }

    static async Task RedirectRejected(string root, string name, string destination, int expectedRequests)
    {
        var handler = new Handler((_, _) => Task.FromResult(Redirect(new Uri(destination))));
        using var client = new UpdateClient(handler);
        var directory = Folder(root, name);
        await ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(Release(new byte[] { 1 }), new UpdateCache(directory), _ => { }, () => { }, default), $"reject {name}");
        Equal(expectedRequests, handler.RequestCount, $"stop at {name}");
        NoCachedInstaller(directory, $"remove redirected {name}");
    }

    static async Task DownloadValidation(string root)
    {
        var shortBytes = new byte[] { 0x41, 0x42, 0x43 };
        await RejectDownload(root, "content-length", Release(shortBytes, size: 4), (_, _) => Task.FromResult(Ok(new ByteArrayContent(shortBytes))));
        await RejectDownload(root, "stream-oversize", Release(shortBytes, size: 2), (_, _) => Task.FromResult(Ok(new StreamContent(new ChunkStream([0x41, 0x42, 0x43])))));
        await RejectDownload(root, "truncated", Release(shortBytes, size: 4), (_, _) => Task.FromResult(Ok(new StreamContent(new ChunkStream(shortBytes)))));
        await RejectDownload(root, "bad-hash", Release(shortBytes, digest: new string('0', 64)), (_, _) => Task.FromResult(Ok(new ByteArrayContent(shortBytes))));

        var pe = FixtureBytes();
        await RejectDownload(root, "version-mismatch", Release(pe, version: new Version(1, 0, 3)), (_, _) => Task.FromResult(Ok(new ByteArrayContent(pe))));

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        using var cancelClient = new UpdateClient(new Handler(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok();
        }));
        var cancelCache = new UpdateCache(Folder(root, "download-cancel"));
        var pending = cancelClient.DownloadAsync(Release(new byte[] { 1 }), cancelCache, _ => { }, () => { }, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => pending, "propagate canceled download");
        NoCachedInstaller(Path.Combine(root, "download-cancel"), "canceled download is removed");
    }

    static async Task RejectDownload(string root, string name, UpdateRelease release,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        using var client = new UpdateClient(new Handler(response));
        var directory = Folder(root, name);
        await ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(release, new UpdateCache(directory), _ => { }, () => { }, default), $"reject {name}");
        NoCachedInstaller(directory, $"remove rejected {name}");
    }

    static async Task SuccessfulDownload(string root)
    {
        var payload = FixtureBytes();
        Array.Resize(ref payload, payload.Length + 180_000);
        var progress = new List<double>();
        var verifying = 0;
        var directory = Folder(root, "successful-download");
        using var client = new UpdateClient(new Handler((_, _) => Task.FromResult(Ok(new ByteArrayContent(payload)))));
        var path = await client.DownloadAsync(Release(payload), new UpdateCache(directory), progress.Add, () => verifying++, default);

        True(File.Exists(path), "verified installer exists");
        True(Regex.IsMatch(Path.GetFileName(path), @"\A[0-9a-f]{32}\.win-mix-update\.exe\z"), "verified installer receives owned exe name");
        Equal(1, verifying, "verification callback runs once");
        True(progress.Count > 1 && progress[^1] == 100 && progress.SequenceEqual(progress.Order()), "download reports increasing progress through 100 percent");
        True(File.ReadAllBytes(path).SequenceEqual(payload), "verified installer bytes are preserved");
        Equal(new Version(1, 0, 2, 0), FileVersion(path), "fixture PE version matches release");
        Equal(0, Directory.EnumerateFiles(directory, "*.partial").Count(), "partial file is renamed only after validation");
    }

    static void CacheOwnsOnlyItsFiles(string root)
    {
        var directory = Folder(root, "cache-ownership");
        var cache = new UpdateCache(directory);
        var partial = cache.CreatePartial();
        True(Regex.IsMatch(Path.GetFileName(partial), @"\A[0-9a-f]{32}\.win-mix-update\.partial\z"), "partial names are owned lowercase GUIDs");
        File.WriteAllText(partial, "partial");
        var exe = Path.Combine(directory, $"{Guid.NewGuid():N}.win-mix-update.exe");
        var unrelated = Path.Combine(directory, "keep.txt");
        var uppercase = Path.Combine(directory, $"{Guid.NewGuid():N}".ToUpperInvariant() + ".win-mix-update.exe");
        var nestedDirectory = Folder(directory, "nested");
        var nestedOwnedName = Path.Combine(nestedDirectory, $"{Guid.NewGuid():N}.win-mix-update.exe");
        File.WriteAllText(exe, "exe");
        File.WriteAllText(unrelated, "keep");
        File.WriteAllText(uppercase, "keep");
        File.WriteAllText(nestedOwnedName, "keep");

        using (var locked = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            cache.Remove(exe);
            True(File.Exists(exe), "locked owned file survives Remove");
            cache.Cleanup();
            True(File.Exists(exe), "locked owned file survives Cleanup");
        }

        cache.Cleanup();
        False(File.Exists(partial), "cleanup removes owned partial files");
        False(File.Exists(exe), "cleanup removes unlocked owned exe files");
        True(File.Exists(unrelated) && File.Exists(uppercase), "cleanup preserves unrelated and noncanonical names");
        True(File.Exists(nestedOwnedName), "cleanup does not recurse into subdirectories");
    }

    static async Task ControllerDuplicateAndCancel(string root)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok();
        });
        using var controller = NewController(root, "controller-check-cancel", handler, new Version(1, 0, 1), _ => throw new InvalidOperationException("must not launch"));
        var pending = controller.RunAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Equal(UpdatePhase.Checking, controller.State.Phase, "controller enters checking");
        await controller.RunAsync();
        Equal(1, handler.RequestCount, "duplicate check is ignored while pending");
        controller.Cancel();
        await pending;
        Equal(UpdatePhase.Canceled, controller.State.Phase, "check cancellation reaches canceled state");
    }

    static async Task ControllerRetriesAndMapsTimeout(string root)
    {
        var attempts = 0;
        var retryHandler = new Handler((_, _) => Task.FromResult(
            Interlocked.Increment(ref attempts) == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Ok(new ByteArrayContent(Metadata()))));
        using (var controller = NewController(root, "controller-retry", retryHandler, new Version(1, 0, 1), _ => throw new InvalidOperationException("must not launch")))
        {
            await controller.RunAsync();
            Equal(UpdatePhase.Error, controller.State.Phase, "request failure reaches error state");
            await controller.RunAsync();
            Equal(UpdatePhase.Available, controller.State.Phase, "retry checks again and offers release");
            Equal(2, retryHandler.RequestCount, "retry performs a second metadata request");
        }

        var timeoutHandler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("simulated timeout")));
        using var timed = NewController(root, "controller-timeout", timeoutHandler, new Version(1, 0, 1), _ => throw new InvalidOperationException("must not launch"));
        await timed.RunAsync();
        Equal(UpdatePhase.Error, timed.State.Phase, "timeout reaches error state");
        True(timed.State.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase), "timeout is distinguished from user cancellation");
    }

    static async Task ControllerCancelDownload(string root)
    {
        var payload = FixtureBytes();
        var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (request, token) =>
        {
            if (request.RequestUri == UpdateClient.Latest) return Ok(new ByteArrayContent(Metadata(payload.Length, Sha256(payload))));
            downloadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok();
        });
        var launchCount = 0;
        using var controller = NewController(root, "controller-download-cancel", handler, new Version(1, 0, 1), _ => launchCount++);
        await controller.RunAsync();
        Equal(UpdatePhase.Available, controller.State.Phase, "download cancellation setup finds update");
        var pending = controller.RunAsync();
        await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Equal(UpdatePhase.Downloading, controller.State.Phase, "controller enters downloading");
        controller.Cancel();
        await pending;
        Equal(UpdatePhase.Canceled, controller.State.Phase, "download cancellation reaches canceled state");
        Equal(0, launchCount, "canceled download never launches installer");
        NoCachedInstaller(Path.Combine(root, "controller-download-cancel"), "canceled controller download is removed");
    }

    static async Task InvalidDownloadNeverLaunches(string root)
    {
        var payload = FixtureBytes();
        var handler = new Handler((request, _) => Task.FromResult(request.RequestUri == UpdateClient.Latest
            ? Ok(new ByteArrayContent(Metadata(payload.Length, new string('0', 64))))
            : Ok(new ByteArrayContent(payload))));
        var launchCount = 0;
        using var controller = NewController(root, "controller-invalid-download", handler, new Version(1, 0, 1), _ => launchCount++);
        await controller.RunAsync();
        Equal(UpdatePhase.Available, controller.State.Phase, "invalid download setup finds update");
        await controller.RunAsync();
        Equal(UpdatePhase.Error, controller.State.Phase, "invalid checksum reaches error state");
        Equal(0, launchCount, "invalid installer is never launched");
        NoCachedInstaller(Path.Combine(root, "controller-invalid-download"), "invalid controller download is removed");
    }

    static async Task ControllerLaunchFailureCanRetry(string root)
    {
        var payload = FixtureBytes();
        var handler = new Handler((request, _) => Task.FromResult(request.RequestUri == UpdateClient.Latest
            ? Ok(new ByteArrayContent(Metadata(payload.Length, Sha256(payload))))
            : Ok(new ByteArrayContent(payload))));
        var installDirectory = Folder(root, "fake-installation");
        string? launchedPath = null;
        string? launchedDirectory = null;
        var launchCalls = 0;
        using var controller = new UpdateController(new UpdateClient(handler), new UpdateCache(Folder(root, "controller-launch-failure")),
            new Version(1, 0, 1), () => installDirectory, (path, directory) =>
            {
                launchCalls++;
                launchedPath = path;
                launchedDirectory = directory;
                throw new IOException("controlled launch failure");
            });

        await controller.RunAsync();
        Equal(UpdatePhase.Available, controller.State.Phase, "launch test setup finds update");
        await controller.RunAsync();
        Equal(UpdatePhase.Error, controller.State.Phase, "launch failure reaches error state");
        Equal(1, launchCalls, "verified installer is passed to injected launcher once");
        True(launchedPath is not null && launchedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "launcher receives verified exe path");
        Equal(installDirectory, launchedDirectory, "launcher receives configured installation directory");
        False(File.Exists(launchedPath!), "unhanded installer is removed after launch failure");

        await controller.RunAsync();
        Equal(UpdatePhase.Available, controller.State.Phase, "launch failure can retry with a fresh check");
        Equal(3, handler.RequestCount, "retry performs another metadata request");
    }

    static UpdateController NewController(string root, string name, Handler handler, Version installed, Func<string, int> launch) =>
        new(new UpdateClient(handler), new UpdateCache(Folder(root, name)), installed, () => Folder(root, "installed"), (path, _) =>
        {
            launch(path);
            throw new IOException("controlled test launcher");
        });

    static byte[] FixtureBytes()
    {
        var path = Assembly.GetExecutingAssembly().Location;
        Equal(new Version(1, 0, 2, 0), FileVersion(path), "test assembly has fixture release file version");
        return File.ReadAllBytes(path);
    }

    static Version FileVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    static UpdateRelease Release(byte[] bytes, long? size = null, string? digest = null, Version? version = null) =>
        new(version ?? ReleaseVersion, InstallerUri, size ?? bytes.LongLength, digest ?? Sha256(bytes));

    static byte[] Metadata(long size = 1, string? digest = null, string tag = Tag, bool draft = false,
        bool prerelease = false, string state = "uploaded", string? downloadUrl = null, int assetCount = 1)
    {
        var version = tag.StartsWith('v') ? tag[1..] : "1.0.2";
        var assets = new JsonArray();
        for (var index = 0; index < assetCount; index++)
            assets.Add(new JsonObject
            {
                ["name"] = $"win-mix-Setup-{version}-x64.exe",
                ["size"] = size,
                ["digest"] = digest == null ? "sha256:" + new string('a', 64) : "sha256:" + digest,
                ["state"] = state,
                ["browser_download_url"] = downloadUrl ?? $"https://github.com/tzachbon/win-mix/releases/download/{tag}/win-mix-Setup-{version}-x64.exe"
            });
        return Encoding.UTF8.GetBytes(new JsonObject
        {
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["tag_name"] = tag,
            ["assets"] = assets
        }.ToJsonString());
    }

    static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static string Folder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    static void NoCachedInstaller(string directory, string message)
    {
        var files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory).ToArray() : [];
        True(files.Length == 0, message + (files.Length == 0 ? "" : $": {string.Join(", ", files.Select(Path.GetFileName))}"));
    }

    static HttpResponseMessage Ok(HttpContent? content = null) => new(HttpStatusCode.OK) { Content = content ?? new ByteArrayContent([]) };
    static HttpResponseMessage Redirect(Uri location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = location;
        return response;
    }

    static void True(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    static void False(bool value, string message) => True(!value, message);

    static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    static void Throws<TException>(Action action, string message) where TException : Exception
    {
        assertions++;
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
    }

    static async Task ThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        assertions++;
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
    }

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        int requestCount;
        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            return send(request, cancellationToken);
        }
    }

    sealed class ChunkStream(byte[] bytes) : Stream
    {
        int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var length = Math.Min(count, bytes.Length - position);
            bytes.AsSpan(position, length).CopyTo(buffer.AsSpan(offset, length));
            position += length;
            return length;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(buffer.Length, bytes.Length - position);
            bytes.AsMemory(position, length).CopyTo(buffer);
            position += length;
            return ValueTask.FromResult(length);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
