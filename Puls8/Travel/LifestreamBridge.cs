using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace Puls8.Travel;

public sealed class LifestreamBridge
{
    private const string InternalName = "Lifestream";

    private readonly ICallGateSubscriber<string, string, string, string, bool, bool, AddressBookEntryTuple> buildAddress;
    private readonly ICallGateSubscriber<AddressBookEntryTuple, object> goToHousingAddress;
    private readonly ICallGateSubscriber<AddressBookEntryTuple, bool> isHere;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<object> abort;

    public LifestreamBridge()
    {
        var pluginInterface = Services.PluginInterface;
        buildAddress = pluginInterface.GetIpcSubscriber<string, string, string, string, bool, bool, AddressBookEntryTuple>("Lifestream.BuildAddressBookEntry");
        goToHousingAddress = pluginInterface.GetIpcSubscriber<AddressBookEntryTuple, object>("Lifestream.GoToHousingAddress");
        isHere = pluginInterface.GetIpcSubscriber<AddressBookEntryTuple, bool>("Lifestream.IsHere");
        isBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        abort = pluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
    }

    public static bool IsInstalled()
    {
        foreach (var plugin in Services.PluginInterface.InstalledPlugins)
        {
            if (string.Equals(plugin.InternalName, InternalName, StringComparison.Ordinal) && plugin.IsLoaded)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryBuild(string world, string district, int ward, int plot, out AddressBookEntryTuple entry)
    {
        try
        {
            entry = buildAddress.InvokeFunc(world, district, ward.ToString(), plot.ToString(), false, false);
            return entry.World != 0 && entry.Ward > 0 && entry.Plot > 0;
        }
        catch (IpcError exception)
        {
            Services.Log.Warning($"Lifestream could not build the venue address: {exception.Message}");
            entry = default;
            return false;
        }
    }

    public bool TryGo(AddressBookEntryTuple entry)
    {
        try
        {
            goToHousingAddress.InvokeAction(entry);
            return true;
        }
        catch (IpcError exception)
        {
            Services.Log.Warning($"Lifestream refused the trip: {exception.Message}");
            return false;
        }
    }

    public bool IsHere(AddressBookEntryTuple entry)
    {
        try
        {
            return isHere.InvokeFunc(entry);
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public bool IsBusy()
    {
        try
        {
            return isBusy.InvokeFunc();
        }
        catch (IpcError)
        {
            return false;
        }
    }

    public void Abort()
    {
        try
        {
            abort.InvokeAction();
        }
        catch (IpcError)
        {
        }
    }
}
