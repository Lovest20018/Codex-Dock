using Tomlyn.Model;

namespace CodexConfig.Core;

internal static class TomlTableExtensions
{
    internal static object? GetValueOrDefault(this TomlTable table, string key) => table.TryGetValue(key, out var value) ? value : null;
}
