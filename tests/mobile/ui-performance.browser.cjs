'use strict';
// Same production gateway and synthetic rows, with/without the appended Dock UI.
// Measures local render+layout only; no native Codex or external model request.
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),http=require('node:http'),zlib=require('node:zlib'),{spawn}=require('node:child_process');
const {chromium}=require('playwright-core'),root=path.resolve(__dirname,'../..');
function proxy(request,port){return new Promise((resolve,reject)=>{const headers={...request.headers(),host:'fixture.invalid'},body=request.postDataBuffer();delete headers['content-length'];if(body)headers['content-length']=body.length;const req=http.request({host:'127.0.0.1',port,path:new URL(request.url()).pathname+new URL(request.url()).search,method:request.method(),headers},res=>{const parts=[];res.on('data',b=>parts.push(b));res.on('end',()=>resolve({status:res.statusCode,headers:res.headers,body:Buffer.concat(parts)}));});req.on('error',reject);req.end(body);});}
(async()=>{
 const child=spawn(process.env.CODEX_DOCK_TEST_PYTHON||'C:/Users/com/AppData/Local/Python/pythoncore-3.12-64/python.exe',['-B','tests/mobile/manager_fixture.py'],{cwd:root,windowsHide:true});let browser;
 try{
  const config=await new Promise((resolve,reject)=>{let text='';child.stdout.on('data',b=>{text+=b;if(text.includes('\n'))resolve(JSON.parse(text.split('\n')[0]));});child.once('exit',code=>reject(Error('fixture '+code)));});
  browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});const results=[];
  for(const mode of ['baseline','dock']){
   const context=await browser.newContext({viewport:{width:1440,height:1000}}),page=await context.newPage();
   await page.route('https://fixture.invalid/**',async route=>{const result=await proxy(route.request(),config.publicPort),name=new URL(route.request().url()).pathname;
    if(mode==='baseline'&&['/app.js','/style.css'].includes(name)){
     if(result.headers['content-encoding']==='gzip')result.body=zlib.gunzipSync(result.body);
     else assert(!result.headers['content-encoding'],'unsupported content encoding');
     delete result.headers['content-encoding'];const suffix=await fs.readFile(path.join(root,'src/CodexMobile/ui',name==='/app.js'?'dock-ui.js':'dock-ui.css'));
     assert(result.body.subarray(-suffix.length).equals(suffix),'baseline removes exact Dock suffix');result.body=result.body.subarray(0,-suffix.length);
    }
    const headers=Object.fromEntries(Object.entries(result.headers).filter(([key])=>!['transfer-encoding','content-length','connection','set-cookie'].includes(key)).map(([key,value])=>[key,String(value)]));if(result.headers['set-cookie'])headers['set-cookie']=result.headers['set-cookie'].join('\n');await route.fulfill({...result,headers});
   });
   await page.goto('https://fixture.invalid/');await page.locator('#login-button').waitFor();await page.locator('#username').fill('fixture');await page.locator('#password').fill('isolated-manager-fixture');await page.locator('#login-button').click();await page.locator('#app').waitFor();await page.waitForFunction(()=>!clientNavigation.loading);
   assert.equal(await page.evaluate(()=>!!window.DockUI),mode==='dock');
   const measurements=await page.evaluate(async()=>{
    clearInterval(clientNavigation.timer);clearInterval(clientNavigation.connectingTimer);await BridgeFeatures.load('chat');
    const median=s=>[...s].sort((a,b)=>a-b)[Math.floor(s.length/2)],measure=async action=>{const values=[];for(let i=0;i<8;i++){await new Promise(r=>requestAnimationFrame(r));const start=performance.now();action();document.body.getBoundingClientRect();if(i)values.push(performance.now()-start);}return {medianMs:median(values),samplesMs:values};};
    const result={};listReadFinished=true;listComplete=true;listHasMore=false;
    for(const count of [100,1000]){listRows=Array.from({length:count},(_,i)=>({id:String(i).padStart(36,'0'),host:'local',title:'Synthetic chat '+i,projectKey:'local|fixture',projectName:'Fixture',recency:Date.now()-i,access:{source:'desktop',canSend:true}}));result['sessions'+count]=await measure(()=>{listRenderStamp='';renderList();});if(document.querySelectorAll('#sessions .session').length!==count)throw Error('List did not render');}
    $('welcome').hidden=true;$('chat').hidden=false;currentId='12345678-1234-4234-9234-123456789012';currentHost='local';workbench.open(currentId,currentHost);
    const timeline=new ChatTimeline({url:()=>'',request:()=>{throw Error('Unexpected timeline request');},renderMeta(){},renderText:(node,text)=>renderMarkdown(node,text,[],()=>''),status(){},canEdit:()=>false});
    const rows=Array.from({length:500},(_,i)=>({key:'synthetic-'+i,role:i%2?'assistant':'user',order:i,version:1,text:'Synthetic message '+i+'\n\nA paragraph with **formatting** and a `code` sample. '.repeat(4)}));
    result.messages500=await measure(()=>timeline.apply({rows,sequence:1,epoch:'synthetic',meta:{requests:[]},hasMore:false,files:[]},true));if(timeline.nodes.size!==500)throw Error('Timeline did not render');timeline.dispose();return result;
   });results.push({mode,measurements});await context.close();
  }
  const report={status:'PASS',method:'1440x1000 Chromium; 1 warmup + 7 render/layout samples, synthetic data, exact Dock suffix removed for baseline; local timings exclude network/native latency',results,realSendCalls:0};await fs.writeFile(path.join(root,'artifacts/ui-performance.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }finally{if(browser)await browser.close();child.stdin.end('\n');child.kill();}
})().catch(error=>{console.error(error);process.exitCode=1;});
