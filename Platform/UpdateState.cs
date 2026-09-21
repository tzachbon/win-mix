namespace Mix.Platform;

enum UpdatePhase { Idle, Checking, Current, Available, Downloading, Verifying, Installing, Error, Canceled, Updated }

sealed record UpdateState(UpdatePhase Phase, string Message, string ActionLabel = "Check for updates", double? Progress = null, bool CanCancel = false)
{
    public bool Busy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Verifying or UpdatePhase.Installing;
}

sealed record UpdateRelease(Version Version, Uri DownloadUri, long Size, string Sha256);
