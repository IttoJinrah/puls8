using System.Security.Cryptography;
using System.Text;

namespace Puls8.Staff;

// DPAPI ties the stored key to this Windows user, so a copied config file is useless on another account or PC.
public static class StaffKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Puls8.StaffKey.v1");

    public static string Protect(string key)
    {
        var sealedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(sealedBytes);
    }

    public static string Unprotect(string stored)
    {
        if (stored.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var plainBytes = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }
}
