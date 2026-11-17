'use strict';
// Real Dock HTTP adapter and browser decoding; no user account or native runtime.
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process');
const {chromium}=require('playwright-core');
const root=path.resolve(__dirname,'../..'),out=path.resolve(process.env.CODEX_DOCK_BRAND_TEST_OUTPUT||path.join(root,'artifacts/web-brand'));
(async()=>{
 await fs.mkdir(out,{recursive:true});
 const child=spawn(process.env.CODEX_DOCK_TEST_PYTHON||path.join(root,'artifacts/mobile-build-venv/Scripts/python.exe'),['-B','tests/android/gateway_fixture.py'],{cwd:root,windowsHide:true,stdio:['pipe','pipe','inherit']});
 let browser;
 try{
  const endpoint=await new Promise((resolve,reject)=>{let data='';const timer=setTimeout(()=>reject(Error('Brand fixture startup timed out')),20000);child.stdout.on('data',chunk=>{data+=chunk;if(data.includes('\n')){clearTimeout(timer);resolve(JSON.parse(data.split('\n')[0]));}});child.once('exit',code=>{clearTimeout(timer);reject(Error('Brand fixture exited '+code));});});
  browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  const checks=[];
  for(const width of [390,1280]){
   const context=await browser.newContext({viewport:{width,height:844}}),page=await context.newPage(),errors=[];
   page.on('pageerror',error=>errors.push(error.message));
   await page.goto(endpoint.url);await page.locator('#login-button').waitFor();
   const mark=page.locator('.phone-header .mark');
   const expected=crypto.createHash('sha256').update(await fs.readFile(path.join(root,'src/CodexMobile/ui/brand/icon-ui.png'))).digest('hex');
   await page.waitForFunction(()=>document.querySelector('.phone-header .mark')?.complete);
   assert.deepEqual(await mark.evaluate(img=>({path:new URL(img.src).pathname,version:new URL(img.src).searchParams.get('v'),width:img.naturalWidth,height:img.naturalHeight})),{path:'/icon-ui.png',version:expected,width:64,height:64});
   checks.push(width+'px header decodes the selected 64px brand with its current digest');
   const icon=await page.getAttribute('link[rel=icon]','href'),touch=await page.getAttribute('link[rel=apple-touch-icon]','href');
   for(const [url,size] of [[icon,32],[touch,180]]){
    const data=await (await context.request.get(new URL(url,endpoint.url).href)).body();
    assert.equal(data.readUInt32BE(16),size);assert.equal(data.readUInt32BE(20),size);
   }
   checks.push(width+'px favicon and touch icon have their required dimensions');
   const manifest=await (await context.request.get(new URL(await page.getAttribute('link[rel=manifest]','href'),endpoint.url).href)).json();
   assert.equal(manifest.name,'Codex Dock');assert.equal(manifest.theme_color,'#EEF2ED');
   assert.deepEqual(manifest.icons.map(icon=>icon.sizes),['192x192','512x512']);
   checks.push(width+'px Web install manifest uses Dock branding and both installation icon sizes');
   await page.screenshot({path:path.join(out,'brand-'+width+'-light.png'),fullPage:true});
   await page.evaluate(()=>{document.documentElement.dataset.theme='dark';});
   await page.screenshot({path:path.join(out,'brand-'+width+'-dark.png'),fullPage:true});
   assert.deepEqual(errors,[]);await context.close();
  }
  await fs.writeFile(path.join(out,'report.json'),JSON.stringify({status:'PASS',scope:'Actual local Dock HTTP and browser; login page and icons only',checks},null,2));
  console.log('PASS Web branding: '+checks.length+' checks at 390/1280px');
 }finally{
  if(browser)await browser.close();
  child.stdin.end('\n');
  await new Promise(resolve=>{const timer=setTimeout(()=>{child.kill();resolve();},6000);child.once('exit',()=>{clearTimeout(timer);resolve();});});
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
