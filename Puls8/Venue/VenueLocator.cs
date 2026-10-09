using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Puls8.Venue;

public static class VenueLocator
{
    private const ushort MistTerritory = 339;
    private const ushort LavenderBedsTerritory = 340;
    private const ushort GobletTerritory = 341;
    private const ushort ShiroganeTerritory = 641;
    private const ushort EmpyreumTerritory = 979;

    private static string cachedWorldName = string.Empty;
    private static uint cachedWorldId;

    public static unsafe bool IsInside(VenueAddress address)
    {
        var housing = HousingManager.Instance();
        if (housing is null || !housing->IsInside())
        {
            return false;
        }

        var house = housing->GetCurrentIndoorHouseId();
        if (house.IsApartment || house.WardIndex + 1 != address.Ward || house.PlotIndex + 1 != address.Plot)
        {
            return false;
        }

        var territory = DistrictTerritory(address.District);
        if (territory != 0 && house.TerritoryTypeId != 0 && house.TerritoryTypeId != territory)
        {
            return false;
        }

        var worldId = WorldId(address.World);
        return worldId == 0 || house.WorldId == worldId;
    }

    public static uint WorldId(string worldName)
    {
        if (string.Equals(worldName, cachedWorldName, StringComparison.OrdinalIgnoreCase))
        {
            return cachedWorldId;
        }

        cachedWorldName = worldName;
        cachedWorldId = 0;
        foreach (var world in Services.Data.GetExcelSheet<World>())
        {
            if (string.Equals(world.Name.ExtractText(), worldName, StringComparison.OrdinalIgnoreCase))
            {
                cachedWorldId = world.RowId;
                break;
            }
        }

        return cachedWorldId;
    }

    private static ushort DistrictTerritory(string district)
    {
        var normalized = district.Replace(" ", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "mist" => MistTerritory,
            "lavenderbeds" or "lb" => LavenderBedsTerritory,
            "goblet" or "thegoblet" => GobletTerritory,
            "shirogane" => ShiroganeTerritory,
            "empyreum" => EmpyreumTerritory,
            _ => 0,
        };
    }
}
