using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace Mix.Platform;

sealed class UpdateController : IDisposable
{
    internal const string ReleasesUrl = "https://github.com/tzachbon/win-mix/releases/latest";
    readonly UpdateClient client;
    readonly UpdateCache cache;
    readonly Version installed;
    readonly Func<string?> installationDirectory;
    readonly Func<string, string, Process> launch;
    CancellationTokenSource? operation;
    UpdateRelease? available;
    int busy;
    public UpdateState State { get; private set; } = new(UpdatePhase.Idle, "");
    public event Action<UpdateState>? Changed;
    public static string InstalledVersion => typeof(UpdateController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public UpdateController(UpdateClient? client = null, UpdateCache? cache = null, Version? installed = null,
        Func<string?>? installationDirectory = null, Func<string, string, Process>? launch = null)
    {
        this.client = client ?? new UpdateClient();
        this.cache = cache ?? new UpdateCache(Path.Combine(Preferences.DirectoryPath, "Updates"));
        this.installed = installed ?? Version.Parse(InstalledVersion);
        this.installationDirectory = installationDirectory ?? RegisteredDirectory;
        this.launch = launch ?? LaunchInstaller;
    }

    void Set(UpdateState value) { State = value; Changed?.Invoke(value); }
    public void Cleanup() => cache.Cleanup();
    public void MarkUpdated() => Set(new(UpdatePhase.Updated, $"Updated to {InstalledVersion}."));
    public void Cancel()
    {
        if (!State.CanCancel && State.Phase != UpdatePhase.Checking) return;
        try { operation?.Cancel(); }
        catch (ObjectDisposedException) { } // Completion may win a concurrent cancellation.
    }

    public async Task RunAsync()
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        string? downloaded = null;
        bool handedOff = false;
        try
        {
            if (State.Phase != UpdatePhase.Available || available == null)
            {
                available = null;
                Set(new(UpdatePhase.Checking, "Checking GitHub for updates…", "Checking…"));
                available = await client.CheckAsync(installed, cancellation.Token).ConfigureAwait(false);
                if (available == null) Set(new(UpdatePhase.Current, "You’re up to date."));
                else Set(new(UpdatePhase.Available, $"Version {available.Version} is available.",
                    installationDirectory() == null ? "Open release page" : $"Update to {available.Version}"));
                return;
            }
            var directory = installationDirectory();
            if (directory == null)
            {
                Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
                return;
            }
            Set(new(UpdatePhase.Downloading, "Downloading update…", "Downloading…", 0, true));
            downloaded = await client.DownloadAsync(available, cache,
                value => Set(new(UpdatePhase.Downloading, $"Downloading update… {value:0}%", "Downloading…", value, true)),
                () => Set(new(UpdatePhase.Verifying, "Verifying installer…", "Verifying…", null, true)), cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!string.Equals(directory, installationDirectory(), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The installation location changed. Check for updates again.");
            Set(new(UpdatePhase.Installing, "The installer will close and reopen Win Mix.", "Installing…"));
            using var process = launch(downloaded, directory);
            handedOff = true;
            // This asynchronous wait does not block the installer's --shutdown request.
            // Normally this process exits first. If setup exits early, allow a retry.
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            Set(new(UpdatePhase.Error, "The installer closed before completing the update. You can try again.", "Retry"));
        }
        catch (OperationCanceledException)
        {
            Set(cancellation.IsCancellationRequested
                ? new(UpdatePhase.Canceled, "Update canceled. Nothing was installed.")
                : new(UpdatePhase.Error, "The update request timed out. Try again.", "Retry"));
        }
        catch (Exception error)
        {
            var message = error switch
            {
                InvalidDataException => error.Message,
                System.ComponentModel.Win32Exception => "Could not start the installer. Nothing was installed. Try again.",
                System.Net.Http.HttpRequestException => "Could not reach GitHub. Check your connection and try again later.",
                IOException or UnauthorizedAccessException => "Could not save the update. Check disk space and permissions, then try again.",
                _ => "Could not read the release information. Try again later."
            };
            Set(new(UpdatePhase.Error, message, "Retry"));
        }
        finally
        {
            if (downloaded != null && !handedOff) cache.Remove(downloaded);
            operation = null;
            Interlocked.Exchange(ref busy, 0);
        }
    }

    internal static string? RegisteredDirectory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{724DA822-9BEC-4E81-8487-2C267218221A}_is1");
            if (key?.GetValue("InstallLocation") is not string location || !Path.IsPathFullyQualified(location)) return null;
            var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
            return string.Equals(directory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Environment.ProcessPath, Path.Combine(directory, "win-mix.exe"), StringComparison.OrdinalIgnoreCase) ? directory : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return null; }
    }

    static Process LaunchInstaller(string path, string directory)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false };
        foreach (var argument in new[] { "/CURRENTUSER", "/SILENT", "/SP-", "/NORESTART", "/WINMIXUPDATE=1", "/DIR=" + directory })
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("The installer could not be started.");
    }

    public void Dispose() { Cancel(); client.Dispose(); }
}
