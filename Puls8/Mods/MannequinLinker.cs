using Dalamud.Game.ClientState.Objects.Enums;
using Penumbra.Api.Enums;

namespace Puls8.Mods;

public readonly record struct MannequinSpot(int ObjectIndex, string Name, float Distance, bool Linked);

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
    private readonly HashSet<int> attempted = new();
    private uint attemptedTerritory;

    public IReadOnlyList<MannequinSpot> Spots => spots;

    public void Scan(PenumbraBridge penumbra, Guid collection)
    {
        spots.Clear();
        var player = Services.Objects.LocalPlayer;
        if (player is null)
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

            var assignment = penumbra.CollectionFor(gameObject.ObjectIndex);
            var linked = collection != Guid.Empty && assignment.IndividualSet && assignment.EffectiveId == collection;
            var distance = Vector3.Distance(player.Position, gameObject.Position);
            spots.Add(new MannequinSpot(gameObject.ObjectIndex, gameObject.Name.TextValue, distance, linked));
        }

        spots.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
    }

    public int LinkAll(PenumbraBridge penumbra, Guid collection) => LinkUnlinked(penumbra, collection, false);

    // Auto-link tries each object once per visit so a refused assignment can't spam Penumbra every few seconds.
    public int AutoLink(PenumbraBridge penumbra, Guid collection)
    {
        var territory = Services.ClientState.TerritoryType;
        if (territory != attemptedTerritory)
        {
            attempted.Clear();
            attemptedTerritory = territory;
        }

        Scan(penumbra, collection);
        return LinkUnlinked(penumbra, collection, true);
    }

    private int LinkUnlinked(PenumbraBridge penumbra, Guid collection, bool oncePerVisit)
    {
        var linked = 0;
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var spot = spots[spotIndex];
            if (spot.Linked || (oncePerVisit && !attempted.Add(spot.ObjectIndex)))
            {
                continue;
            }

            var code = penumbra.AssignCollection(spot.ObjectIndex, collection);
            if (code is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged))
            {
                Services.Log.Warning($"Penumbra refused the mannequin assignment for object {spot.ObjectIndex}: {code}");
                continue;
            }

            penumbra.Redraw(spot.ObjectIndex);
            spots[spotIndex] = spot with { Linked = true };
            linked++;
        }

        return linked;
    }
}
