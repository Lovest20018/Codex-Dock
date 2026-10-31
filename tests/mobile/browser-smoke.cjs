// Browser QA uses a new temporary Chrome profile and fulfills every request in
// memory. It never reaches a real gateway, account, or native Codex process.
'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');
const {execFileSync} = require('node:child_process');
const {chromium} = require('playwright-core');
const {PNG} = require('pngjs');
const jsQR = require('jsqr');
const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'artifacts/mobile-browser');
const origin = 'https://phone.example.com';
const sid = '12345678-1234-1234-1234-123456789abc';
const checks = [];

(async () => {
  await fs.mkdir(out, {recursive: true});
  const files = Object.fromEntries(await Promise.all(['index.html','app.js','app.css'].map(async name => [name, await fs.readFile(path.join(root, 'src/CodexMobile/web', name))])));
  const browser = await chromium.launch({executablePath: process.env.CODEX_DOCK_TEST_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true});
  try {
    const context = await browser.newContext({viewport:{width:1180,height:800}});
    let logged = false, mode = 'accept', forceUnauthorized = false, mutations = [], fakeState = {
      id:sid,title:'Isolated chat',cwd:'C:/fake-project',provider:'openai',model:'fake-model',canWrite:true,connected:true,status:'idle',requests:[],operations:[],
      turns:[{id:'turn-1',status:'completed',messages:[{id:'u1',role:'user',text:'A saved user message'},{id:'a1',role:'assistant',text:'<img src=x onerror="window.injected=true">\nA fake Codex response.'}]}]
    };
    await context.route('**/*', async route => {
      const request = route.request(), url = new URL(request.url());
      if (url.origin !== origin) return route.abort();
      const staticFile = {'/':'index.html','/app.js':'app.js','/app.css':'app.css'}[url.pathname];
      if (staticFile) return route.fulfill({status:200,contentType:staticFile.endsWith('.js')?'text/javascript':staticFile.endsWith('.css')?'text/css':'text/html',body:files[staticFile]});
      let data;
      if (url.pathname === '/api/auth') data = {authenticated:logged,passwordless:false,csrf:logged?'fake-csrf':null,draftIdentity:'fake-account-scope',enabledClients:['codex'],computer:{name:'Test computer'}};
      else if (url.pathname === '/api/login' || url.pathname === '/api/pair') { logged=true; data={csrf:'fake-csrf'}; }
      else if (url.pathname === '/api/logout') { logged=false; data={ok:true}; }
      else if (url.pathname === '/api/sessions') {
        const q = url.searchParams.get('q');
        if (q === 'old') await new Promise(resolve=>setTimeout(resolve,300));
        data={sessions:[{id:sid,title:q==='new'?'New search result':'<script>bad title</script>',cwd:'C:/fake-project'}]};
      } else if (url.pathname === '/api/sessions/'+sid) {
        if (forceUnauthorized) return route.fulfill({status:401,contentType:'application/json',body:'{"error":"please login","code":"unauthenticated"}'});
        data=fakeState;
      }
      else if (url.pathname.startsWith('/api/sessions/'+sid+'/')) {
        const body = request.postDataJSON(); mutations.push({path:url.pathname,body});
        if (mode === 'network') return route.abort('failed');
        if (mode === 'validation') return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error:'Fake validation rejection'})});
        data={id:body.id,status:mode==='unknown'?'unknown':'accepted'};
        fakeState.operations.push({id:body.id,status:data.status,kind:url.pathname.split('/').at(-1)});
      } else return route.fulfill({status:404,contentType:'application/json',body:'{"error":"not enabled"}'});
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(data)});
    });
    const page = await context.newPage(), errors=[];
    page.on('pageerror', error=>errors.push(error.message));
    await page.addInitScript(() => { window.testEvents=[]; for(const name of ['bridge-authenticated','bridge-logged-out','bridge-auth-lost','bridge-chat-visible'])document.addEventListener(name,()=>window.testEvents.push(name)); });
    page.on('dialog', async dialog=>dialog.accept());
    await page.goto(origin); await page.locator('#username').fill('tester'); await page.locator('#password').fill('fake-browser-password');
    await page.screenshot({path:path.join(out,'login-desktop.png')});
    await page.locator('#login-button').click(); await page.locator('.session').waitFor();
    assert(await page.evaluate(()=>window.testEvents.includes('bridge-authenticated')));
    assert.equal(await page.locator('.session strong').textContent(),'<script>bad title</script>');
    await page.locator('.session').click(); await page.getByText('A fake Codex response.',{exact:false}).waitFor();
    assert.equal(await page.evaluate(()=>!!window.injected),false);
    await page.screenshot({path:path.join(out,'chat-desktop.png')});
    checks.push('Login and Android authenticated event; conversation content and titles remain text');

    await page.locator('#message').fill('accepted-message'); await page.locator('#send').click();
    await page.waitForFunction(()=>document.getElementById('message').value==='');
    assert.equal(mutations.length,1); assert.equal(mutations[0].body.text,'accepted-message');
    assert.match(mutations[0].body.id,/^[0-9a-f-]{36}$/);
    checks.push('Explicit send uses UUID and clears draft only after confirmed acceptance');

    mode='network'; await page.locator('#message').fill('uncertain-message'); await page.locator('#send').click();
    await page.locator('#operation-warning').waitFor(); await page.waitForFunction(()=>document.getElementById('acknowledge').disabled===false);
    assert(await page.locator('#send').isDisabled()); const count=mutations.length;
    await page.reload(); await page.locator('.session').click(); await page.locator('#operation-warning').waitFor();
    assert.equal(await page.locator('#message').inputValue(),'uncertain-message');
    await page.waitForTimeout(2200); assert.equal(mutations.length,count);
    assert(await page.locator('#send').isDisabled()); await page.locator('#acknowledge').click();
    await page.waitForFunction(()=>document.getElementById('operation-warning').hidden);
    checks.push('Lost HTTP response persists across reload and never replays automatically');

    mode='validation'; await page.locator('#send').click(); await page.waitForFunction(()=>document.getElementById('acknowledge').disabled===false);
    assert(await page.locator('#operation-warning').isHidden()); assert.equal(await page.locator('#message').inputValue(),'uncertain-message');
    mode='accept'; fakeState.requests=[{id:17,method:'item/commandExecution/requestApproval',supported:true,params:{command:'fake-read-only-command',cwd:'C:/fake-project',availableDecisions:['accept','decline']}}];
    await page.locator('#refresh').click(); await page.getByRole('button',{name:'本次批准',exact:true}).waitFor();
    await page.getByRole('button',{name:'本次批准',exact:true}).click(); await page.waitForFunction(()=>document.getElementById('acknowledge').disabled===false);
    assert.equal(mutations.at(-1).body.requestId,17); assert.deepEqual(mutations.at(-1).body.response,{decision:'accept'});
    checks.push('Validation errors retain draft; native approval forwards exact request and one-turn choice');

    fakeState.requests=[{id:18,method:'item/tool/requestUserInput',supported:true,params:{questions:[{id:'q1',question:'Which option?',options:[{label:'A'},{label:'B'}]}]}}];
    await page.locator('#refresh').click(); await page.locator('#approvals input').fill('A'); await page.waitForTimeout(2300);
    assert.equal(await page.locator('#approvals input').inputValue(),'A');
    checks.push('Polling preserves an unfinished native question answer');
    await page.setViewportSize({width:390,height:844}); await page.screenshot({path:path.join(out,'chat-mobile-approval.png')});
    assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    await page.evaluate(()=>window.BridgeNativeTheme={dark:true}); await page.evaluate(()=>document.dispatchEvent(new Event('bridge-native-theme')));
    await page.screenshot({path:path.join(out,'chat-mobile-dark.png')});
    assert.equal(await page.evaluate(()=>window.BridgeNavigation.back()),true); await page.locator('#sidebar').waitFor();
    assert.equal(await page.evaluate(()=>window.BridgeNavigation.back()),false);
    checks.push('Android back navigation, mobile width, native dark-theme event');

    await page.locator('#search').fill('old'); await page.locator('#search-form button').click();
    await page.locator('#search').fill('new'); await page.locator('#search-form button').click();
    await page.waitForTimeout(600); assert.equal(await page.locator('.session').count(),1); assert.equal(await page.locator('.session strong').textContent(),'New search result');
    checks.push('Late search responses cannot replace or append to a newer query');
    await page.locator('#logout').click(); await page.locator('#login-button').waitFor();
    assert(await page.evaluate(()=>window.testEvents.includes('bridge-logged-out')));
    logged=false; await page.goto(origin+'/#pair='+'f'.repeat(43)); await page.locator('.session').waitFor();
    assert.equal(await page.evaluate(()=>location.hash),'');
    assert(await page.evaluate(()=>window.testEvents.includes('bridge-authenticated')));
    checks.push('Pairing fragment is cleared before exchange and native session events are emitted');
    // Exercise the actual four scripts injected by the pinned upstream APK.
    // This verifies DOM/hooks, not Android OS cookie/key-store implementation.
    logged=false; const nativePage=await context.newPage(); nativePage.on('pageerror',error=>errors.push(error.message));
    await nativePage.setViewportSize({width:390,height:844}); await nativePage.goto(origin);
    await nativePage.evaluate(()=>{window.nativePrompts=[];window.prompt=(message,value)=>{window.nativePrompts.push({type:message.split(':')[0],value});return 'saved';};});
    for(const name of ['mobile-ui.js','mobile-clipboard.js','mobile-system-bars.js','mobile-session.js']){
      const script=(await fs.readFile(path.join(__dirname,'android-host',name),'utf8')).replaceAll('__BRIDGE_CLIPBOARD_TOKEN__','fake-native-token').replaceAll('__BRIDGE_CONNECTION_NAME__','Test alias').replaceAll('__BRIDGE_NATIVE_LANGUAGE__','zh');
      await nativePage.evaluate(script);
    }
    await nativePage.locator('#username').fill('tester');await nativePage.locator('#password').fill('fake-password');await nativePage.locator('#login-button').click();await nativePage.locator('.session').waitFor();
    assert(await nativePage.evaluate(()=>window.nativePrompts.some(row=>row.type==='codexbridge-session')));
    assert.equal(await nativePage.locator('.sidebar-foot').evaluate(node=>node.parentElement.id),'sidebar');
    await nativePage.locator('.session').click(); await nativePage.locator('#approvals input').waitFor();
    assert(await nativePage.evaluate(()=>window.nativePrompts.some(row=>row.type==='codexbridge-chat-visible')));
    assert.equal(await nativePage.locator('.chat-head').evaluate(node=>getComputedStyle(node).paddingRight),'64px');
    assert(await nativePage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    await nativePage.screenshot({path:path.join(out,'android-host-injected.png')});
    forceUnauthorized=true;await nativePage.locator('#refresh').click();await nativePage.locator('#login-button').waitFor();
    assert(await nativePage.evaluate(()=>window.nativePrompts.some(row=>row.type==='codexbridge-auth-lost')));forceUnauthorized=false;
    await nativePage.locator('#password').fill('fake-password');await nativePage.locator('#login-button').click();await nativePage.locator('.session').waitFor();await nativePage.locator('#logout').click();await nativePage.locator('#login-button').waitFor();
    assert(await nativePage.evaluate(()=>window.nativePrompts.some(row=>row.type==='codexbridge-logout')));
    checks.push('Actual upstream Android host scripts preserve layout, native session, auth-lost, logout and chat-visible hooks');
    assert.deepEqual(errors,[]);
    await context.close();
    const python = process.env.CODEX_DOCK_TEST_PYTHON || 'python';
    const url=origin+'/#pair='+'f'.repeat(43);
    const script="import sys;sys.path.insert(0,'src/CodexMobile');from qr_png import pairing_image;print(pairing_image('"+url+"'))";
    const image=PNG.sync.read(Buffer.from(execFileSync(python,['-B','-c',script],{cwd:root,encoding:'utf8'}).trim(),'base64'));
    const qr=jsQR(new Uint8ClampedArray(image.data),image.width,image.height);
    assert.equal(qr?.data,url); checks.push('Independent jsQR decoder recovers the exact pairing URL from generated PNG');
    await fs.writeFile(path.join(out,'report.json'),JSON.stringify({status:'PASS',checks},null,2));
    console.log('PASS '+checks.length+' browser/QR checks. Screenshots: '+out);
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
