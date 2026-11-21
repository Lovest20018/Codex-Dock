using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CodexConfig.Core;

internal static class ConnectionRecoveryBrowserTests
{
    private const string Runtime = "const modules={};function w(k){let e={exports:{}};modules[k](e,e.exports,w);return e.exports}w.m=modules;w.o=(o,k)=>Object.prototype.hasOwnProperty.call(o,k);w.C=e=>{var k,m=e.__webpack_modules__,r=e.__rspack_esm_runtime;for(k in m)w.o(m,k)&&(w.m[k]=m[k]);r&&r(w);};export{w as __webpack_require__};";
    private const string Module = """
export const __webpack_modules__={Manager:function(e,t,n){
class M {
  constructor(){this.requestClient={sendRequest:async(method,p,options,signal)=>{signal?.throwIfAborted();trace.push({method,p,sameSignal:signal==null?undefined:signal===globalThis.fixtureSignal});const s=fixtureThread;
    if(method==='thread/read')return{thread:{id:p.threadId,modelProvider:s.provider,cwd:'C:/fixture',status:{type:s.status}}};
    if(method==='config/read')return{config:{model_provider:'openai'}};
    if(method==='thread/unsubscribe'){s.status='notLoaded';return{status:'unsubscribed'}};
    if(method==='thread/resume'){s.status='idle';s.provider=p.modelProvider;return{thread:{id:p.threadId},modelProvider:p.modelProvider,model:p.model,reasoningEffort:p.config?.model_reasoning_effort,serviceTier:p.serviceTier}};
    if(method==='turn/start')return{turn:{id:'fixture-turn'}};return{};}}}
  assertActive(){}
  getHostId(){return 'local'}
  getConversation(){return{model:'selected-model',reasoningEffort:'max',serviceTier:'ultrafast',threadRuntimeStatus:{type:fixtureThread.status}}}
  async sendRequest(e,t,n,r){this.assertActive();let p=null==r?this.requestClient.sendRequest(e,t,n):this.requestClient.sendRequest(e,t,n,r),a=await p.catch(n=>{throw n});return a}
  async resumeThread(p){const value=await this.sendRequest('thread/resume',p);globalThis.cacheRefreshed=true;return value}
}
globalThis.trace=[];globalThis.fixtureThread={status:'idle',provider:'legacy'};globalThis.manager=new M();globalThis.fixtureReady=true;
}};
""";
    internal static async Task RunAsync(string root, string browser, string? nativeAssets = null)
    {
        root = Path.GetFullPath(Path.Combine(root, "connection-browser-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='selected-model'\nmodel_provider='openai'\nopenai_base_url='https://a.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), new ConnectionSettings("https://a.invalid/v1", "fake-browser-key", "selected-model").RenderAuth());
        var threadId = Guid.NewGuid().ToString();
        using (var db = new NativeSqlite(Path.Combine(home, "state_5.sqlite"), true, true))
        { db.Execute("CREATE TABLE threads(id TEXT PRIMARY KEY,model_provider TEXT)"); db.Execute("INSERT INTO threads VALUES(?1,'legacy')", threadId); }
        File.WriteAllText(Path.Combine(root, "index.html"), "<meta http-equiv=\"Content-Security-Policy\" content=\"script-src 'self'\"><script type=\"module\" src=\"./assets/main.js\"></script>");
        Directory.CreateDirectory(Path.Combine(root, "assets")); File.WriteAllText(Path.Combine(root, "assets", "runtime.js"), Runtime); File.WriteAllText(Path.Combine(root, "assets", "manager.js"), Module);
        var main = "import{__webpack_require__ as w}from'./runtime.js';import*as m from'./manager.js';w.C(m);for(const k of Object.keys(m.__webpack_modules__))w(k);";
        if (nativeAssets is not null)
        {
            File.WriteAllText(Path.Combine(root, "assets", "speed.js"), DesktopSpeedTests.Factories);
            main += "import*as speed from'./speed.js';w.C(speed);for(const k of Object.keys(speed.__webpack_modules__))w(k);globalThis.auth='apiKey';";
            var nativeFiles = new[] { "739301.37a39f26d6.js", "222624.ceee0263cf.js", "150595.413582832c.js" };
            // Use the exact previously inspected chunks, locating the request chunk
            // by its original gate marker when its hash differs in the saved sample.
            nativeFiles[2] = Directory.GetFiles(nativeAssets, "150595.*.js").Select(Path.GetFileName).Single()!;
            for (var index = 0; index < nativeFiles.Length; index++)
            {
                File.Copy(Path.Combine(nativeAssets, nativeFiles[index]), Path.Combine(root, "assets", "native" + index + ".js"));
                main += "import*as original" + index + " from'./native" + index + ".js';w.C(original" + index + ");";
            }
            main += "globalThis.nativeManagerPatched=Object.values(original0.__webpack_modules__).some(f=>Function.prototype.toString.call(f).includes('__codexDockConnection'));";
        }
        File.WriteAllText(Path.Combine(root, "assets", "main.js"), main);
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        var start = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-background-networking", "--allow-file-access-from-files", "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=" + port, "--user-data-dir=" + Path.Combine(root, "profile"), new Uri(Path.Combine(root, "index.html")).AbsoluteUri }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        SpeedCdp? inspector = null; DesktopSpeedSession? session = null; var checks = new List<string>();
        var enabled = true;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine("PASS " + name); }
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
            string? socket = null;
            for (var i = 0; i < 100 && socket is null; i++)
            {
                try
                {
                    using var pages = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"));
                    socket = pages.RootElement.EnumerateArray()
                        .Single(value => value.GetProperty("type").GetString() == "page" &&
                            Uri.TryCreate(value.GetProperty("url").GetString(), UriKind.Absolute, out var url) &&
                            url.Scheme == "file")
                        .GetProperty("webSocketDebuggerUrl").GetString();
                }
                catch { await Task.Delay(100); }
            }
            if (socket is null) throw new IOException("Fixture browser not ready");
            inspector = await SpeedCdp.ConnectAsync(new Uri(socket), CancellationToken.None);
            var store = new ConfigStore(new FixtureEnvironment(), _ => null); var baseline = store.Load(home);
            var backend = new DesktopConnectionBackend(store, baseline, () => enabled, () => true);
            var plan = new DesktopSpeedPlan(DesktopSpeedPatch.RuntimeLocations("assets/runtime.js", Runtime), new Dictionary<string, int> { ["connections"] = 1 }) { SpeedEnabled = nativeAssets is not null, ConnectionsEnabled = true };
            session = await DesktopSpeedSession.StartAsync(port, browser, plan, () => true, connections: backend, scope: "browser-fixture");
            await session.WaitUntilAppliedAsync();
            for (var i = 0; i < 100 && !(await Evaluate("globalThis.fixtureReady===true")).GetBoolean(); i++) await Task.Delay(50);
            var before = await Evaluate("({ready:globalThis.fixtureReady,enabled:globalThis.__codexDockConnection?.enabled,patched:Function.prototype.toString.call(manager.sendRequest).includes('__codexDockConnection')})");
            Check(before.GetProperty("ready").GetBoolean() && before.GetProperty("enabled").GetBoolean() && before.GetProperty("patched").GetBoolean(), "registration patch installs the real controller through CDP without changing CSP");
            var id = JsonSerializer.Serialize(threadId);
            var cancelled = await Evaluate("(async()=>{try{await manager.sendRequest('thread/resume',{threadId:" + id + "},null,AbortSignal.abort());return false}catch(e){return e.name==='AbortError'&&trace.length===0}})()");
            using (var db = new NativeSqlite(Path.Combine(home, "state_5.sqlite"))) Check(cancelled.GetBoolean() && db.Query("SELECT model_provider FROM threads WHERE id=?1", threadId).Single()[0] == "legacy",
                "an aborted native resume leaves the old thread loaded and its index untouched");
            await Evaluate("globalThis.fixtureSignal=new AbortController().signal;manager.sendRequest('turn/start',{threadId:" + id + ",clientUserMessageId:'stable-uuid',input:[{type:'text',text:'browser-private-sentinel'}]},null,fixtureSignal)");
            using (var db = new NativeSqlite(Path.Combine(home, "state_5.sqlite"))) Check(db.Query("SELECT model_provider FROM threads WHERE id=?1", threadId).Single()[0] == "openai", "renderer binding prepares only the synthetic chat index");
            var trace = await Evaluate("trace");
            Check(trace.EnumerateArray().Count(value => value.GetProperty("method").GetString() == "turn/start") == 1 && (await Evaluate("cacheRefreshed===true")).GetBoolean(), "native cache refresh completes before exactly one original input");
            Check(trace.EnumerateArray().All(value => value.GetProperty("method").GetString() == "turn/start"
                ? value.GetProperty("sameSignal").GetBoolean() : !value.TryGetProperty("sameSignal", out _)),
                "the native abort signal follows the original turn and is absent from auxiliary requests");
            var resume = trace.EnumerateArray().Single(value => value.GetProperty("method").GetString() == "thread/resume").GetProperty("p");
            Check(resume.GetProperty("model").GetString() == "selected-model" && resume.GetProperty("config").GetProperty("model_reasoning_effort").GetString() == "max" && resume.GetProperty("serviceTier").GetString() == "ultrafast", "recovery preserves model, Max and Ultra Fast selections");
            var doc = new TomlDocument(baseline.Config); doc.Set("openai_base_url", "https://b.invalid/v1"); File.WriteAllBytes(Path.Combine(home, "config.toml"), doc.Bytes);
            await Evaluate("trace=[];fixtureThread.status='active';manager.sendRequest('turn/start',{threadId:" + id + ",input:[]})");
            Check((await Evaluate("trace.some(x=>x.method==='thread/unsubscribe')")).GetBoolean() == false, "a live turn retains its connection without interruption");
            await Evaluate("trace=[];fixtureThread.status='idle';manager.sendRequest('turn/start',{threadId:" + id + ",input:[]})");
            Check((await Evaluate("trace.find(x=>x.method==='thread/resume').p.config.openai_base_url")).GetString() == "https://b.invalid/v1", "an idle send observes the newly saved connection generation");
            Check((await Evaluate("(()=>{try{new Function('return 1');return false}catch{return true}})()")).GetBoolean(), "recovery leaves the original Content Security Policy in force");
            if (nativeAssets is not null) Check((await Evaluate("globalThis.nativeManagerPatched===true")).GetBoolean(), "the current original Codex manager factory compiles with the same hook");
            if (nativeAssets is not null) Check((await Evaluate("menu({}).serviceTierAccess")).GetString() == "fast", "speed compatibility and old chat recovery operate in the same guarded session");
            await Evaluate("globalThis.pageSentinel='keep-this-document';globalThis.originalManager=manager;true");
            var liveRejected = false;
            try { await using var duplicate = await DesktopSpeedSession.StartAsync(port, browser, plan, () => true, connections: backend, scope: "browser-fixture", resumeOnly: true); }
            catch (DesktopEnhancementResumeException) { liveRejected = true; }
            Check(liveRejected && (await Evaluate("pageSentinel==='keep-this-document'&&manager===originalManager&&__codexDockConnection.enabled")).GetBoolean(), "automatic reconnect cannot steal a live lease or reload its page");
            await session.DisposeAsync(); session = null;
            var wrongScope = false;
            try { await using var wrong = await DesktopSpeedSession.StartAsync(port, browser, plan, () => true, connections: backend, scope: "other-home", resumeOnly: true); }
            catch (DesktopEnhancementResumeException) { wrongScope = true; }
            Check(wrongScope && (await Evaluate("pageSentinel==='keep-this-document'")).GetBoolean(), "reconnect rejects another configuration scope without reloading or mutating the page");
            var wrongOwner = false;
            try { await using var wrong = await DesktopSpeedSession.StartAsync(port, browser, plan, () => true, connections: backend,
                expectedOwner: new(process.Id, 1, process.StartTime.ToFileTimeUtc() + 1, browser), scope: "browser-fixture", resumeOnly: true); }
            catch (IOException) { wrongOwner = true; }
            Check(wrongOwner, "reconnect requires the exact listener process creation time");
            session = await DesktopSpeedSession.StartAsync(port, browser, plan, () => true, connections: backend,
                expectedOwner: new(process.Id, 1, process.StartTime.ToFileTimeUtc(), browser), scope: "browser-fixture", resumeOnly: true);
            await session.WaitUntilAppliedAsync();
            Check((await Evaluate("pageSentinel==='keep-this-document'&&manager===originalManager&&__codexDockConnection.enabled")).GetBoolean(), "automatic reconnect restores the controller and leases while preserving the existing document and manager");
            await Evaluate("trace=[];fixtureThread.status='idle';manager.sendRequest('turn/start',{threadId:" + id + ",clientUserMessageId:'after-reconnect',input:[]})");
            Check((await Evaluate("trace.filter(x=>x.method==='turn/start'&&x.p.clientUserMessageId==='after-reconnect').length===1")).GetBoolean(), "reconnected recovery sends the original input once through the current binding");
            enabled = false; await Task.Delay(3500);
            Check(!(await Evaluate("globalThis.__codexDockConnection.enabled")).GetBoolean(), "turning off recovery disables the controller lease");
            File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { passed = true, checks, nativeAssets, realCredentialsUsed = false, realCodexDesktopChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
            async Task<JsonElement> Evaluate(string expression)
            {
                var response = await inspector.CallAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true, allowUnsafeEvalBlockedByCSP = false }, CancellationToken.None);
                if (response.TryGetProperty("exceptionDetails", out var error)) throw new IOException(error.GetRawText());
                return response.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
            }
        }
        catch (Exception error) { throw new InvalidOperationException(error.Message + " Fixture: " + root + " Session: " + session?.Status, error); }
        finally
        {
            if (session is not null) await session.DisposeAsync();
            if (inspector is not null) { try { await inspector.CallAsync("Browser.close", new { }, CancellationToken.None); } catch { } await inspector.DisposeAsync(); }
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
    private sealed class FixtureEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { } }
}
