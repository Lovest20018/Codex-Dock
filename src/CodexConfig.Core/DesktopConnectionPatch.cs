using System.Text.RegularExpressions;

namespace CodexConfig.Core;

internal static class DesktopConnectionPatch
{
    internal const string Marker = "__codexDockConnection";
    private const string Id = @"[A-Za-z_$][\w$]*";
    private static readonly Regex Method = new(
        $"async sendRequest\\((?<method>{Id}),(?<parameters>{Id}),(?<options>{Id})\\)\\{{this\\.assertActive\\(\\);let {Id}=await (?<call>this\\.requestClient\\.sendRequest\\(\\k<method>,\\k<parameters>,\\k<options>\\))\\.catch\\(",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(3));
    internal static int Count(string source) => Method.Matches(source).Count;
    internal static string Rewrite(string source)
    {
        if (source.Contains(Marker, StringComparison.Ordinal)) return source;
        var matches = Method.Matches(source);
        if (matches.Count != 1) throw new InvalidDataException("此 Codex 聊天请求入口尚未匹配，未启用接续。");
        var match = matches[0]; var call = match.Groups["call"];
        var replacement = "(globalThis." + Marker + "?.enabled===true?globalThis." + Marker + ".dispatch(this," +
            match.Groups["method"].Value + "," + match.Groups["parameters"].Value + "," + match.Groups["options"].Value +
            ",(__dockMethod,__dockParameters,__dockOptions)=>this.requestClient.sendRequest(__dockMethod,__dockParameters,__dockOptions)):" + call.Value + ")";
        return source[..call.Index] + replacement + source[(call.Index + call.Length)..];
    }
    internal static string Controller(string binding, bool resumeOnly = false)
    {
        using var input = typeof(DesktopConnectionPatch).Assembly.GetManifestResourceStream("CodexConfig.Core.desktop-connections.js")!;
        using var reader = new StreamReader(input);
        return "(" + reader.ReadToEnd() + ")(" + System.Text.Json.JsonSerializer.Serialize(binding) + "," + (resumeOnly ? "true" : "false") + ")";
    }
}
