using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunPatEditorSmokeAsync(string output, List<string> checks)
    {
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var localToken = new string('A', 64);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# legacy connection\nmodel='legacy-model'\nmodel_provider='codex_config_mixed'\nopenai_base_url='http://127.0.0.1:48125/v1'\ncli_auth_credentials_store='file'\nweb_search='cached'\n[model_providers.codex_config_mixed]\nname='Codex Config connection'\nbase_url='http://127.0.0.1:48125/v1'\nwire_api='responses'\nrequires_openai_auth=true\nexperimental_bearer_token='" + localToken + "'\nsupports_websockets=false\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-legacy-id\",\"access_token\":\"fake-legacy-access\",\"refresh_token\":\"fake-legacy-refresh\"}}");
        var plain = JsonSerializer.SerializeToUtf8Bytes(new { BaseUrl = "https://offline.invalid/backend-api/codex", ApiKey = "fake-legacy-pat", Kind = 0, LocalToken = localToken, Port = 48125, UseSystemProxy = true });
        try { File.WriteAllBytes(Path.Combine(home, MixedConnection.FileName), ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("CodexConfig.mixed.v1"), DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        environment.Write(null);
        var before = store.Load(home);
        Assert(Reload(), "Legacy connection should remain readable");
        ShowPage("connection"); await Settle();
        Assert(BaseUrlBox.Text == "https://offline.invalid/backend-api/codex" && ApiKeyBox.Password == "fake-legacy-pat" && LegacyConnectionHint.IsVisible && !SaveButton.IsEnabled,
            "Legacy PAT must be available for explicit conversion");
        Assert(before.SameContents(store.Load(home)), "Opening an old connection must not change files");
        ConnectionModeBox.SelectedIndex = 0;
        Assert(!OfficialPanel.IsVisible && !GatewayPanel.IsVisible && SaveButton.IsEnabled, "PAT mode hides optional account and gateway controls");
        PreviewClicked(PreviewButton, new());
        Assert(!PreviewBox.Text.Contains("fake-legacy") && !PreviewBox.Text.Contains(localToken), "Conversion preview must mask old credentials");
        CloseOverlay();
        Assert(SaveCurrent(), "Legacy conversion must save without login or an online service: " + StatusText.Text);
        var saved = store.Load(home);
        Assert(saved.Bridge is null && loadedSettings.Mode == ConnectionMode.Direct && !Dirty && !LegacyConnectionHint.IsVisible, "Legacy conversion must remove gateway routing and reset the draft");
        using (var auth = JsonDocument.Parse(saved.Auth!))
            Assert(auth.RootElement.GetProperty("personal_access_token").GetString() == "fake-legacy-pat" && !auth.RootElement.TryGetProperty("tokens", out _), "PAT save replaces old login authentication");
        store.Restore(saved); Reload();
        Assert(before.SameContents(store.Load(home)) && ApiKeyBox.Password == "fake-legacy-pat" && !Dirty, "Encrypted backup restores the original old connection without creating a draft");
        ConnectionModeBox.SelectedIndex = 0;
        Assert(SaveCurrent(), "Restored connection can convert again");
        var baseline = store.Load(home);
        BaseUrlBox.Text = "not-an-address";
        Assert(!SaveCurrent() && SaveFeedback.IsVisible && SaveFeedback.Text.StartsWith("保存未完成：", StringComparison.Ordinal) && Dirty,
            "Failed save must explain the error next to the save button and preserve the draft");
        Assert(baseline.SameContents(store.Load(home)), "Invalid PAT edit must not change saved files");
        await Settle(); Capture(output, "pat-save-error.png");
        BaseUrlBox.Text = "https://offline.invalid/backend-api/codex"; ApiKeyBox.Password = "fake-updated-pat";
        Assert(SaveCurrent() && !SaveFeedback.IsVisible && store.ReadSettings(store.Load(home)).ApiKey == "fake-updated-pat", "Correcting the error must save and reload the new PAT");
        using (var handler = new DelayedModelCatalogHandler())
        using (var catalog = new ModelCatalogClient(handler))
        {
            var fetching = FetchModelsAsync(catalog);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                ApiKeyBox.Password = "fake-saved-while-loading";
                Assert(SaveButton.IsEnabled && SaveCurrent() && store.ReadSettings(store.Load(home)).ApiKey == "fake-saved-while-loading",
                    "A pending model request must not prevent local PAT saving");
                Assert(!fetching.IsCompleted, "The save must complete before the model service replies");
            }
            finally { handler.Release.TrySetResult(); await fetching; }
        }
        await Settle(); Capture(output, "pat-editor.png");
        checks.AddRange(["Old connection opens without writing and converts to PAT without login", "Conversion masks credentials and creates a reversible encrypted backup", "PAT saves require no gateway or reachable model service", "Invalid save keeps its draft and shows an error beside save", "Corrected PAT saves and reloads from disk", "Pending model requests do not block local saving"]);

        var fixture = new MainWindow(store, home, output, environment) { smokeLifecycle = true };
        fixture.Loaded -= fixture.WindowLoaded;
        fixture.Show(); fixture.InitializeTray();
        Assert(fixture.tray is not null, "System tray icon created");
        await fixture.tray!.RefreshGatewayActionsAsync();
        Assert(!fixture.tray.StopMobileItem.Enabled && !fixture.tray.StopModelItem.Enabled && fixture.tray.StopMobileItem.Text?.Contains("未运行") == true, "Tray marks both stopped gateways without starting them");
        fixture.ApiKeyBox.Password = "fake-tray-draft"; fixture.Close();
        Assert(!fixture.IsVisible && fixture.Dirty, "Close hides to tray and preserves PAT draft");
        fixture.RestoreWindow();
        Assert(fixture.IsVisible && fixture.ShowInTaskbar, "Tray restores the editor");
        fixture.MinimizeClicked(this, new());
        Assert(fixture.WindowState == WindowState.Minimized && fixture.ShowInTaskbar, "Minimize keeps taskbar entry");
        fixture.RestoreWindow(); fixture.Reload(); fixture.RequestExit();
        for (var attempt = 0; attempt < 100 && (!fixture.exitReady || fixture.tray is not null); attempt++) await Task.Delay(100);
        Assert(fixture.exitReady && fixture.tray is null, "Exit disposes the tray without a background gateway");
        checks.AddRange(["Close hides to tray and preserves PAT draft", "Tray restore and taskbar minimize", "Exit removes the tray without a background gateway", "Tray gateway status check leaves stopped services stopped"]);
    }

    private sealed class DelayedModelCatalogHandler : System.Net.Http.HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"data\":[{\"id\":\"fixture-model\"}]}") };
        }
    }
}
