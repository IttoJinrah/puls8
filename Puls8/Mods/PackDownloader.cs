using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Puls8.Net;

namespace Puls8.Mods;

public static class PackDownloader
{
    private const int BufferSize = 1 << 17;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    public static string TempDirectory => Path.Combine(Path.GetTempPath(), "Puls8");

    public static async Task<string> DownloadAsync(PackRelease release, InstallProgress progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(TempDirectory);
        var target = Path.Combine(TempDirectory, $"{Guid.NewGuid():N}.pmp");
        progress.BeginBytes(release.Size);

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stall.CancelAfter(StallTimeout);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var response = await Http.Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InstallException(Problems.Network($"download answered {(int)response.StatusCode}"));
            }

            await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true);
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                progress.AddBytes(read);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            TryDelete(target);
            throw new InstallException(Problems.Network("the download stalled for 30 seconds"));
        }
        catch (HttpRequestException exception)
        {
            TryDelete(target);
            throw new InstallException(Problems.Network(exception.Message), exception);
        }
        catch
        {
            TryDelete(target);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        var sizeMatches = release.Size <= 0 || new FileInfo(target).Length == release.Size;
        var hashMatches = release.Sha256.Length == 0 || string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!sizeMatches || !hashMatches)
        {
            TryDelete(target);
            throw new InstallException(Problems.DownloadCorrupt);
        }

        return target;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Leftovers from installs interrupted by a crash or game exit.
    public static void SweepTempDirectory()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
