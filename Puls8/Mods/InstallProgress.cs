namespace Puls8.Mods;

public enum InstallStage : byte
{
    Idle,
    Checking,
    Downloading,
    Unpacking,
    Swapping,
    Registering,
    Enabling,
    Linking,
    Done,
    Failed,
}

// Written by the install worker, read by the draw thread; a torn read only shows one stale frame of progress.
public sealed class InstallProgress
{
    private long bytesDone;
    private long bytesTotal;
    private int itemsDone;
    private int itemsTotal;
    private InstallStage stage;

    public InstallStage Stage
    {
        get => stage;
        set
        {
            stage = value;
            if (value is not (InstallStage.Failed or InstallStage.Idle))
            {
                ReachedStage = value;
            }
        }
    }

    public InstallStage ReachedStage { get; private set; }

    public long BytesDone => Interlocked.Read(ref bytesDone);

    public long BytesTotal => Interlocked.Read(ref bytesTotal);

    public float Fraction
    {
        get
        {
            if (Stage == InstallStage.Downloading)
            {
                var total = BytesTotal;
                return total > 0 ? Math.Clamp((float)BytesDone / total, 0f, 1f) : 0f;
            }

            if (Stage == InstallStage.Unpacking)
            {
                var total = Volatile.Read(ref itemsTotal);
                return total > 0 ? Math.Clamp((float)Volatile.Read(ref itemsDone) / total, 0f, 1f) : 0f;
            }

            return Stage == InstallStage.Done ? 1f : 0f;
        }
    }

    public void Reset()
    {
        Stage = InstallStage.Idle;
        ReachedStage = InstallStage.Idle;
        Interlocked.Exchange(ref bytesDone, 0);
        Interlocked.Exchange(ref bytesTotal, 0);
        Volatile.Write(ref itemsDone, 0);
        Volatile.Write(ref itemsTotal, 0);
    }

    public void BeginBytes(long total)
    {
        Stage = InstallStage.Downloading;
        Interlocked.Exchange(ref bytesDone, 0);
        Interlocked.Exchange(ref bytesTotal, total);
    }

    public void AddBytes(int count) => Interlocked.Add(ref bytesDone, count);

    public void BeginItems(int total)
    {
        Stage = InstallStage.Unpacking;
        Volatile.Write(ref itemsDone, 0);
        Volatile.Write(ref itemsTotal, total);
    }

    public void AddItem() => Interlocked.Increment(ref itemsDone);
}
