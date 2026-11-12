using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CodexConfig.Core;

internal static class DesktopSpeedTests
{
    internal const string Factories = """
export const __webpack_modules__={
Menu(e,t,n){let s={tn:()=>({authMethod:globalThis.auth})};globalThis.menu=function(e){let d="local",p=e?.hostId??d,f=(0,s.tn)(p),m=f?.authMethod==="chatgpt"||f?.authMethod==="personalAccessToken"||f?.authMethod==="amazonBedrock",g=f?.authMethod??null;let q={authMethod:g,hostId:p};return{serviceTierAccess:m?"fast":null}}},
Request(e,t,n){const diagnostic="Failed to read service tier for request";let l={s:async()=>globalThis.auth},o={Wr:async()=>({allowed:["fast"]}),b9:1},r={W6:x=>x};globalThis.request=async function(e,t){let n=await (0,l.s)(e,t);if("chatgpt"!==n&&"personalAccessToken"!==n&&"amazonBedrock"!==n)return null;let i=await (0,o.Wr)(e,t,{priority:"critical"});return e.query.setData(o.b9,{authMethod:n,hostId:t},i),(0,r.W6)(i)}},
Default(e,t,n){globalThis.runDefault=async cb=>cb();globalThis.makeDefault=function(e,{runtime:t},n,r){let i=null;return{async readServiceTier(r){return globalThis.runDefault(async()=>{if("local"===e.getHostId()&&await t.isUsingCopilotInference())return!1;let n=await e.getAccount({priority:"critical"});return n.account?.type==="chatgpt"||n.account?.type==="amazonBedrock"})},async writeServiceTier(t,a){return null}}}},
Explicit(e,t,n){const diagnostic="readServiceTier";globalThis.makeExplicit=function(e,{settings:t,storage:n,filesystem:r,runtime:i},a,s,o){return async(l,u)=>{let d=u.submissionOptions,c=u.context.workPermissions,h=null!=c,p=d?.executionHostId??e.getHostId();let[E,I]=await Promise.all([(async()=>{if(null==d)return a(null);if(null==d.serviceTier)return null;let{account:t}=await e.getAccount({priority:"critical"});return t?.type==="chatgpt"||t?.type==="amazonBedrock"?d.serviceTier:null})(),d?.usePermissionSelection??t.readPermissionRefreshEnabled()]);return E}}},
Catalog(e,t,n){const diagnostic="serviceTierAccess:";const a={c:n=>[]},C={Ym:options=>({data:{models:globalThis.speedCatalog.filter(m=>m.model==="visible-model"||options.additionalAvailableModels?.includes(m.model))},isLoading:false})};globalThis.readTierOptions=function(e,t){let E,ef=(0,a.c)(60),{modelSettings:em,setModelAndReasoningEffort:eg}=t,x=e;let eb=x.hostId;ef[3]!==eb?(E={hostId:eb},ef[3]=eb,ef[4]=E):E=ef[4];let{data:eS,isLoading:ek}=(0,C.Ym)(E),eA=null;return eS.models.find(m=>m.model===em.model)?.serviceTiers??[]}}
};
""";
    private const string Runtime = """
const modules={};function w(k){let e={exports:{}};modules[k](e,e.exports,w);return e.exports}w.m=modules;w.o=(o,k)=>Object.prototype.hasOwnProperty.call(o,k);w.C=e=>{var k,m=e.__webpack_modules__,r=e.__rspack_esm_runtime;for(k in m)w.o(m,k)&&(w.m[k]=m[k]);r&&r(w);};export{w as __webpack_require__};
""";

    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
        Test("speed patch recognizes four auth gates and selected model lookup without changing native requirements", () =>
        {
            var result = DesktopSpeedPatch.Rewrite(Factories);
            Assert(result.Gates.Count == 5 && result.Source.Contains("allowed:[\"fast\"]") && result.Source.Contains("serviceTier:null"));
            Assert(result.Gates.Contains("selected-model") && result.Source.Contains("additionalAvailableModels:[em.model]"));
            Assert(result.Source.Contains("isUsingCopilotInference()") && !result.Source.Contains("serviceTiers="));
            Throws(() => DesktopSpeedPatch.Rewrite(Factories + Factories));
            Throws(() => DesktopSpeedPatch.Rewrite(result.Source));
            Assert(DesktopSpeedPatch.Rewrite(Factories.Replace("personalAccessToken", "futureIdentity")).Gates.Count < 5);
        });
        Test("speed patch finds a registration breakpoint before native factories run", () =>
        {
            var location = DesktopSpeedPatch.RuntimeLocations("assets/runtime.js", Runtime).Single();
            Assert(Runtime[location.Column..].StartsWith("var k,m=e.__webpack_modules__") && location.Chunk == "e");
            Throws(() => DesktopSpeedPatch.RuntimeLocations("runtime.js", "unsupported-runtime"));
        });
        Test("speed preferences default off and are isolated from connection drafts and other homes", () =>
        {
            var data = Path.Combine(root, Guid.NewGuid().ToString("N")); var a = new DesktopSpeedSettings(Path.Combine(root, "a"), data); var b = new DesktopSpeedSettings(Path.Combine(root, "b"), data);
            Assert(!a.Load() && !b.Load()); a.Save(true); Assert(a.Load() && !b.Load()); a.Save(false); Assert(!a.Load());
            File.WriteAllText(a.SettingsPath, "{\"enabled\":\"true\"}"); Throws(() => a.Load());
        });
        Test("speed compatibility accepts saved API key direct only and refuses official, PAT, mixed and profile routes", () =>
        {
            var home = Path.Combine(root, "speed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            var store = new ConfigStore(new FakeEnvironment(), _ => null);
            store.Apply(store.Plan(store.Load(home), new("https://relay.invalid", "fake-speed-key", "speed-model")));
            var saved = store.Load(home); Assert(DesktopSpeedSettings.Eligible(store, saved));
            Assert(!DesktopSpeedSettings.Eligible(store, saved with { Auth = LegacyConnectionTests.Auth() }));
            Assert(!DesktopSpeedSettings.Eligible(store, saved with { AuthApiBaseUrl = "https://other.invalid" }));
            Assert(!DesktopSpeedSettings.Eligible(store, saved with { Bridge = [1] }));
            File.AppendAllText(Path.Combine(home, "config.toml"), "\n[profiles.other]\nmodel='other'\n");
            File.WriteAllText(Path.Combine(home, "config.toml"), "profile='other'\n" + File.ReadAllText(Path.Combine(home, "config.toml")));
            Assert(!DesktopSpeedSettings.Eligible(store, store.Load(home)));
        });
        if (Environment.GetEnvironmentVariable("CODEX_CONFIG_SPEED_BROWSER") is { Length: > 0 } browser)
            tests.Add(("speed compatibility preserves CSP and sends API tiers through all four native fixture gates", () => BrowserAsync(root, browser)));
    }

    internal static async Task BrowserAsync(string root, string browser, string? nativeAssets = null)
    {
        var fixture = Path.Combine(root, "speed-browser-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(fixture, "assets"));
        File.WriteAllText(Path.Combine(fixture, "index.html"), "<meta http-equiv=\"Content-Security-Policy\" content=\"script-src 'self'\"><script type=\"module\" src=\"./assets/main.js\"></script>");
        File.WriteAllText(Path.Combine(fixture, "assets", "runtime.js"), Runtime);
        File.WriteAllText(Path.Combine(fixture, "assets", "chunk.js"), Factories);
        File.WriteAllText(Path.Combine(fixture, "assets", "main.js"), "import{__webpack_require__ as w}from'./runtime.js';import*as chunk from'./chunk.js';w.C(chunk);for(const k of Object.keys(chunk.__webpack_modules__))w(k);globalThis.auth='apiKey';globalThis.ready=true;");
        if (nativeAssets is not null)
        {
            var modules = Directory.GetFiles(nativeAssets, "*.js"); Assert(modules.Length == 3, "Expected three verified native gate chunks");
            var imports = new List<string>(); var calls = new List<string>();
            for (var i = 0; i < modules.Length; i++)
            {
                var name = "native" + i; File.Copy(modules[i], Path.Combine(fixture, "assets", name + ".js"));
                imports.Add("import*as " + name + " from'./" + name + ".js';"); calls.Add("w.C(" + name + ");");
            }
            File.AppendAllText(Path.Combine(fixture, "assets", "main.js"), string.Join("", imports) + string.Join("", calls) +
                "globalThis.nativePatched=[" + string.Join(",", Enumerable.Range(0, modules.Length).Select(i => "Object.values(native" + i + ".__webpack_modules__).filter(f=>Function.prototype.toString.call(f).includes('__codexDockSpeedUntil')).length")) + "];globalThis.nativeReady=true;");
        }
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        var start = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var value in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-background-networking", "--allow-file-access-from-files", "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=" + port, "--user-data-dir=" + Path.Combine(fixture, "profile"), new Uri(Path.Combine(fixture, "index.html")).AbsoluteUri }) start.ArgumentList.Add(value);
        using var process = Process.Start(start)!; DesktopSpeedSession? session = null; SpeedCdp? inspector = null; var eligible = true;
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
            string? socket = null;
            for (var i = 0; i < 100 && socket is null; i++)
            {
                try
                {
                    using var data = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"));
                    socket = data.RootElement.EnumerateArray().FirstOrDefault(page => page.GetProperty("type").GetString() == "page").GetProperty("webSocketDebuggerUrl").GetString();
                }
                catch { await Task.Delay(100); }
            }
            Assert(socket is not null, "Fixture browser did not start");
            inspector = await SpeedCdp.ConnectAsync(new Uri(socket!), CancellationToken.None);
            // Retain the first notification across a real resume and a second pause.
            // A delayed consumer must not act on the first pause's invalid call frames.
            using (var pauseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await inspector.CallAsync("Debugger.enable", new { }, pauseTimeout.Token);
                var firstEvaluation = inspector.CallAsync("Runtime.evaluate", new { expression = "debugger;1" }, pauseTimeout.Token);
                var firstPause = await NextPause();
                Assert(inspector.IsCurrentPause(firstPause), "Live debugger pause was not tracked");
                await inspector.CallAsync("Debugger.resume", new { }, pauseTimeout.Token); await firstEvaluation;
                var secondEvaluation = inspector.CallAsync("Runtime.evaluate", new { expression = "debugger;2" }, pauseTimeout.Token);
                var secondPause = await NextPause();
                Assert(!inspector.IsCurrentPause(firstPause) && inspector.IsCurrentPause(secondPause), "A stale debugger pause can target the next pause");
                await inspector.CallAsync("Debugger.resume", new { }, pauseTimeout.Token); await secondEvaluation;
                Assert(!inspector.IsCurrentPause(secondPause), "Resumed debugger frames remain active");
                await inspector.CallAsync("Debugger.disable", new { }, pauseTimeout.Token);

                async Task<long> NextPause()
                {
                    while (await inspector.Events.WaitToReadAsync(pauseTimeout.Token))
                        while (inspector.Events.TryRead(out var value))
                            if (value.Message.GetProperty("method").GetString() == "Debugger.paused") return value.Pause;
                    throw new InvalidOperationException("The debugger pause notification was lost");
                }
            }
            var locations = DesktopSpeedPatch.RuntimeLocations("assets/runtime.js", Runtime);
            session = await DesktopSpeedSession.StartAsync(port, browser, new(locations, new Dictionary<string, int>()), () => eligible);
            try { await session.WaitUntilAppliedAsync(); }
            catch (Exception error)
            {
                var debug = await inspector.CallAsync("Runtime.evaluate", new { expression = "JSON.stringify({url:location.href,ready:globalThis.ready,resources:performance.getEntriesByType('resource').map(x=>x.name)})", returnByValue = true }, CancellationToken.None);
                throw new InvalidOperationException(error.Message + " Browser: " + debug.GetRawText(), error);
            }
            for (var i = 0; i < 100; i++)
            { if ((await Evaluate("globalThis.ready===true")).ValueKind == JsonValueKind.True) break; await Task.Delay(100); }
            var result = await Evaluate("(async()=>{const e={getHostId:()=>\"local\",getAccount:async()=>({account:{type:globalThis.auth}})},r={runtime:{isUsingCopilotInference:async()=>false},settings:{readPermissionRefreshEnabled:()=>true}},m={submissionOptions:{serviceTier:\"ultrafast\"},context:{}};return[menu({}).serviceTierAccess,(await request({query:{setData(){}}},\"local\")).allowed[0],await makeDefault(e,r,()=>{},null).readServiceTier(null),await makeExplicit(e,r,()=>null,null,null)(null,m),menu({hostId:\"ssh\"}).serviceTierAccess,await makeExplicit(e,r,()=>null,null,null)(null,{...m,submissionOptions:{serviceTier:\"fast\",executionHostId:\"ssh\"}}),await makeDefault(e,{runtime:{isUsingCopilotInference:async()=>true}},()=>{},null).readServiceTier(null),await makeExplicit(e,{...r,runtime:{isUsingCopilotInference:async()=>true}},()=>null,null,null)(null,m)]})()");
            Assert(result.GetRawText() == "[\"fast\",\"fast\",true,\"ultrafast\",null,null,false,null]", "Four gate behavior: " + result.GetRawText());
            await Evaluate("globalThis.speedCatalog=[{model:'visible-model',serviceTiers:[]},{model:'gpt-6.1-sol',serviceTiers:[{id:'priority',name:'Fast'}]},{model:'other-selected-model',serviceTiers:[{id:'ultrafast',name:'Ultrafast'}]}];true");
            var catalog = await Evaluate("['gpt-6.1-sol','other-selected-model','unknown-model'].map(model=>readTierOptions({hostId:'local'},{modelSettings:{model}}).map(t=>t.id))");
            Assert(catalog.GetRawText() == "[[\"priority\"],[\"ultrafast\"],[]]", "Selected model tiers were lost or invented: " + catalog.GetRawText());
            Assert((await Evaluate("readTierOptions({hostId:'ssh'},{modelSettings:{model:'gpt-6.1-sol'}}).length")).GetInt32() == 0, "Selected model lookup changed a remote catalog");
            if (nativeAssets is not null)
            {
                Assert((await Evaluate("globalThis.nativeReady===true")).GetBoolean(), "Native registration did not finish: " + session.Status);
                var patched = await Evaluate("globalThis.nativePatched");
                Assert(patched.EnumerateArray().All(value => value.GetInt32() > 0), "Native chunks were not patched: " + patched.GetRawText());
                File.WriteAllText(Path.Combine(root, "native-module-summary.json"), JsonSerializer.Serialize(new { passed = true, chunks = patched.EnumerateArray().Select(value => value.GetInt32()).ToArray(), cspPreserved = true, source = "Codex 26.1106.6628.0 public renderer modules; factories compiled before registration, not executed" }));
            }
            var csp = await Evaluate("(()=>{try{new Function('return 1')();return false}catch{return true}})()");
            Assert(csp.GetBoolean(), "Fixture CSP was bypassed");
            await Evaluate("globalThis.auth=\"unknown-auth\";true"); Assert((await Evaluate("menu({}).serviceTierAccess")).ValueKind == JsonValueKind.Null);
            await Evaluate("globalThis.auth=\"apiKey\";true"); eligible = false;
            for (var i = 0; i < 100 && session.IsRunning; i++) await Task.Delay(100);
            Assert(!session.IsApplied && (await Evaluate("menu({}).serviceTierAccess")).ValueKind == JsonValueKind.Null, "Disabled session still allowed a tier");
            Assert((await Evaluate("readTierOptions({hostId:'local'},{modelSettings:{model:'gpt-6.1-sol'}}).length")).GetInt32() == 0, "Disabled session still changed the selected model lookup");

            async Task<JsonElement> Evaluate(string expression)
            {
                var value = await inspector!.CallAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true, allowUnsafeEvalBlockedByCSP = false }, CancellationToken.None);
                if (value.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.GetRawText());
                return value.GetProperty("result").GetProperty("value");
            }
        }
        catch (Exception error) { throw new InvalidOperationException(error.Message + " Session: " + session?.Status + " Fixture: " + fixture, error); }
        finally
        {
            if (session is not null) await session.DisposeAsync();
            if (inspector is not null) { try { await inspector.CallAsync("Browser.close", new { }, CancellationToken.None); } catch { } await inspector.DisposeAsync(); }
            // Only the headless fixture owned by this test can be terminated.
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }

    // The real renderer runs in a separate app profile and fictional CODEX_HOME.
    // Only this newly created instance is debugged, reloaded and closed.
    internal static async Task DesktopAsync(string root)
    {
        var fixture = Path.Combine(Path.GetFullPath(root), "native-desktop-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(fixture, "home"); var profile = Path.Combine(fixture, "profile");
        Directory.CreateDirectory(home); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='gpt-6.1-sol'\nopenai_base_url='http://127.0.0.1:9/v1'\n[features]\nfast_mode=true\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-isolated-speed-key\"}");
        var platform = new WindowsCodexDesktopPlatform();
        var running = platform.Running();
        var target = new CodexDesktopTarget(platform.Installed().Distinct(StringComparer.OrdinalIgnoreCase).Single(), []);
        var original = running.Where(value => !running.Any(parent => parent.Id == value.ParentId)).ToArray();
        var plan = DesktopSpeedPatch.Inspect(target.Executable);
        var start = CodexDesktop.CreateStartInfo(target.Executable, home, null); platform.PrepareStart(start);
        start.Environment["CODEX_ELECTRON_USER_DATA_PATH"] = profile;
        start.Environment.Remove("OPENAI_API_KEY");
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        start.ArgumentList.Add("--user-data-dir=" + profile);
        start.ArgumentList.Add("--remote-debugging-address=127.0.0.1"); start.ArgumentList.Add("--remote-debugging-port=" + port);
        using var process = Process.Start(start)!; DesktopSpeedSession? session = null; SpeedCdp? inspector = null;
        Assert(!original.Any(value => value.Id == process.Id), "Fixture reused an existing desktop process");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                Assert(!process.HasExited, "Isolated native desktop exited before compatibility could load");
                try
                {
                    if (GatewayWorkerProcess.PortFree(port)) { await Task.Delay(200, timeout.Token); continue; }
                    using var listener = GatewayWorkerProcess.Capture(port);
                    if (listener is not null) Assert(listener.Id == process.Id, "Fixture debugger belongs to another process");
                    session = await DesktopSpeedSession.StartAsync(port, target.Executable, plan, () => true, timeout.Token); break;
                }
                catch (Exception error) when (error is DesktopSpeedNotReadyException or HttpRequestException)
                { await Task.Delay(200, timeout.Token); }
            }
            await session.WaitUntilAppliedAsync(timeout.Token);
            Assert(session.IsApplied, "Real renderer did not confirm compatibility");
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            using var pages = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", timeout.Token));
            var page = pages.RootElement.EnumerateArray().Single(value => value.GetProperty("type").GetString() == "page" && value.GetProperty("url").GetString()!.StartsWith("app://", StringComparison.Ordinal));
            inspector = await SpeedCdp.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), timeout.Token);
            var runtime = JsonSerializer.Serialize("app://-/" + plan.Runtimes.Single(value => value.Path.Contains("348410", StringComparison.Ordinal)).Path);
            var state = await inspector.CallAsync("Runtime.evaluate", new
            {
                expression = "(async()=>{const w=(await import(" + runtime + ")).__webpack_require__;" + """
const select=(w.c.Xvtr?.exports??w("Xvtr")).a;
const model={model:"gpt-6.1-sol",displayName:"Isolated model",supportedReasoningEfforts:[{reasoningEffort:"high",description:"Fixture"}],serviceTiers:[{id:"priority",name:"Fast"}],hidden:false,availableAccessPrograms:null,isDefault:false};
const options={additionalAvailableModels:new Set(),authMethod:"apikey",availableModels:new Set(["old-default-model"]),defaultModel:null,enabledReasoningEfforts:new Set(["high"]),hasConfiguredModelCatalog:false,includeUltraReasoningEffort:true,isCustomModelProvider:false,models:[model],useHiddenModels:true};
const before=select(options),after=select({...options,additionalAvailableModels:new Set([model.model])});
return {beforeCount:before.models.length,afterCount:after.models.length,tiers:after.models[0]?.serviceTiers,selectedModelQueryPatched:Function.prototype.toString.call(w.m.uizX).includes("additionalAvailableModels:")};
})()
""", returnByValue = true, awaitPromise = true, allowUnsafeEvalBlockedByCSP = false
            }, timeout.Token);
            var nativeLookup = state.GetProperty("result").GetProperty("value");
            Assert(nativeLookup.GetProperty("beforeCount").GetInt32() == 0 && nativeLookup.GetProperty("afterCount").GetInt32() == 1 &&
                nativeLookup.GetProperty("selectedModelQueryPatched").GetBoolean() &&
                nativeLookup.GetProperty("tiers")[0].GetProperty("id").GetString() == "priority",
                "Native selected model lookup lost its original Fast tier: " + nativeLookup.GetRawText());
            File.WriteAllText(Path.Combine(fixture, "result.json"), JsonSerializer.Serialize(new
            { passed = true, isolatedHome = home, isolatedProfile = profile, status = session.Status, nativeSelectedModelLookup = nativeLookup, originalProcessesPreserved = original.All(value => platform.Running().Contains(value)) }));
            Console.WriteLine("Isolated native desktop PASS: " + session.Status);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(fixture, "result.json"), JsonSerializer.Serialize(new { passed = false, error = error.Message, status = session?.Status }));
            throw new InvalidOperationException(error.Message + " Native fixture: " + fixture, error);
        }
        finally
        {
            if (inspector is not null) await inspector.DisposeAsync();
            if (session is not null) await session.DisposeAsync();
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            Assert(original.All(value => platform.Running().Contains(value)), "The original desktop changed during the isolated test");
        }
    }
    private static void Assert(bool condition, string message = "Speed assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Expected unsupported speed shape to fail"); }
}
