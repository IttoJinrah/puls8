namespace Puls8.Ui;

public struct Spring
{
    public float Value;
    public float Velocity;

    public float Step(float target, float smoothTime, float deltaSeconds)
    {
        var omega = 2f / MathF.Max(0.0001f, smoothTime);
        var x = omega * deltaSeconds;
        var decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        var difference = Value - target;
        var temp = (Velocity + omega * difference) * deltaSeconds;
        Velocity = (Velocity - omega * temp) * decay;
        var result = target + (difference + temp) * decay;
        if (target - Value > 0f == result > target)
        {
            result = target;
            Velocity = 0f;
        }

        Value = result;
        return result;
    }

    public void Snap(float value)
    {
        Value = value;
        Velocity = 0f;
    }
}

public static class Motion
{
    public const double HeartbeatPeriodMs = 1250.0;

    public static bool Reduced { get; set; }

    public static float Delta => MathF.Min(Dalamud.Bindings.ImGui.ImGui.GetIO().DeltaTime, 0.1f);

    public static float Seconds => (float)(Environment.TickCount64 % 3_600_000L / 1000.0);

    public static float Phase(double periodMs) => Reduced ? 0f : (float)(Environment.TickCount64 % (long)periodMs / periodMs);

    public static float Wave(double periodMs) => Reduced ? 0.5f : (MathF.Sin(Phase(periodMs) * MathF.Tau) + 1f) * 0.5f;

    // Double-bump "lub-dub" envelope that matches the ECG line in the logo.
    public static float Heartbeat()
    {
        if (Reduced)
        {
            return 0f;
        }

        var phase = Phase(HeartbeatPeriodMs);
        return MathF.Max(Bump(phase, 0.06f, 0.06f), Bump(phase, 0.22f, 0.07f) * 0.6f);
    }

    public static float EaseOutCubic(float value)
    {
        var inverse = 1f - Math.Clamp(value, 0f, 1f);
        return 1f - inverse * inverse * inverse;
    }

    // Deterministic per-step noise in [-1, 1]; no Random so nothing allocates per frame.
    public static float Hash(int step, int lane)
    {
        var hash = (uint)((step * 73856093) ^ ((lane + 1) * 19349663));
        hash ^= hash >> 13;
        hash *= 2654435761u;
        hash ^= hash >> 16;
        return hash % 2000u / 1000f - 1f;
    }

    private static float Bump(float phase, float center, float width)
    {
        var distance = (phase - center) / width;
        return distance is < -1f or > 1f ? 0f : 0.5f * (1f + MathF.Cos(distance * MathF.PI));
    }
}
