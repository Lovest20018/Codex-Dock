'use strict';
// Actual production renderers and owned Android scripts; synthetic gateway only.
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{spawn}=require('node:child_process');
const {chromium}=require('playwright-core');
const root=path.resolve(__dirname,'../..'),upstream=path.join(root,'src/CodexMobile/upstream');
const out=path.join(root,'artifacts/mobile-backport-2026-11-09/browser');
(async()=>{
 await fs.mkdir(out,{recursive:true});
 const child=spawn(process.env.CODEX_DOCK_TEST_PYTHON||'C:/Users/com/AppData/Local/Python/pythoncore-3.12-64/python.exe',['-B','tests/mobile/upstream_browser_fixture.py'],{cwd:root,windowsHide:true,stdio:['pipe','pipe','inherit']});
 let browser;
 try{
  const url=await new Promise((resolve,reject)=>{let value='';const timer=setTimeout(()=>reject(Error('Fixture startup timeout')),20000);child.stdout.on('data',data=>{value+=data;if(value.includes('\n')){clearTimeout(timer);resolve(value.split('\n')[0].trim());}});child.once('exit',code=>{clearTimeout(timer);reject(Error('Fixture exited '+code));});});
  browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  const reading=await fs.readFile(path.join(__dirname,'reading-state.browser.js'),'utf8'),reports=[];
  const mobile=await fs.readFile(path.join(upstream,'mobile/shared/web/mobile-ui.js'),'utf8'),owned=await fs.readFile(path.join(root,'src/CodexAndroid/assets/lovest-shell.js'),'utf8'),clipboard=await fs.readFile(path.join(upstream,'mobile/shared/web/mobile-clipboard.js'),'utf8');
  for(const width of [390,1280]){
   const context=await browser.newContext({viewport:{width,height:844}}),page=await context.newPage(),errors=[];
   page.on('pageerror',error=>errors.push(error.message));
   await context.route('**/*',route=>new URL(route.request().url()).origin===url?route.continue():route.abort());
   await page.goto(url+'/?case=success');await page.waitForFunction(()=>document.getElementById('bootstrap-status')?.hidden);
   const result=await page.evaluate(reading+'\nrunChatReadingStateChecks();');
   assert(result.passed>=20,JSON.stringify(result));reports.push(result);
   const imagePath='/api/sessions/12345678-1234-4234-9234-123456789012/uploads/12345678-1234-4234-9234-123456789013/preview';
   await page.route('**'+imagePath+'*',route=>route.fulfill({status:200,contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0XcAAAAASUVORK5CYII=','base64')}));
   await page.evaluate(imagePath=>{const target=document.createElement('button');target.id='qa-image';target.dataset.imagePreview=imagePath+'?variant=thumb';target.dataset.imageName='原图.png';target.textContent='Open synthetic image';document.body.append(target);target.click();},imagePath);
   await page.locator('#image-preview-dialog[open]').waitFor();
   const save=page.locator('#image-preview-save');assert.equal(await save.isVisible(),true);
   const link=new URL(await save.getAttribute('href'));assert.equal(link.searchParams.get('download'),'1');assert.equal(link.searchParams.get('variant'),'original');
   assert.equal(await save.getAttribute('download'),'原图.png');assert.equal(await save.getAttribute('aria-label'),'保存到本地');
   const bounds=await save.boundingBox();assert(bounds.width>=44&&bounds.height>=44,'Save image remains a 44px touch target');
   assert.equal(await page.locator('#image-preview-dialog').evaluate(node=>node.scrollWidth<=node.clientWidth+1),true,'Image controls fit the viewport');
   await page.screenshot({path:path.join(out,'image-'+width+'.png')});await page.locator('#image-preview-close').click();
   const workspacePath='/api/sessions/12345678-1234-4234-9234-123456789012/workspace/download?path=plot.png';
   await page.evaluate(workspacePath=>{const image=document.createElement('img'),data='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0XcAAAAASUVORK5CYII=';image.src='data:image/png;base64,'+data;document.body.append(image);if(!BridgeImageViewer.registerWorkspaceImage(image,{kind:'image',name:'plot.png',mime:'image/png',data},{provider:'codex'},null,workspacePath))throw Error('Workspace image registration failed');image.click();},workspacePath);
   assert.equal(new URL(await save.getAttribute('href')).pathname+new URL(await save.getAttribute('href')).search,workspacePath,'Workspace image retains its authenticated file-download URL');await page.locator('#image-preview-close').click();
   // The native host may execute before DOM readiness or before its CSS is readable.
   // Hide real same-origin sheets temporarily, then deliver their real load event.
   await page.evaluate(()=>Object.defineProperty(document,'styleSheets',{configurable:true,get:()=>[]}));
   // Acknowledge exactly as the production native host does; other prompts fail.
   await page.evaluate(()=>{window.prompt=(message,payload)=>message.startsWith('lovest-shell:')&&JSON.parse(payload).action==='ready'?'ready':null;});
   await page.evaluate(mobile);await page.evaluate(owned);
   assert.equal(await page.evaluate(()=>!!window.LovestShell),false,'Owned shell waits for a usable stylesheet');
   await page.evaluate(()=>{delete document.styleSheets;document.querySelector('link[rel=stylesheet]').dispatchEvent(new Event('load',{bubbles:false}));});
   await page.waitForFunction(()=>window.LovestShell?.version===1);
   const installed=await page.evaluate(()=>({shell:document.documentElement.classList.contains('lovest-shell'),mobile:document.documentElement.classList.contains('bridge-mobile'),rules:[...document.styleSheets].filter(s=>s.href&&new URL(s.href).origin===location.origin).flatMap(s=>[...s.cssRules].map(r=>r.cssText)).filter(r=>r.includes('.lovest-shell')||r.includes('.bridge-mobile')).length}));
   assert(installed.shell&&installed.mobile&&installed.rules>25,'Native layout installs both shared and owned CSS after delayed load');
   await page.evaluate(()=>document.querySelector('link[rel=stylesheet]').dispatchEvent(new Event('load')));await page.evaluate(owned);
   const after=await page.evaluate(()=>[...document.styleSheets].filter(s=>s.href&&new URL(s.href).origin===location.origin).flatMap(s=>[...s.cssRules].map(r=>r.cssText)).filter(r=>r.includes('.lovest-shell')||r.includes('.bridge-mobile')).length);
   assert.equal(after,installed.rules,'Repeated load does not duplicate native CSS');
   await page.evaluate(()=>{window.qaCopied=[];window.prompt=(command,payload)=>{if(command.startsWith('codexbridge-copy:')){qaCopied.push(payload);return 'copied';}return null;};});
   await page.evaluate(clipboard);
   await page.evaluate(()=>{window.qaMenu=new SessionMenu({notify:message=>window.qaCopyFeedback=message});qaMenu.show({title:'Copy regression',link:'codex://threads/12345678-1234-4234-9234-123456789012',pin:async()=>{}},{element:document.getElementById('clients-button')});});
   await page.locator('dialog.session-menu .message-copy').click();
   await page.waitForFunction(()=>qaCopyFeedback==='已复制'&&!qaMenu.box);
   assert.deepEqual(await page.evaluate(()=>qaCopied),['codex://threads/12345678-1234-4234-9234-123456789012'],'First trusted click reaches the native clipboard exactly once');
   assert.deepEqual(errors,[]);reports.push({width,imageSave:'PASS',workspaceImageSave:'PASS',delayedNativeStyles:'PASS',firstNativeCopy:'PASS'});await context.close();
  }
  await fs.writeFile(path.join(out,'report.json'),JSON.stringify({status:'PASS',reports,realSendCalls:0},null,2));console.log('PASS production reading state, original-image controls, and delayed native CSS at 390/1280px');
 }finally{if(browser)await browser.close();child.kill();}
})().catch(error=>{console.error(error);process.exitCode=1;});
