using System.Text;
using CodexConfig.Core;

namespace CodexConfig.Application;

// Only ordinary values are exposed in the editable document. Existing routing,
// permissions and private fields remain in the original file and are preserved.
public sealed class ProfileDraftViewModel : WorkspaceObservableObject
{
    private string text = "", baselineText = "";
    public ProfileSnapshot? Snapshot { get; private set; }
    public long Revision { get; private set; }
    public string Text { get => text; set { if (Set(ref text, value)) { Revision++; Changed(nameof(Revision)); Changed(nameof(HasDraft)); } } }
    public bool HasDraft => Snapshot is not null && text != baselineText;
    public int PreservedFields { get; private set; }
    public void Load(ProfileSnapshot? snapshot)
    {
        Snapshot = snapshot is null ? null : snapshot with { Config = snapshot.Config?.ToArray() };
        var original = new TomlDocument(snapshot?.Config); var ordinary = new TomlDocument(null); PreservedFields = 0;
        foreach (var path in TomlDocument.ValuePaths(original.Model))
        {
            try { var value = ConfigSchema.Literal(original.Get(path)); ProfileStore.ValidateOrdinary(path, value); ordinary.SetRaw(path, value); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException) { PreservedFields++; }
        }
        text = baselineText = ordinary.Text; Revision++; Changed(nameof(Text)); Changed(nameof(Revision)); Changed(nameof(HasDraft));
    }
    public IReadOnlyDictionary<string, string?> Changes()
    {
        if (Snapshot is null) throw new InvalidOperationException("请先读取或新建 Profile。");
        var original = new TomlDocument(Encoding.UTF8.GetBytes(baselineText)); var after = new TomlDocument(Encoding.UTF8.GetBytes(text));
        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var path in TomlDocument.ValuePaths(after.Model).Concat(TomlDocument.ValuePaths(original.Model)).Distinct(StringComparer.Ordinal))
        {
            var before = original.Get(path); var next = after.Get(path);
            if (ConfigSchema.Literal(before) == ConfigSchema.Literal(next)) continue;
            var value = next is null ? null : ConfigSchema.Literal(next); ProfileStore.ValidateOrdinary(path, value); changes[path] = value;
        }
        return changes;
    }
}
