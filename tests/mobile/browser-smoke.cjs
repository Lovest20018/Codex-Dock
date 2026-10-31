// Real isolated worker and reused upstream account panels; no native Codex writes.
'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),http=require('node:http');
const assert=require('node:assert/strict');
const {spawn,execFileSync}=require('node:child_process');
const {chromium}=require('playwright-core'),{PNG}=require('pngjs'),jsQR=require('jsqr');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/mobile-browser');
const python=process.env.CODEX_DOCK_TEST_PYTHON||'C:/Users/com/AppData/Local/Python/pythoncore-3.12-64/python.exe';
const checks=[];
function proxy(request,port){return new Promise((resolve,reject)=>{
  const headers={...request.headers(),host:'fixture.invalid'};delete headers['content-length'];
  const body=request.postDataBuffer();if(body)headers['content-length']=body.length;
  const req=http.request({host:'127.0.0.1',port,path:new URL(request.url()).pathname+new URL(request.url()).search,method:request.method(),headers},res=>{
    const chunks=[];res.on('data',chunk=>chunks.push(chunk));res.on('end',()=>resolve({status:res.statusCode,headers:res.headers,body:Buffer.concat(chunks)}));
  });req.on('error',reject);req.end(body);
});}
(async()=>{
 await fs.mkdir(out,{recursive:true});
 const child=spawn(python,['-B','tests/mobile/manager_fixture.py'],{cwd:root,windowsHide:true});
 let browser;
 child.stderr.on('data',data=>process.stderr.write(data));
 try{
  const value=await new Promise((resolve,reject)=>{let data='';child.stdout.on('data',chunk=>{data+=chunk;if(data.includes('\n'))resolve(JSON.parse(data.split('\n')[0]));});child.on('exit',code=>reject(Error('Fixture exited '+code)));});
  browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  const context=await browser.newContext({viewport:{width:1180,height:900}}),page=await context.newPage(),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.goto(value.url);await page.locator('#cli .cli-access-form').waitFor();
  assert.equal(new URL(page.url()).hash,'');
  await page.getByRole('button',{name:'保存 API 接入',exact:true}).waitFor();
  await page.getByText('添加账号或 API',{exact:true}).first().waitFor();
  assert.equal(await page.getByText('Claude Code',{exact:true}).count(),0);
  await page.screenshot({path:path.join(out,'codex-manager-desktop.png'),fullPage:true});
  checks.push('One-use local manager ticket removed; original accounts and Codex API forms render');
  await page.reload();await page.locator('#cli .cli-access-form').waitFor();
  assert.equal(await page.locator('#content').isVisible(),true);
  await page.setViewportSize({width:390,height:844});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await page.emulateMedia({colorScheme:'dark'});
  await page.screenshot({path:path.join(out,'codex-manager-mobile-dark.png'),fullPage:true});
  checks.push('Manager cookie reload, narrow layout and dark appearance');
  await context.close();
  const phone=await browser.newContext({viewport:{width:390,height:844}});
  await phone.route('https://fixture.invalid/**',async route=>{
    const result=await proxy(route.request(),value.publicPort);
    const headers=Object.fromEntries(Object.entries(result.headers).filter(([key])=>!['transfer-encoding','content-length','connection','set-cookie'].includes(key)).map(([key,val])=>[key,String(val)]));
    if(result.headers['set-cookie'])headers['set-cookie']=result.headers['set-cookie'].join('\n');
    await route.fulfill({status:result.status,headers,body:result.body});
  });
  const mobile=await phone.newPage();mobile.on('pageerror',error=>errors.push(error.message));
  await mobile.goto('https://fixture.invalid/');await mobile.locator('#login-button').waitFor();
  await mobile.evaluate(()=>{window.nativePrompts=[];window.prompt=(message,value)=>{window.nativePrompts.push(message.split(':')[0]);return 'saved';};});
  for(const name of ['mobile-ui.js','mobile-clipboard.js','mobile-system-bars.js','mobile-session.js']){
    const script=(await fs.readFile(path.join(__dirname,'android-host',name),'utf8')).replaceAll('__BRIDGE_CLIPBOARD_TOKEN__','fixture-native-token').replaceAll('__BRIDGE_CONNECTION_NAME__','Isolated fixture').replaceAll('__BRIDGE_NATIVE_LANGUAGE__','zh');
    await mobile.evaluate(script);
  }
  await mobile.locator('#username').fill('fixture');await mobile.locator('#password').fill('isolated-manager-fixture');await mobile.locator('#login-button').click();
  await mobile.locator('#app').waitFor();
  assert(await mobile.evaluate(()=>window.nativePrompts.includes('codexbridge-session')));
  assert(await mobile.locator('#harness-open').isHidden());
  assert(await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  await mobile.screenshot({path:path.join(out,'upstream-workbench-android-host.png'),fullPage:true});
  await mobile.locator('#accounts-button').click();await mobile.locator('.accounts-panel').first().waitFor();
  checks.push('Full production web assets login through real gateway; upstream Android session hooks and account panel');
  await mobile.reload();await mobile.locator('#app').waitFor();
  // The Android host hides the Web logout button; its native menu invokes it.
  await mobile.evaluate(()=>document.getElementById('logout').click());await mobile.locator('#login-button').waitFor();
  assert.deepEqual(errors,[]);
  checks.push('Valid cookie survives page reload; logout returns to login without JavaScript errors');
  await phone.close();
  const url='https://fixture.invalid/#pair='+'f'.repeat(43);
  const script="import sys;sys.path.insert(0,'src/CodexMobile');from qr_png import pairing_image;print(pairing_image('"+url+"'))";
  const png=PNG.sync.read(Buffer.from(execFileSync(python,['-B','-c',script],{cwd:root,encoding:'utf8'}).trim(),'base64'));
  assert.equal(jsQR(new Uint8ClampedArray(png.data),png.width,png.height)?.data,url);
  checks.push('Independent QR decoder recovers exact local pairing URL');
  await fs.writeFile(path.join(out,'report.json'),JSON.stringify({status:'PASS',checks},null,2));
  console.log('PASS '+checks.length+' real gateway/browser/QR checks');
 }finally{if(browser)await browser.close();child.stdin.end('\n');await new Promise(resolve=>{child.once('exit',resolve);setTimeout(()=>{child.kill();resolve();},4000).unref();});}
})().catch(error=>{console.error(error);process.exitCode=1;});
