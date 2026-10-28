using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace CodexConfig.Core;

// Adapted from Superba's owned-key editing design: parse spans and patch only
// managed values. Keep unrelated source text, including comments, byte-for-byte.
// See third_party/Superba-LICENSE.txt and THIRD-PARTY-NOTICES.md.
public sealed class TomlDocument
{
    private string source;
    private readonly bool bom;
    private readonly string newline;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public TomlDocument(byte[]? bytes)
    {
        bytes ??= [];
        bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        try { source = StrictUtf8.GetString(bom ? bytes.AsSpan(3) : bytes); }
        catch (DecoderFallbackException) { throw new InvalidDataException("config.toml 必须使用 UTF-8 编码。"); }
        newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        Validate();
    }

    public TomlTable Model => ParseModel(source);
    public string Text => source;
    public byte[] Bytes => bom ? [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(source)] : Encoding.UTF8.GetBytes(source);
    public string? String(string key) => Model.TryGetValue(key, out var value) ? value as string : null;
    public object? Get(string path)
    {
        object? current = Model;
        foreach (var part in ParsePath(path))
        {
            if (current is not TomlTable table || !table.TryGetValue(part, out current)) return null;
        }
        return current;
    }

    public void SetRaw(string path, string repr)
    {
        var model = ParseModel("value = " + repr);
        if (model.Count != 1 || !model.ContainsKey("value")) throw new ArgumentException("请输入单个 TOML 值。");
        SetValue(ParsePath(path), repr.Trim());
    }

    public bool Remove(string path)
    {
        var syntax = Toml.Parse(source);
        var parts = ParsePath(path);
        var entry = Assignments(syntax).FirstOrDefault(x => x.Path.SequenceEqual(parts));
        if (entry is null) return false;
        var inline = Assignments(syntax).Select(x => x.Syntax.Value).OfType<InlineTableSyntax>()
            .FirstOrDefault(x => x.Items.Any(i => ReferenceEquals(i.KeyValue, entry.Syntax)));
        if (inline is not null)
        {
            var items = inline.Items.ToList();
            var index = items.FindIndex(x => ReferenceEquals(x.KeyValue, entry.Syntax));
            var item = items[index];
            var start = item.KeyValue!.Key!.Span.Start.Offset;
            var end = item.KeyValue.Value!.Span.End.Offset + 1;
            if (item.Comma is not null) end = item.Comma.Span.End.Offset + 1;
            else if (index > 0) start = items[index - 1].Comma!.Span.Start.Offset;
            Replace(start, end - start, "");
        }
        else
        {
            var start = entry.Syntax.Key!.Span.Start.Offset;
            var end = entry.Syntax.EndOfLineToken is { } eol && eol.Span.Length > 0 ? Math.Min(eol.Span.End.Offset + 1, source.Length) : entry.Syntax.Span.End.Offset + 1;
            Replace(start, end - start, "");
        }
        Validate();
        return true;
    }

    public static TomlTable ParseModel(string text)
    {
        var syntax = Toml.Parse(text);
        if (syntax.HasErrors)
            throw new InvalidDataException($"config.toml 语法错误（第 {syntax.Diagnostics.First().Span.Start.Line + 1} 行），请先修复源文件。");
        try { return Toml.ToModel(text); }
        catch { throw new InvalidDataException("config.toml 包含重复键或无效的 TOML 结构，请先修复源文件。"); }
    }

    private void Validate() => _ = ParseModel(source);

    private static string Decode(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bare => bare.Key!.Text!,
        StringValueSyntax quoted => quoted.Value!,
        _ => throw new InvalidDataException("无法读取 TOML 键名。")
    };

    private static string[] Parts(KeySyntax key) => [Decode(key.Key!), .. key.DotKeys.Select(x => Decode(x.Key!))];

    public static string[] ParsePath(string path)
    {
        path = path.Trim();
        var syntax = Toml.Parse(path + " = 0");
        var assignments = syntax.KeyValues.ToArray();
        if (syntax.HasErrors || syntax.Tables.Any() || assignments.Length != 1 ||
            assignments[0].Key is not { } key || assignments[0].Value?.ToString() != "0")
            throw new ArgumentException("请输入有效的 TOML 点分键，包含点或路径的名称请加引号。");
        if (key.Span.Start.Offset != 0 || key.Span.End.Offset + 1 != path.Length)
            throw new ArgumentException("配置路径不能包含赋值或注释。");
        var parts = Parts(key);
        // Re-rendering the key must describe exactly one path, never injected assignments/comments.
        if (path.Contains('\n') || path.Contains('\r') || path.Contains('<') || path.Contains('>'))
            throw new ArgumentException("请填写实际的配置路径，不要保留 <name> 占位符。");
        return parts;
    }

    public static string FormatPath(IEnumerable<string> parts) => string.Join('.', parts.Select(Key));
    private static string Key(string part) => Regex.IsMatch(part, "^[A-Za-z0-9_-]+$") ? part : Quote(part);

    public static object? ValueAt(TomlTable model, string path)
    {
        object? value = model;
        foreach (var part in ParsePath(path))
            if (value is not TomlTable table || !table.TryGetValue(part, out value)) return null;
        return value;
    }

    public static IEnumerable<string> ValuePaths(TomlTable model, string[]? prefix = null)
    {
        foreach (var (key, value) in model)
        {
            string[] path = [.. prefix ?? [], key];
            if (value is TomlTable table)
                foreach (var nested in ValuePaths(table, path)) yield return nested;
            else yield return FormatPath(path);
        }
    }
    private sealed record Assignment(string[] Path, KeyValueSyntax Syntax);

    private static IEnumerable<Assignment> Assignments(DocumentSyntax doc)
    {
        foreach (var entry in doc.KeyValues)
            foreach (var item in Walk(entry, [])) yield return item;
        foreach (var table in doc.Tables)
        {
            if (table is not TableSyntax standard) continue;
            var prefix = Parts(standard.Name!);
            foreach (var entry in standard.Items)
                foreach (var item in Walk(entry, prefix)) yield return item;
        }
    }

    private static IEnumerable<Assignment> Walk(KeyValueSyntax entry, string[] prefix)
    {
        string[] path = [.. prefix, .. Parts(entry.Key!)];
        yield return new(path, entry);
        if (entry.Value is InlineTableSyntax inline)
            foreach (var item in inline.Items)
                foreach (var child in Walk(item.KeyValue!, path)) yield return child;
    }

    private void Replace(int start, int length, string text) => source = source.Remove(start, length).Insert(start, text);
    public static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var rune in value.EnumerateRunes())
            builder.Append(rune.Value switch
            {
                0x22 => "\\\"", 0x5c => "\\\\", 0x0a => "\\n", 0x0d => "\\r", 0x09 => "\\t", 0x08 => "\\b", 0x0c => "\\f",
                < 0x20 or 0x7f => "\\u" + rune.Value.ToString("X4"), _ => rune.ToString()
            });
        return builder.Append('"').ToString();
    }

    public void Set(string key, string value) => SetValue(key.Split('.'), Quote(value));
    public void Set(string key, bool value) => SetValue(key.Split('.'), value ? "true" : "false");

    private void SetValue(string[] path, string value)
    {
        var syntax = Toml.Parse(source);
        var entries = Assignments(syntax).ToList();
        var existing = entries.FirstOrDefault(x => x.Path.SequenceEqual(path));
        if (existing is not null)
        {
            var span = existing.Syntax.Value!.Span;
            Replace(span.Start.Offset, span.Length, value);
            Validate();
            return;
        }
        if (path.Length == 1)
        {
            InsertRoot(syntax, $"{Key(path[0])} = {value}");
            Validate();
            return;
        }
        var parentPath = path[..^1];
        var parent = entries.FirstOrDefault(x => x.Path.SequenceEqual(parentPath));
        if (parent is not null)
        {
            if (parent.Syntax.Value is not InlineTableSyntax inline)
                throw new InvalidDataException($"配置中的 {string.Join('.', parentPath)} 必须是 TOML 表。");
            var prefix = !inline.Items.Any() ? " " : ", ";
            Replace(inline.CloseBrace!.Span.Start.Offset, 0, $"{prefix}{Key(path[^1])} = {value} ");
        }
        else
        {
            var table = syntax.Tables.OfType<TableSyntax>().FirstOrDefault(x => Parts(x.Name!).SequenceEqual(parentPath));
            if (table is not null)
            {
                var end = table.EndOfLineToken?.Span.End.Offset + 1 ?? table.CloseBracket!.Span.End.Offset + 1;
                end = Math.Min(end, source.Length);
                var separator = end == 0 || source[end - 1] == '\n' ? "" : newline;
                Replace(end, 0, separator + $"{Key(path[^1])} = {value}" + newline);
            }
            else
            {
                // Dotted root assignments also handle an already implicit table.
                InsertRoot(syntax, $"{FormatPath(path)} = {value}");
            }
        }
        Validate();
    }

    private void InsertRoot(DocumentSyntax syntax, string entry)
    {
        var index = !syntax.Tables.Any() ? source.Length : syntax.Tables.First().Span.Start.Offset;
        var separator = index == 0 || source[index - 1] == '\n' ? "" : newline;
        Replace(index, 0, separator + entry + newline);
    }

    public bool RemoveRoot(string key)
    {
        var syntax = Toml.Parse(source);
        var item = syntax.KeyValues.FirstOrDefault(x => Parts(x.Key!).SequenceEqual([key]));
        if (item is null) return false;
        var start = item.Key!.Span.Start.Offset;
        var end = item.EndOfLineToken is { } eol && eol.Span.Length > 0
            ? Math.Min(eol.Span.End.Offset + 1, source.Length) : item.Span.End.Offset + 1;
        Replace(start, end - start, "");
        Validate();
        return true;
    }

    public string Masked()
    {
        var syntax = Toml.Parse(source);
        var secretEntries = Assignments(syntax).Where(x => SensitiveParts(x.Path)).ToList();
        var spans = secretEntries.Select(x => x.Syntax.Value!.Span).OrderBy(x => x.Start.Offset).ToList();
        var nonOverlapping = new List<SourceSpan>();
        foreach (var span in spans)
            if (nonOverlapping.Count == 0 || span.Start.Offset > nonOverlapping[^1].End.Offset) nonOverlapping.Add(span);
        var masked = source;
        foreach (var span in nonOverlapping.AsEnumerable().Reverse())
            masked = masked.Remove(span.Start.Offset, span.Length).Insert(span.Start.Offset, "\"••••••••\"");
        return masked;
    }

    public static bool IsSensitivePath(string path)
    {
        try { return SensitiveParts(ParsePath(path)); }
        catch (ArgumentException) { return SensitiveParts(path.Split('.')); }
    }
    private static bool SensitiveParts(IEnumerable<string> parts) => parts.Any(part =>
        part.Equals("token", StringComparison.OrdinalIgnoreCase) || part.Equals("tokens", StringComparison.OrdinalIgnoreCase) || part.EndsWith("_token", StringComparison.OrdinalIgnoreCase) ||
        part.Contains("secret", StringComparison.OrdinalIgnoreCase) || part.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        part.Contains("api_key", StringComparison.OrdinalIgnoreCase) || part.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        part.Contains("http_headers", StringComparison.OrdinalIgnoreCase) || part.Equals("env", StringComparison.OrdinalIgnoreCase));
}
