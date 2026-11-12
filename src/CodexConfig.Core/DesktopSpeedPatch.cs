using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConfig.Core;

public sealed record SpeedRuntimeLocation(string Path, int Line, int Column, string Chunk);
public sealed record DesktopSpeedPlan(IReadOnlyList<SpeedRuntimeLocation> Runtimes, IReadOnlyDictionary<string, int> Gates);
internal sealed record SpeedRewrite(string Source, IReadOnlyList<string> Gates);

// Independently implemented from the four-gate design documented by CodexPlusPlus 2.6.6.
// See docs/desktop-speed-compatibility.md for provenance and the supported renderer shape.
public static class DesktopSpeedPatch
{
    private const string Id = @"[A-Za-z_$][\w$]*";
    private const string Call = @"\(0," + Id + @"\." + Id + @"\)";
    private const string Types = "\"[A-Za-z][A-Za-z0-9]*\"";
    internal const string Lease = "globalThis.__codexDockSpeedUntil";
    internal static string Allow(string host, string auth) =>
        $"({Lease}>Date.now()&&{host}===\"local\"&&({auth}===\"apiKey\"||{auth}===\"apikey\"||{auth}==null))";
    private sealed record Gate(string Name, Regex Pattern, Func<Match, string> Replacement);
    private static Regex Pattern(string value) => new(value, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(3));
    private static readonly Gate[] Definitions =
    [
        new("menu", Pattern(
            $"(?<host>{Id})={Id}\\?\\.hostId\\?\\?{Id},(?<identity>{Id})={Call}\\(\\k<host>\\),{Id}=" +
            $"(?<gate>\\k<identity>\\?\\.authMethod===\"chatgpt\"\\|\\|\\k<identity>\\?\\.authMethod===\"personalAccessToken\"(?:\\|\\|\\k<identity>\\?\\.authMethod==={Types})*)," +
            $"(?<method>{Id})=\\k<identity>\\?\\.authMethod\\?\\?null;[\\s\\S]{{0,1000}}?\\{{authMethod:\\k<method>,hostId:\\k<host>\\}}[\\s\\S]{{0,1500}}?serviceTierAccess:"),
            m => "(" + m.Groups["gate"].Value + "||" + Allow(m.Groups["host"].Value, m.Groups["identity"].Value + "?.authMethod") + ")"),
        new("request", Pattern(
            $"async function\\((?<scope>{Id}),(?<host>{Id})\\)\\{{let (?<auth>{Id})=await {Call}\\(\\k<scope>,\\k<host>\\);if\\(" +
            $"(?<gate>\"chatgpt\"!==\\k<auth>&&\"personalAccessToken\"!==\\k<auth>(?:&&{Types}!==\\k<auth>)*)\\)return null;" +
            $"let (?<rules>{Id})=await {Call}\\(\\k<scope>,\\k<host>,\\{{priority:\"critical\"\\}}\\);return \\k<scope>\\.query\\.setData\\({Id}\\.{Id},\\{{authMethod:\\k<auth>,hostId:\\k<host>\\}},\\k<rules>\\),{Call}\\(\\k<rules>\\)\\}}"),
            m => "(" + m.Groups["gate"].Value + ")&&!" + Allow(m.Groups["host"].Value, m.Groups["auth"].Value)),
        new("queue-default", Pattern(
            $"if\\(\"local\"===(?<manager>{Id})\\.getHostId\\(\\)&&await (?<runtime>{Id})\\.isUsingCopilotInference\\(\\)\\)return!1;" +
            $"let (?<result>{Id})=await \\k<manager>\\.getAccount\\(\\{{priority:\"critical\"\\}}\\);return " +
            $"(?<gate>\\k<result>\\.account\\?\\.type===\"chatgpt\"(?:\\|\\|\\k<result>\\.account\\?\\.type==={Types})*)\\}}\\)\\}},async writeServiceTier\\("),
            m => "(" + m.Groups["gate"].Value + "||" + Allow(m.Groups["manager"].Value + ".getHostId()", m.Groups["result"].Value + ".account?.type") + ")"),
        new("queue-explicit", Pattern(
            $"function\\((?<manager>{Id}),\\{{settings:(?<settings>{Id}),storage:{Id},filesystem:{Id},runtime:(?<runtime>{Id})\\}},{Id},{Id},{Id}\\)\\{{" +
            $"return async\\({Id},(?<message>{Id})\\)=>\\{{let (?<options>{Id})=\\k<message>\\.submissionOptions,[^;]{{0,250}}?,(?<host>{Id})=\\k<options>\\?\\.executionHostId\\?\\?\\k<manager>\\.getHostId\\(\\);" +
            $"[\\s\\S]{{0,3200}}?if\\(null==\\k<options>\\.serviceTier\\)return null;let\\{{account:(?<account>{Id})\\}}=await \\k<manager>\\.getAccount\\(\\{{priority:\"critical\"\\}}\\);return " +
            $"(?<gate>\\k<account>\\?\\.type===\"chatgpt\"(?:\\|\\|\\k<account>\\?\\.type==={Types})*)\\?\\k<options>\\.serviceTier:null"),
            m => "(" + m.Groups["gate"].Value + "||(" + m.Groups["host"].Value + "===\"local\"&&" +
                Allow(m.Groups["manager"].Value + ".getHostId()", m.Groups["account"].Value + "?.type") +
                "&&typeof " + m.Groups["runtime"].Value + ".isUsingCopilotInference===\"function\"&&!(await " + m.Groups["runtime"].Value + ".isUsingCopilotInference())))"),
        // The speed hook otherwise filters the model catalog without including the selected model.
        // The composer already includes it. Preserve its real metadata instead of inventing tiers.
        new("selected-model", Pattern(
            $"(?<compiler>{Id})={Call}\\(60\\),\\{{modelSettings:(?<selectedSettings>{Id}),setModelAndReasoningEffort:{Id}\\}}={Id}," +
            $"[\\s\\S]{{0,1400}}?let (?<host>{Id})={Id}\\.hostId;[\\s\\S]{{0,800}}?" +
            $"\\k<compiler>\\[3\\]!==\\k<host>\\?\\((?<arguments>{Id})=\\{{hostId:\\k<host>\\}}," +
            $"\\k<compiler>\\[3\\]=\\k<host>,\\k<compiler>\\[4\\]=\\k<arguments>\\):\\k<arguments>=\\k<compiler>\\[4\\];" +
            $"let\\{{data:{Id},isLoading:{Id}\\}}=\\(0,{Id}\\.Ym\\)\\((?<gate>\\k<arguments>)\\),"),
            m => "(" + Lease + ">Date.now()&&" + m.Groups["host"].Value + "===\"local\"?{..." +
                m.Groups["arguments"].Value + ",additionalAvailableModels:[" + m.Groups["selectedSettings"].Value + ".model]}:" + m.Groups["arguments"].Value + ")")
    ];
    internal static int RequiredGateCount => Definitions.Length;

    internal static SpeedRewrite Rewrite(string source)
    {
        if (source.Length > 24 * 1024 * 1024 || source.Contains("__codexDockSpeedUntil", StringComparison.Ordinal))
            throw new InvalidDataException("速度兼容模块已修改或超出支持范围。");
        var edits = new List<(int Start, int Length, string Text)>(); var gates = new List<string>();
        foreach (var definition in Definitions)
        {
            var matches = definition.Pattern.Matches(source);
            if (matches.Count > 1) throw new InvalidDataException("速度兼容门控存在重复匹配：" + definition.Name);
            if (matches.Count == 0) continue;
            var match = matches[0]; var gate = match.Groups["gate"];
            var bindings = new[] { "host", "identity", "manager", "runtime", "account", "result", "auth", "scope", "selectedSettings", "compiler", "arguments" }
                .Select(name => match.Groups[name]).Where(group => group.Success).Select(group => group.Value).ToArray();
            if (bindings.Distinct().Count() != bindings.Length) throw new InvalidDataException("速度兼容门控的变量作用域无法核对。");
            edits.Add((gate.Index, gate.Length, definition.Replacement(match))); gates.Add(definition.Name);
        }
        var last = source.Length;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
        {
            if (edit.Start + edit.Length > last) throw new InvalidDataException("速度兼容门控重叠。");
            source = source[..edit.Start] + edit.Text + source[(edit.Start + edit.Length)..]; last = edit.Start;
        }
        return new(source, gates);
    }

    public static DesktopSpeedPlan Inspect(string desktopExecutable)
    {
        var archive = Path.Combine(Path.GetDirectoryName(desktopExecutable)!, "resources", "app.asar");
        using var stream = File.OpenRead(archive);
        Span<byte> prefix = stackalloc byte[16]; stream.ReadExactly(prefix);
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(prefix[4..]);
        var jsonSize = BinaryPrimitives.ReadUInt32LittleEndian(prefix[12..]);
        if (jsonSize > 32 * 1024 * 1024 || headerSize < jsonSize + 8 || 8L + headerSize > stream.Length)
            throw new InvalidDataException("无法读取 Codex 客户端资源目录。");
        var headerBytes = new byte[jsonSize]; stream.ReadExactly(headerBytes);
        using var header = JsonDocument.Parse(headerBytes);
        var assets = header.RootElement.GetProperty("files").GetProperty("webview").GetProperty("files").GetProperty("assets");
        var counts = Definitions.ToDictionary(gate => gate.Name, _ => 0);
        var runtimes = new List<SpeedRuntimeLocation>();
        foreach (var (path, entry) in Entries(assets, "assets/"))
        {
            if (!path.EndsWith(".js", StringComparison.Ordinal) || entry.TryGetProperty("unpacked", out _) || entry.TryGetProperty("link", out _)) continue;
            var size = entry.GetProperty("size").GetInt64();
            if (size is <= 0 or > 24 * 1024 * 1024) continue;
            var offset = 8L + headerSize + long.Parse(entry.GetProperty("offset").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            if (offset < 8L + headerSize || offset + size > stream.Length) throw new InvalidDataException("Codex 资源偏移无效。");
            stream.Position = offset; var bytes = new byte[size]; stream.ReadExactly(bytes); var source = Encoding.UTF8.GetString(bytes);
            if (source.Contains("serviceTierAccess:") || source.Contains("readServiceTier") || source.Contains("Failed to read service tier for request"))
                foreach (var gate in Rewrite(source).Gates) counts[gate]++;
            if (!path[7..].Contains('/') && source.Contains("as __webpack_require__")) runtimes.AddRange(RuntimeLocations(path, source));
        }
        if (counts.Any(pair => pair.Value == 0) || runtimes.Count == 0)
            throw new InvalidDataException("此 Codex 版本的速度门控、当前模型读取或模块加载器尚未匹配，未应用兼容处理。");
        return new(runtimes, counts);
    }

    internal static IReadOnlyList<SpeedRuntimeLocation> RuntimeLocations(string path, string source)
    {
        var matches = Pattern($"(?<chunk>{Id})=>\\{{(?<start>var {Id},{Id}=\\k<chunk>\\.__webpack_modules__,{Id}=\\k<chunk>\\.__rspack_esm_runtime;)for\\(").Matches(source);
        if (matches.Count != 1) throw new InvalidDataException("此 Codex 模块加载器尚未匹配。");
        var match = matches[0]; var index = match.Groups["start"].Index;
        var head = source[..index]; var line = head.Count(character => character == '\n'); var lastLine = head.LastIndexOf('\n');
        return [new(path, line, index - lastLine - 1, match.Groups["chunk"].Value)];
    }

    private static IEnumerable<(string Path, JsonElement Entry)> Entries(JsonElement node, string prefix)
    {
        foreach (var item in node.GetProperty("files").EnumerateObject())
            if (item.Value.TryGetProperty("files", out _))
            { foreach (var child in Entries(item.Value, prefix + item.Name + "/")) yield return child; }
            else yield return (prefix + item.Name, item.Value);
    }
}
