using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CorditeWars.Core.Licensing;

/// <summary>
/// Computes a stable, privacy-respecting "machine ID" for use as the second
/// part of the (license, machine) activation key.
///
/// We deliberately avoid disk serial / motherboard UUID — both produce too
/// many false "new machine" events on common operations like swapping a
/// failed drive. Instead we blend several relatively-stable signals and
/// hash them with SHA-256, returning the first 16 bytes.
///
/// What goes in:
///   * MAC address of the lowest-numbered non-loopback interface (sorted).
///   * Operating system + machine architecture.
///   * CPU vendor string + processor count.
///
/// What goes OUT (over the wire):
///   * Only the 16-byte SHA-256 prefix, hex-encoded.
///
/// The raw signals never leave the device. This is also why the activation
/// API takes a hex string rather than the components.
/// </summary>
public static class MachineFingerprint
{
    public const int IdLengthBytes = 16;

    /// <summary>
    /// Compute the device fingerprint. Returns a 32-character lowercase hex
    /// string suitable for sending to the activation API.
    /// </summary>
    public static string Compute()
    {
        var sb = new StringBuilder();
        sb.Append("v1\n");
        sb.Append("os=").Append(RuntimeInformation.OSDescription).Append('\n');
        sb.Append("arch=").Append(RuntimeInformation.ProcessArchitecture).Append('\n');
        sb.Append("ncpu=").Append(System.Environment.ProcessorCount).Append('\n');
        sb.Append("mac=").Append(GetStableMac()).Append('\n');
        sb.Append("uid=").Append(GetUserHomeAnchor()).Append('\n');

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash, 0, IdLengthBytes).ToLowerInvariant();
    }

    /// <summary>Best-effort hostname for "manage your machines" UI display.</summary>
    public static string Hostname()
    {
        try { return System.Environment.MachineName; }
        catch { return "unknown"; }
    }

    private static string GetStableMac()
    {
        try
        {
            // Pick the alphabetically-lowest physical interface that's up
            // and has a non-zero MAC. Sorting makes the choice deterministic
            // across reboots even when interface enumeration order varies.
            IEnumerable<NetworkInterface> candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                            && n.OperationalStatus == OperationalStatus.Up)
                .OrderBy(n => n.Id, StringComparer.Ordinal);

            foreach (var iface in candidates)
            {
                byte[] addr = iface.GetPhysicalAddress().GetAddressBytes();
                if (addr.Length == 0 || addr.All(b => b == 0)) continue;
                return Convert.ToHexString(addr).ToLowerInvariant();
            }
        }
        catch
        {
            // Fall through.
        }
        return "no-mac";
    }

    /// <summary>
    /// A stable per-user-per-machine anchor. We hash the user-profile path
    /// to absorb "different user account on the same PC" into a different
    /// fingerprint (one license should be reasonable to use across the same
    /// person's accounts, but we don't want a roommate's account to silently
    /// share the slot either; the manage-machines UI handles the edge case).
    /// </summary>
    private static string GetUserHomeAnchor()
    {
        try
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                home = Path.GetTempPath();
            }
            return home;
        }
        catch
        {
            return "no-home";
        }
    }
}
