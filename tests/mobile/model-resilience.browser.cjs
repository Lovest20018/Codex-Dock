'use strict';
// Exercise the production composer against a synthetic gateway, never a real account.
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{spawn}=require('node:child_process');
const {chromium}=require('playwright-core');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/mobile-backport-2026-11-15/browser');
(async()=>{
 await fs.mkdir(out,{recursive:true});
 const child=spawn(process.env.CODEX_DOCK_TEST_PYTHON||'C:/Users/com/AppData/Local/Python/pythoncore-3.12-64/python.exe',['-B','tests/mobile/upstream_browser_fixture.py'],{cwd:root,windowsHide:true,stdio:['ignore','pipe','inherit']});
 let browser;
 try{
  const url=await new Promise((resolve,reject)=>{let value='';const timer=setTimeout(()=>reject(Error('Fixture startup timeout')),20000);child.stdout.on('data',data=>{value+=data;if(value.includes('\n')){clearTimeout(timer);resolve(value.split('\n')[0].trim());}});child.once('exit',code=>{clearTimeout(timer);reject(Error('Fixture exited '+code));});});
  browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  const reports=[];
  for(const width of [390,1280]){
   const context=await browser.newContext({viewport:{width,height:844}}),page=await context.newPage(),errors=[];
   page.on('pageerror',error=>errors.push(error.message));
   await context.route('**/*',route=>new URL(route.request().url()).origin===url?route.continue():route.abort());
   await page.goto(url+'/?case=success');await page.waitForFunction(()=>document.getElementById('bootstrap-status')?.hidden);
   const result=await page.evaluate(async width=>{
    const checks=[],originalFetch=window.fetch;
    const check=(value,label)=>{if(!value)throw Error(label);checks.push(label);};
    const deferred=()=>{let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});return {promise,resolve,reject};};
    const response=(data,status=200)=>({ok:status===200,status,json:async()=>data});
    const accountA='a'.repeat(64),accountB='b'.repeat(64),id='11111111-1111-4111-8111-111111111111';
    const catalog=(identity,model,updatedAt)=>({models:[{id:model,name:model,efforts:['low','high']}],modelSource:'api',cache:{identity,updatedAt}});
    let next=null,reads=0;const writes=[];
    window.fetch=async(url,options)=>{
     if(String(url).includes('/catalog?kind=models')){reads++;return next?next.promise:response(catalog(accountA,'model-a',1000));}
     if(String(url).includes('/settings?')){writes.push(JSON.parse(options.body));return response({applied:true,confirmed:true});}
     return originalFetch(url,options);
    };
    currentId=id;currentHost='local';listIdentity='fixture-gateway';state={connected:true,provider:'chatgpt',model:'model-a',effort:'low',modelCatalogIdentity:accountA};
    if(width===1280)BridgeI18n.setLanguage('en');
    const input=(id,value,event='input')=>{$(id).value=value;$(id).dispatchEvent(new Event(event,{bubbles:true}));};
    await openModelDialog();check(!!$('model-options').querySelector('input[value="model-a"]'),'Initial catalog renders');const timestamp=$('model-updated').textContent;
    input('model-select','__custom__','change');input('custom-model','manual-before');input('effort-select','high','change');
    next=deferred();const pending=openModelDialog(true);
    input('custom-model','手动/manual-after');input('effort-select','high','change');
    next.resolve(response(catalog(accountA,'model-a',2000)));await pending;
    check($('model-select').value==='__custom__'&&$('custom-model').value==='手动/manual-after','Delayed success preserves manual model typed during refresh');
    check($('effort-select').value==='high','Delayed success preserves chosen effort');
    next=deferred();const failed=openModelDialog(true);next.reject(Error('Synthetic temporary discovery failure'));await failed;
    check($('custom-model').value==='手动/manual-after'&&$('effort-select').value==='high','Discovery failure preserves model draft');
    check(!!$('model-options').querySelector('input[value="model-a"]'),'Discovery failure keeps last successful rows');
    check(!$('model-save').disabled,'Verified manual draft remains editable after failure');
    next=deferred();const empty=openModelDialog(true);next.resolve(response({models:[],modelSource:'codex',modelError:'Synthetic empty response',cache:{identity:accountA,state:'unavailable'}}));await empty;
    check($('custom-model').value==='手动/manual-after','Empty discovery preserves manual model');
    check($('model-updated').textContent.includes('Synthetic empty response'),'Empty discovery gives explicit feedback');
    $('model-dialog').close();state={...state,modelCatalogIdentity:accountB,model:'model-b'};next=deferred();const switched=openModelDialog();
    check(!$('model-options').querySelector('input[value="model-a"]'),'Other account cannot paint previous catalog');
    next.resolve(response({models:[],modelError:'Synthetic unavailable',cache:{identity:accountB,state:'unavailable'}}));await switched;
    check($('model-select').value==='__custom__'&&$('custom-model').value==='model-b','First failure retains native current model for manual entry');
    check($('effort-select').querySelector('option[value=""]').textContent===(width===1280?'Keep native default':'沿用原生默认'),'Native default effort follows the selected language');
    input('custom-model','manual-b');input('effort-select','','change');
    await $('model-form').onsubmit({preventDefault(){}});
    check(writes.length===1&&writes[0].model==='manual-b'&&writes[0].modelCatalogIdentity===accountB&&!('effort' in writes[0]),'Explicit apply binds account and preserves native default effort');
    next=deferred();const changed=openModelDialog(true);next.resolve(response({error:'Synthetic account changed',code:'model_identity_changed'},409));await changed;
    check($('model-save').disabled,'Identity change disables model apply');input('custom-model','cannot-apply');
    await $('model-form').onsubmit({preventDefault(){}});check(writes.length===1,'Typing cannot bypass identity-change guard');
    next=deferred();const late=openModelDialog();state={...state,modelCatalogIdentity:accountA};next.resolve(response(catalog(accountB,'must-not-paint',4000)));await late;
    check(!$('model-options').querySelector('input[value="must-not-paint"]'),'Late old-account response cannot repaint current account');
    check(!$('model-dialog').open,'Account change closes stale model dialog');
    state={...state,modelCatalogIdentity:accountA,model:'model-a'};next=null;await openModelDialog();
    check(document.querySelector('#model-dialog').scrollWidth<=document.querySelector('#model-dialog').clientWidth+1,'Model controls fit viewport');
    window.fetch=originalFetch;
    return {checks,passed:checks.length,reads,fixtureSettingsWrites:writes.length,realSendCalls:0};
   },width);
   assert(result.passed>=15);assert.deepEqual(errors,[]);await page.screenshot({path:path.join(out,'model-'+width+'.png')});reports.push({width,...result});await context.close();
  }
  await fs.writeFile(path.join(out,'model-report.json'),JSON.stringify({status:'PASS',reports},null,2));
  console.log('PASS production model refresh, draft and account isolation at 390/1280px');
 }finally{if(browser)await browser.close();child.kill();}
})().catch(error=>{console.error(error);process.exitCode=1;});
