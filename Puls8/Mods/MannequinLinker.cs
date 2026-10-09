using Dalamud.Game.ClientState.Objects.Enums;
using Penumbra.Api.Enums;

namespace Puls8.Mods;

public readonly record struct MannequinSpot(int ObjectIndex, nint Address, string Name, float Distance, bool Linked);

// Temporary: a session-only collection assigned per object. Saved: the legacy persistent "Puls8" collection.
public readonly record struct MannequinTarget(Guid Collection, bool Temporary, int Generation);

public sealed class MannequinLinker
{
    // Penumbra.GameData ActorIdentifierFactory.MannequinIds, sorted for BinarySearch. Matching ids instead of the
    // English object name keeps detection working on German, French and Japanese clients.
    private static readonly uint[] MannequinIds =
    [
        1007137, 1026228, 1026229, 1026986, 1026987, 1026988, 1026989,
        1032291, 1032292, 1032293, 1032294, 1033046, 1033047, 1033658, 1033659,
    ];

    private readonly List<MannequinSpot> spots = new(8);
    private readonly HashSet<nint> attempted = new();
    private readonly HashSet<nint> temporaryLinks = new();
    private uint knownTerritory;
    private int knownGeneration = -1;

    public IReadOnlyList<MannequinSpot> Spots => spots;

    public void Scan(PenumbraBridge penumbra, MannequinTarget target)
    {
        ForgetIfStale(target);
        spots.Clear();
        var player = Services.Objects.LocalPlayer;
        if (player is null || target.Collection == Guid.Empty)
        {
            return;
        }

        var objects = Services.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var gameObject = objects[objectIndex];
            if (gameObject is null || gameObject.ObjectKind != ObjectKind.EventNpc || Array.BinarySearch(MannequinIds, gameObject.BaseId) < 0)
            {
                continue;
            }

            var linked = target.Temporary
                ? temporaryLinks.Contains(gameObject.Address)
                : IsSavedLink(penumbra, gameObject.ObjectIndex, target.Collection);
            var distance = Vector3.Distance(player.Position, gameObject.Position);
            spots.Add(new MannequinSpot(gameObject.ObjectIndex, gameObject.Address, gameObject.Name.TextValue, distance, linked));
        }

        spots.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
    }

    public int LinkAll(PenumbraBridge penumbra, TemporaryVenue venue, MannequinTarget target) => LinkUnlinked(penumbra, venue, target, false);

    // Auto-link tries each object once per visit so a refused assignment can't spam Penumbra every few seconds.
    public int AutoLink(PenumbraBridge penumbra, TemporaryVenue venue, MannequinTarget target)
    {
        Scan(penumbra, target);
        return LinkUnlinked(penumbra, venue, target, true);
    }

    private int LinkUnlinked(PenumbraBridge penumbra, TemporaryVenue venue, MannequinTarget target, bool oncePerVisit)
    {
        var linked = 0;
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var spot = spots[spotIndex];
            if (spot.Linked || (oncePerVisit && !attempted.Add(spot.Address)))
            {
                continue;
            }

            var code = target.Temporary ? venue.Assign(spot.ObjectIndex) : penumbra.AssignCollection(spot.ObjectIndex, target.Collection);
            if (code is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged))
            {
                Services.Log.Warning($"Penumbra refused the mannequin assignment for object {spot.ObjectIndex}: {code}");
                continue;
            }

            if (target.Temporary)
            {
                temporaryLinks.Add(spot.Address);
            }

            penumbra.Redraw(spot.ObjectIndex);
            spots[spotIndex] = spot with { Linked = true };
            linked++;
        }

        return linked;
    }

    // Objects are recreated on zone change and a rebuilt temporary collection has no assignments, so both reset the memory.
    private void ForgetIfStale(MannequinTarget target)
    {
        var territory = Services.ClientState.TerritoryType;
        if (territory == knownTerritory && target.Generation == knownGeneration)
        {
            return;
        }

        knownTerritory = territory;
        knownGeneration = target.Generation;
        attempted.Clear();
        temporaryLinks.Clear();
    }

    private static bool IsSavedLink(PenumbraBridge penumbra, int objectIndex, Guid collection)
    {
        var assignment = penumbra.CollectionFor(objectIndex);
        return assignment.IndividualSet && assignment.EffectiveId == collection;
    }
}
