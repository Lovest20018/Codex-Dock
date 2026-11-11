using System.Text.Json;
using CodexConfig.Core;

internal static class TemplateProvenanceTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        string DirectoryForTest()
        {
            var path = Path.Combine(root, "template-provenance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); return path;
        }
        static string Seed(string directory, ConfigTemplate template)
        {
            var path = Path.Combine(directory, "templates.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, Templates = new[] { template }, HiddenBuiltIns = Array.Empty<string>() }));
            return path;
        }
        Test("template provenance preserves older built-in source and local content across load and edits", () =>
        {
            var directory = DirectoryForTest();
            var original = BuiltInTemplates.KrillCommunity with
            {
                Name = "My existing Krill setup", SourceName = "Krill / Superba alpha.13",
                SourceUrl = "https://github.com/krill-lab/superba/blob/v0.1.1-alpha.13/crates/superba-core/src/codex.rs",
                Settings = [new("analytics.enabled", "false"), new("agents.max_depth", "1")]
            };
            var path = Seed(directory, original); var bytes = File.ReadAllBytes(path);
            var repository = new TemplateRepository(directory);
            var loaded = repository.Load().Single(template => template.Id == original.Id);
            Assert(loaded.SourceName == original.SourceName && loaded.SourceUrl == original.SourceUrl);
            Assert(loaded.Settings.SequenceEqual(original.Settings) && File.ReadAllBytes(path).SequenceEqual(bytes));
            repository.Save(loaded with { Name = "Edited local name" });
            var edited = new TemplateRepository(directory).Load().Single(template => template.Id == original.Id);
            Assert(edited.Name == "Edited local name" && edited.SourceName == original.SourceName && edited.SourceUrl == original.SourceUrl);
            Assert(edited.Settings.SequenceEqual(original.Settings));
        });
        Test("template provenance does not invent an upstream source for an unrecorded local recipe", () =>
        {
            var directory = DirectoryForTest();
            var original = BuiltInTemplates.KrillCommunity with { SourceName = "", SourceUrl = "", Settings = [new("agents.max_depth", "1")] };
            var path = Seed(directory, original); var bytes = File.ReadAllBytes(path);
            var loaded = new TemplateRepository(directory).Load().Single(template => template.Id == original.Id);
            Assert(loaded.SourceUrl.Length == 0 && loaded.SourceName == "本地方案（来源未记录）");
            Assert(loaded.Settings.SequenceEqual(original.Settings) && File.ReadAllBytes(path).SequenceEqual(bytes));
        });
        Test("template provenance current built-ins remain available after explicitly restoring the library", () =>
        {
            var repository = new TemplateRepository(DirectoryForTest());
            var bundled = BuiltInTemplates.KrillCommunity;
            var fresh = repository.Load().Single(template => template.Id == bundled.Id);
            Assert(fresh.SourceName == bundled.SourceName && fresh.SourceUrl == bundled.SourceUrl);
            repository.Save(bundled with { SourceName = "Old source", SourceUrl = "https://example.invalid/old", Settings = [new("agents.max_depth", "1")] });
            var custom = repository.Save(TemplateRepository.New("Personal recipe", [new("agents.max_depth", "2")]), create: true);
            repository.RestoreBuiltIns();
            var restored = repository.Load().Single(template => template.Id == bundled.Id);
            Assert(restored.SourceName == bundled.SourceName && restored.SourceUrl == bundled.SourceUrl && restored.Settings.SequenceEqual(bundled.Settings));
            Assert(repository.Load().Any(template => template.Id == custom.Id));
        });
    }

    private static void Assert(bool value)
    {
        if (!value) throw new InvalidOperationException("Template provenance regression");
    }
}
