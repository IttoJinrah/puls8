namespace Puls8.Mods;

public enum FixAction : byte
{
    None,
    Retry,
    OpenPluginInstaller,
    OpenPenumbraSettings,
    OpenPenumbraCollections,
    OpenPenumbraMods,
}

public enum ProblemSeverity : byte
{
    Blocking,
    Warning,
}

public sealed record Problem(string Title, string Detail, FixAction Fix, string FixLabel, ProblemSeverity Severity = ProblemSeverity.Blocking);

public sealed class InstallException : Exception
{
    public InstallException(Problem problem, Exception? inner = null)
        : base(problem.Title, inner)
    {
        Problem = problem;
    }

    public Problem Problem { get; }
}

public static class Problems
{
    public static readonly Problem PenumbraMissing = new(
        "Penumbra isn't running",
        "Puls8 installs its packs through Penumbra. Install or enable Penumbra in the plugin installer, then come back here.",
        FixAction.OpenPluginInstaller,
        "Open plugin installer");

    public static readonly Problem ModDirectoryMissing = new(
        "Penumbra has no mod folder yet",
        "Penumbra needs a root folder for mods before anything can be installed. Pick one in Penumbra's settings (a short path like C:\\PenumbraMods works best).",
        FixAction.OpenPenumbraSettings,
        "Open Penumbra settings");

    public static readonly Problem ModsDisabled = new(
        "Penumbra mods are switched off",
        "The packs will install, but you won't see them until you turn mods back on in Penumbra's settings.",
        FixAction.OpenPenumbraSettings,
        "Open Penumbra settings",
        ProblemSeverity.Warning);

    public static readonly Problem BaseCollectionMissing = new(
        "No Base collection assigned",
        "The Venue Pack goes into your Base collection, but Penumbra has none assigned. Assign one on Penumbra's Collections tab.",
        FixAction.OpenPenumbraCollections,
        "Open Collections");

    public static readonly Problem DownloadCorrupt = new(
        "The download didn't arrive intact",
        "The file's fingerprint doesn't match the release, usually a dropped connection or an antivirus intercepting it. Retrying downloads a fresh copy.",
        FixAction.Retry,
        "Download again");

    public static readonly Problem PenumbraRejected = new(
        "Penumbra didn't accept the pack",
        "The files were copied but Penumbra didn't load them. Check /xllog for Penumbra errors, then retry.",
        FixAction.Retry,
        "Try again");

    public static readonly Problem NoRelease = new(
        "Pack not published yet",
        "The venue hasn't published this pack on GitHub yet. Nothing to install for now.",
        FixAction.Retry,
        "Check again");

    public static Problem PenumbraOutdated(int breaking) => new(
        "Penumbra needs an update",
        $"This Penumbra speaks API version {breaking}, Puls8 needs version 5. Update Penumbra in the plugin installer.",
        FixAction.OpenPluginInstaller,
        "Open plugin installer");

    public static Problem CollectionMissing(string name) => new(
        $"Create the \"{name}\" collection",
        $"The Cityscape is shown on the venue mannequin through its own collection. In Penumbra's Collections tab, press \"Create New Empty Collection\", name it {name}, then come back and continue. You only do this once.",
        FixAction.OpenPenumbraCollections,
        "Open Collections");

    public static Problem Network(string detail) => new(
        "Couldn't reach GitHub",
        $"Check your connection and try again. ({detail})",
        FixAction.Retry,
        "Try again");

    public static Problem RateLimited(DateTimeOffset? reset) => new(
        "GitHub asked us to slow down",
        reset is { } moment
            ? $"Too many update checks from your network. GitHub allows more at {moment.ToLocalTime():HH:mm}."
            : "Too many update checks from your network. Wait a few minutes and retry.",
        FixAction.Retry,
        "Try again");

    public static Problem DiskFull(string drive, long neededBytes) => new(
        "Not enough disk space",
        $"Installing needs about {Format.Bytes(neededBytes)} free on {drive}. Free some space and retry.",
        FixAction.Retry,
        "Try again");

    public static Problem FilesInUse(string folder) => new(
        "Pack files are in use",
        $"Something is holding files in {folder} open: often Windows Explorer, an image viewer, an antivirus scan, or Penumbra still compressing the last install. Close those, wait a few seconds, and retry. Restarting the game always clears it.",
        FixAction.Retry,
        "Try again");

    public static Problem AccessDenied(string folder) => new(
        "Windows blocked access to the mod folder",
        $"Puls8 can't write to {folder}. Mod folders inside Program Files or OneDrive often cause this. Move Penumbra's root folder somewhere like C:\\PenumbraMods.",
        FixAction.OpenPenumbraSettings,
        "Open Penumbra settings");

    public static Problem Unexpected(string detail) => new(
        "Something unexpected went wrong",
        $"{detail} Retry, and if it keeps happening send the venue a screenshot of this card and /xllog.",
        FixAction.Retry,
        "Try again");
}

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024d && unitIndex < Units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} B" : $"{value:0.0} {Units[unitIndex]}";
    }
}
