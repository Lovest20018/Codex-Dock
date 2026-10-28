namespace CodexConfig.Core;

public sealed record KrillLine(string Id, string Name, string Origin, IReadOnlyList<string> LegacyOrigins);

public static class KrillLines
{
    // Project-owned presets. They do not depend on an upstream checkout.
    public static KrillLine Cdn { get; } = new("cdn", "Krill · 三网优化 CDN", "https://api.kbsfdxc.com", ["https://api.cdn-krill-ai.com"]);
    public static IReadOnlyList<KrillLine> All { get; } =
    [
        Cdn,
        new("overseas", "Krill · 海外线路", "https://api.kbsfdxc.net", ["https://api.krill-code.net"]),
        new("cn-fast", "Krill · 国内极速", "https://api-slb.kbsfdxc.net", ["https://api-slb.krill-code.net"])
    ];

    public static KrillLine? Find(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var url) || url.UserInfo.Length > 0 ||
            url.Query.Length > 0 || url.Fragment.Length > 0 || url.Scheme != "https") return null;
        var origin = url.GetLeftPart(UriPartial.Authority);
        return All.FirstOrDefault(line => string.Equals(line.Origin, origin, StringComparison.OrdinalIgnoreCase) ||
            line.LegacyOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));
    }
}
