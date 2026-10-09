using Puls8.Venue;

namespace Puls8.Travel;

public enum TravelState : byte
{
    Idle,
    Starting,
    Travelling,
    Arrived,
    Stopped,
    Failed,
}

public sealed class TravelService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(6);

    private readonly LifestreamBridge lifestream;
    private AddressBookEntryTuple entry;
    private DateTime nextPollUtc;

    public TravelService(LifestreamBridge lifestream)
    {
        this.lifestream = lifestream;
    }

    public TravelState State { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTime StartedUtc { get; private set; }

    public bool IsActive => State is TravelState.Starting or TravelState.Travelling;

    public bool LifestreamAvailable { get; private set; }

    public bool IsInsideVenue { get; private set; }

    public void Go(VenueAddress address)
    {
        if (IsActive)
        {
            return;
        }

        if (!LifestreamBridge.IsInstalled())
        {
            Finish(TravelState.Failed, "Lifestream isn't installed or enabled.");
            return;
        }

        if (!lifestream.TryBuild(address.World, address.District, address.Ward, address.Plot, out entry))
        {
            Finish(TravelState.Failed, "Lifestream didn't recognise the venue address.");
            return;
        }

        if (lifestream.IsHere(entry))
        {
            Finish(TravelState.Arrived, "You're already at the door. Step inside!");
            return;
        }

        if (!lifestream.TryGo(entry))
        {
            Finish(TravelState.Failed, "Lifestream refused the trip. Check /xllog.");
            return;
        }

        State = TravelState.Starting;
        Message = "Plotting a course...";
        StartedUtc = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (!IsActive)
        {
            return;
        }

        lifestream.Abort();
        Finish(TravelState.Stopped, "Trip cancelled.");
    }

    public void Dismiss()
    {
        if (!IsActive)
        {
            State = TravelState.Idle;
            Message = string.Empty;
        }
    }

    public void Tick(VenueAddress address)
    {
        var now = DateTime.UtcNow;
        if (now < nextPollUtc)
        {
            return;
        }

        nextPollUtc = now + PollInterval;
        IsInsideVenue = VenueLocator.IsInside(address);
        if (!IsActive)
        {
            LifestreamAvailable = LifestreamBridge.IsInstalled();
            return;
        }

        var busy = lifestream.IsBusy();
        if (State == TravelState.Starting)
        {
            if (busy)
            {
                State = TravelState.Travelling;
                Message = "On the way. Sit back, Lifestream has the wheel.";
            }
            else if (now - StartedUtc > StartGrace)
            {
                Finish(TravelState.Failed, "Lifestream didn't start the trip. Are you in combat or a duty?");
            }

            return;
        }

        if (busy)
        {
            return;
        }

        if (lifestream.IsHere(entry) || IsInsideVenue)
        {
            Finish(TravelState.Arrived, "You made it. Head inside, the music's on!");
            return;
        }

        Finish(TravelState.Stopped, "The trip stopped before reaching the club.");
    }

    private void Finish(TravelState state, string message)
    {
        State = state;
        Message = message;
    }
}
