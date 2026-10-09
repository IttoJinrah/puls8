using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace Puls8.Mods;

public static class PackFiles
{
    private const string MetaFileName = "meta.json";
    private const string StagingPrefix = ".puls8-staging-";
    private const int RetryAttempts = 6;
    private const int FirstRetryDelayMilliseconds = 250;
    private const int DiskFullHResult = unchecked((int)0x80070070);
    private const int HandleDiskFullHResult = unchecked((int)0x80070027);
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private const int LockViolationHResult = unchecked((int)0x80070021);

    // Extracted packs are larger than the archive; three times the download covers the archive, staging copy and headroom.
    public const int DiskSpaceFactor = 3;

    public static void EnsureDiskSpace(string modRoot, long packBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(modRoot));
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var needed = packBytes * DiskSpaceFactor;
        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < needed)
        {
            throw new InstallException(Problems.DiskFull(root, needed));
        }
    }

    public static string StagingDirectory(string modRoot)
    {
        var parent = Directory.GetParent(Path.GetFullPath(modRoot).TrimEnd(Path.DirectorySeparatorChar));
        var home = parent is not null && IsWritable(parent.FullName) ? parent.FullName : modRoot;
        return Path.Combine(home, StagingPrefix + Guid.NewGuid().ToString("N"));
    }

    public static void Extract(string archivePath, string stagingPath, InstallProgress progress)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            ValidateMeta(archive);
            var stagingRoot = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(stagingRoot);
            var entries = archive.Entries;
            progress.BeginItems(entries.Count);
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                progress.AddItem();
                var relative = entry.FullName.Replace('\\', '/');
                var destination = Path.GetFullPath(Path.Combine(stagingRoot, relative));
                if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InstallException(Problems.DownloadCorrupt);
                }

                if (relative.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new InstallException(Problems.DownloadCorrupt, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Classify(exception, stagingPath);
        }
    }

    public static async Task DeleteWithRetriesAsync(string path, CancellationToken cancellation)
    {
        var delay = FirstRetryDelayMilliseconds;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    ClearReadOnly(path);
                    Directory.Delete(path, true);
                }

                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < RetryAttempts)
            {
                await Task.Delay(delay, cancellation).ConfigureAwait(false);
                delay *= 2;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Classify(exception, path);
            }
        }
    }

    public static async Task MoveWithRetriesAsync(string source, string destination, CancellationToken cancellation)
    {
        var delay = FirstRetryDelayMilliseconds;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < RetryAttempts)
            {
                await Task.Delay(delay, cancellation).ConfigureAwait(false);
                delay *= 2;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Classify(exception, destination);
            }
        }
    }

    public static void SweepStaging(string modRoot)
    {
        var staging = StagingDirectory(modRoot);
        var home = Path.GetDirectoryName(staging);
        if (home is null || !Directory.Exists(home))
        {
            return;
        }

        try
        {
            var leftovers = Directory.GetDirectories(home, StagingPrefix + "*");
            for (var leftoverIndex = 0; leftoverIndex < leftovers.Length; leftoverIndex++)
            {
                Directory.Delete(leftovers[leftoverIndex], true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static InstallException Classify(Exception exception, string path)
    {
        if (exception is UnauthorizedAccessException)
        {
            return new InstallException(Problems.AccessDenied(path), exception);
        }

        var code = exception.HResult;
        if (code is DiskFullHResult or HandleDiskFullHResult)
        {
            return new InstallException(Problems.DiskFull(Path.GetPathRoot(path) ?? path, 0), exception);
        }

        if (code is SharingViolationHResult or LockViolationHResult || exception is IOException)
        {
            return new InstallException(Problems.FilesInUse(path), exception);
        }

        return new InstallException(Problems.Unexpected(exception.Message), exception);
    }

    private static void ValidateMeta(ZipArchive archive)
    {
        var meta = archive.GetEntry(MetaFileName);
        if (meta is null)
        {
            throw new InstallException(Problems.DownloadCorrupt);
        }

        using var stream = meta.Open();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("Name", out var name) || string.IsNullOrWhiteSpace(name.GetString()))
        {
            throw new InstallException(Problems.DownloadCorrupt);
        }
    }

    private static void ClearReadOnly(string path)
    {
        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            var attributes = File.GetAttributes(files[fileIndex]);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(files[fileIndex], attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    private static bool IsWritable(string directory)
    {
        var probe = Path.Combine(directory, StagingPrefix + "probe");
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
