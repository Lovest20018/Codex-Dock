using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace CodexConfig.Core;

// reqwest gives NO_PROXY precedence over WinINET's exceptions. Carry those
// exceptions forward when no explicit list is inherited.
internal static class CodexLaunchProxy
{
    private static readonly string[] Loopback = ["localhost", "127.0.0.1", "::1"];

    internal static void Apply(ProcessStartInfo start)
    {
        start.Environment.TryGetValue("NO_PROXY", out var upper);
        start.Environment.TryGetValue("no_proxy", out var lower);
        var entries = (upper + "," + lower).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var merged = Merge(entries, entries.Length == 0 ? ReadWindowsExceptions() : null);
        start.Environment["NO_PROXY"] = merged;
        if (!OperatingSystem.IsWindows()) start.Environment["no_proxy"] = merged;
    }

    internal static string Merge(IReadOnlyList<string> inherited, string? windowsExceptions)
    {
        var entries = inherited.Count > 0 ? inherited : (windowsExceptions ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeWindowsException).ToArray();
        return string.Join(",", entries.Concat(Loopback).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeWindowsException(string entry)
    {
        var parts = entry.Split('.');
        var wildcard = Array.IndexOf(parts, "*");
        if (wildcard is >= 1 and <= 3 && parts.Length <= 4 && parts.Skip(wildcard).All(part => part == "*"))
        {
            var octets = new byte[4];
            for (var i = 0; i < wildcard; i++)
                if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out octets[i])) return entry;
            return string.Join(".", octets) + "/" + (8 * wildcard).ToString(CultureInfo.InvariantCulture);
        }
        return entry.Replace("*.", "", StringComparison.Ordinal);
    }

    private static string? ReadWindowsExceptions()
    {
        if (!OperatingSystem.IsWindows()) return null;
        const string settingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        try
        {
            using var policy = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings");
            var root = policy?.GetValue("ProxySettingsPerUser") is 0 ? Registry.LocalMachine : Registry.CurrentUser;
            using var settings = root.OpenSubKey(settingsPath);
            return settings?.GetValue("ProxyEnable") is int enabled && enabled != 0 ? settings.GetValue("ProxyOverride") as string : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }
}
