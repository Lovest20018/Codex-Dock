using System.Security.Cryptography;
using System.Text;
using CodexConfig.Core;

internal static class OfficialLoginTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
        string Home() { var home = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home); return home; }
        ConnectionSettings Api() => new("https://fixture.invalid/v1", "fake-official-model-key", "fixture-model") { Kind = ConnectionKind.ResponsesApi, PreservePreferenceValues = true };
        byte[] Login(string token) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(LegacyConnectionTests.Auth()).Replace("fake-access-token", token));
        Test("official login restoration reads only the selected home's encrypted backup without writes", () =>
        {
            var home = Home(); var environment = new FakeEnvironment(); var store = new ConfigStore(environment, _ => null); var login = Login("fake-stashed-access");
            File.WriteAllBytes(Path.Combine(home, "auth.json"), login);
            var backup = store.Apply(store.Plan(store.Load(home), Api())); var before = store.Load(home);
            var stashed = store.ReadOfficialLoginBackup(home);
            Assert(stashed is not null && stashed.SequenceEqual(login) && before.SameContents(store.Load(home)));
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(backup)).Contains("fake-stashed-access"));
            var other = Home(); Assert(store.ReadOfficialLoginBackup(other) is null && !Directory.Exists(Path.Combine(other, "codex-config-backups")));
            CryptographicOperations.ZeroMemory(stashed!);
        });
        Test("official login restoration stages the newest saved login and keeps current model settings", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login("fake-older-access")); store.Apply(store.Plan(store.Load(home), Api()));
            var newest = Login("fake-newest-access"); File.WriteAllBytes(Path.Combine(home, "auth.json"), newest);
            store.Apply(store.Plan(store.Load(home), Api() with { Model = "latest-model" }));
            var before = store.Load(home); var cached = store.ReadOfficialLoginBackup(home)!;
            Assert(cached.SequenceEqual(newest));
            var plan = store.Plan(before, store.ReadSettings(before) with { Mode = ConnectionMode.OfficialWithApi }, officialAuth: cached);
            Assert(before.SameContents(store.Load(home)) && plan.Auth!.SequenceEqual(newest) && new TomlDocument(plan.Config).String("model") == "latest-model");
            store.Apply(plan); var saved = store.Load(home);
            Assert(saved.Auth!.SequenceEqual(newest) && new TomlDocument(saved.Config).Get("chatgpt_base_url") is null && saved.AuthApiBaseUrl is null);
            store.Restore(saved); Assert(before.SameContents(store.Load(home))); CryptographicOperations.ZeroMemory(cached);
        });
        Test("current official login wins over an older staged backup including refreshed tokens", () =>
        {
            var home = Home(); var current = Login("fake-live-refreshed-access"); File.WriteAllBytes(Path.Combine(home, "auth.json"), current);
            var store = new ConfigStore(new FakeEnvironment(), _ => null); var before = store.Load(home);
            var plan = store.Plan(before, Api() with { Mode = ConnectionMode.OfficialWithApi }, officialAuth: Login("fake-old-access"));
            Assert(plan.Auth!.SequenceEqual(current) && before.SameContents(store.Load(home)));
        });
        Test("a sign out or API key file does not automatically resurrect a backed up official login", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login("fake-signed-out-access")); store.Apply(store.Plan(store.Load(home), Api()));
            File.WriteAllText(Path.Combine(home, "auth.json"), "{}"); var before = store.Load(home);
            Assert(store.ReadSettings(before).ApiKey.Length == 0 && !OfficialCredentials.HasLogin(before.Auth));
            try { store.Plan(before, Api() with { Mode = ConnectionMode.OfficialWithApi }); throw new Exception("Missing live login must require an explicit backup selection."); }
            catch (InvalidOperationException) { }
            Assert(before.SameContents(store.Load(home)) && !OfficialCredentials.HasLogin(store.Load(home).Auth));
        });
        Test("damaged encrypted login backup is reported without changing authentication or earlier backups", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login("fake-backup-access")); store.Apply(store.Plan(store.Load(home), Api()));
            var before = store.Load(home); var damaged = Path.Combine(home, "codex-config-backups", "zz-damaged.backup"); File.WriteAllBytes(damaged, [1, 2, 3]);
            try { store.ReadOfficialLoginBackup(home); throw new Exception("Damaged backup must not be silently skipped."); }
            catch (CryptographicException) { }
            Assert(before.SameContents(store.Load(home)) && File.ReadAllBytes(damaged).SequenceEqual(new byte[] { 1, 2, 3 }));
        });
        Test("external authentication changes still block a reviewed official backup restoration", () =>
        {
            var home = Home(); var store = new ConfigStore(new FakeEnvironment(), _ => null);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login("fake-backup-access")); store.Apply(store.Plan(store.Load(home), Api()));
            var cached = store.ReadOfficialLoginBackup(home)!; var before = store.Load(home);
            var plan = store.Plan(before, Api() with { Mode = ConnectionMode.OfficialWithApi }, officialAuth: cached);
            File.WriteAllBytes(Path.Combine(home, "auth.json"), Login("fake-concurrent-access")); var external = store.Load(home);
            try { store.Apply(plan); throw new Exception("Restoration must reject changed authentication."); }
            catch (InvalidOperationException) { }
            Assert(external.SameContents(store.Load(home))); CryptographicOperations.ZeroMemory(cached);
        });
    }

    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Official login backup assertion failed."); }
}
