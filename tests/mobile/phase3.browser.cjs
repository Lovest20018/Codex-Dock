'use strict';
// Exercise the production browser controls with a synthetic local gateway.
const assert=require('node:assert/strict'),path=require('node:path'),fs=require('node:fs/promises');
const {spawn,execFileSync}=require('node:child_process'),{chromium}=require('playwright-core');
const root=path.resolve(__dirname,'../..');
const python=process.env.CODEX_DOCK_TEST_PYTHON||'python';
const stamp=Date.UTC(2026,10,18,9,17);

(async()=>{
  const script="import json,sys; from pathlib import Path; p=Path('src/CodexMobile/upstream'); sys.path.insert(0,str(p)); from bridge.api.visualize import shell_bytes,SHELL_CSP; print(json.dumps({'html':shell_bytes(p/'web').decode(),'csp':SHELL_CSP}))";
  const shell=JSON.parse(execFileSync(python,['-B','-c',script],{cwd:root,encoding:'utf8',windowsHide:true}));
  const server=spawn(python,['-B','tests/mobile/upstream_browser_fixture.py'],{cwd:root,windowsHide:true,stdio:['ignore','pipe','inherit']});
  let browser;
  try{
    const origin=await new Promise((resolve,reject)=>{
      let stdout='';const timer=setTimeout(()=>reject(Error('Local test gateway timed out')),15000);
      server.stdout.on('data',chunk=>{stdout+=chunk;if(stdout.includes('\n')){clearTimeout(timer);resolve(stdout.split('\n')[0].trim());}});
      server.once('exit',code=>{clearTimeout(timer);reject(Error('Local test gateway exited '+code));});
    });
    browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe'});
    const output=process.env.CODEX_DOCK_TEST_OUTPUT||path.join(root,'artifacts/mobile-phase3-20261118/browser');
    await fs.mkdir(output,{recursive:true});
    for(const [width,language] of [[390,'zh'],[1280,'en']]){
      const page=await browser.newPage({viewport:{width,height:844}});
      const errors=[];page.on('pageerror',error=>errors.push(error.message));
      const chart='<button id="increment">Change</button><button id="ask">Ask</button><output id="count"></output><script>'+
        'const out=document.querySelector("#count"); const paint=()=>out.textContent=window.openai.widgetState?.count??0; paint(); document.querySelector("#increment").onclick=async()=>{await window.openai.setWidgetState({count:Number(out.textContent)+1});paint();};document.querySelector("#ask").onclick=()=>window.openai.sendFollowUpMessage({prompt:"chart follow-up"});<\/script>';
      await page.route('**/*',route=>{
        const url=new URL(route.request().url());
        if(url.origin!==origin)return route.abort();
        if(url.pathname==='/visualize-shell')return route.fulfill({contentType:'text/html',headers:{'Content-Security-Policy':shell.csp},body:shell.html});
        if(/\/visualizations\/[a-f0-9]{64}$/.test(url.pathname))return route.fulfill({json:{text:chart,revision:'b'.repeat(64)}});
        return route.continue();
      });
      await page.goto(origin+'/?case=session-menu');
      await page.waitForFunction(()=>document.getElementById('bootstrap-status')?.hidden&&!document.getElementById('app').hidden);
      await page.evaluate(async({language,stamp})=>{
        await BridgeFeatures.load('chat');BridgeI18n.setLanguage(language);
        draftGateway='chart-fixture-gateway';currentId='12345678-1234-4234-9234-123456789012';currentHost='local';
        state={id:currentId,cwd:'D:/fixture',access:{canSend:true,canChangeModel:true}};
        $('welcome').hidden=true;$('chat').hidden=false;$('app').classList.add('chat-open');
        codexDraftScope=null;bindCodexDraft({draftAccountIdentity:'fixture-account'});
        $('message').disabled=false;$('message').value='';
        window.qaOpenedModel=0;$('model-button').hidden=false;$('model-button').disabled=false;
        $('model-button').onclick=()=>{window.qaOpenedModel++;};
        const path='D:/fixture/chart.html',id='a'.repeat(64);
        window.qaPaint=()=>{
          qaOwner?.remove();
          const row=document.createElement('div');row.className='message assistant';
          const body=document.createElement('div');body.className='message-body';row.append(body);$('messages').append(row);
          body.__visualizeAssistant=true;body.__visualizeContext={key:()=>JSON.stringify(codexDraftScope),url:()=>sessionUrl(currentId,'visualizations/'+id),canCompose:()=>true};
          renderMarkdown(body,'\ue200visualize\ue202'+JSON.stringify({path,title:'Fixture chart'})+'\ue201',[{id,reference:path,visualization:true}],()=>{throw Error('unexpected file URL');});
          window.qaOwner=row;
        };
        window.qaOwner=null;qaPaint();
        renderChatMessageTime(document.querySelector('.message.assistant'),stamp,new Date(stamp));
      },{language,stamp});
      const chartFrame=page.frameLocator('.visualize-stage iframe');
      await chartFrame.locator('#count').waitFor();
      assert.equal(await page.locator('.visualize-stage iframe').getAttribute('sandbox'),'allow-scripts');
      await chartFrame.locator('#increment').click();
      await page.waitForFunction(()=>localStorage.length>0);
      await chartFrame.locator('#count').filter({hasText:'1'}).waitFor();
      await page.locator('.visualize-toolbar button').last().click();
      await page.locator('dialog.visualize-dialog[open]').waitFor();
      if(width===390)await page.setViewportSize({width:844,height:390});
      await chartFrame.locator('#count').filter({hasText:'1'}).waitFor();
      await page.locator('.visualize-closebar button').click();
      await page.locator('dialog.visualize-dialog[open]').waitFor({state:'hidden'});
      if(width===390)await page.setViewportSize({width,height:844});
      await page.evaluate(()=>qaPaint());
      await chartFrame.locator('#count').filter({hasText:'1'}).waitFor();
      await chartFrame.locator('#ask').click();
      await page.locator('.visualize-followup button').click();
      assert.equal(await page.locator('#message').inputValue(),'chart follow-up');
      const command=await page.evaluate(()=>{
        const input=$('message');input.focus();input.value='/model';input.dispatchEvent(new Event('input',{bubbles:true}));
        input.dispatchEvent(new CompositionEvent('compositionstart'));
        input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',isComposing:true,bubbles:true,cancelable:true}));
        const during=window.qaOpenedModel;input.dispatchEvent(new CompositionEvent('compositionend'));
        const menu=codexCommands.menu.textContent;
        input.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true,cancelable:true}));
        return {during,after:window.qaOpenedModel,menu};
      });
      assert.equal(command.during,0);
      assert.equal(command.after,1);
      assert(command.menu.includes('/model'));
      const creation=await page.evaluate(async()=>{
        const dialog=$('new-chat-dialog'),project=$('new-chat-project'),title=$('new-chat-title');
        dialog.showModal();project.replaceChildren(new Option('fixture','local|independent'));project.value='local|independent';
        $('new-chat-type').value='desktop';title.value='';
        const requests=[],fetchOriginal=window.fetch;
        window.fetch=async(url,options)=>{
          const target=String(url);
          if(target==='/api/sessions'&&options?.method==='POST'){requests.push(JSON.parse(options.body));throw Error('fixture response lost');}
          if(target.startsWith('/api/sessions/creation-status/')){requests.push('receipt');return new Response(JSON.stringify({status:'unknown'}),{status:200});}
          return fetchOriginal(url,options);
        };
        try{
          const submit=()=>$('new-chat-form').onsubmit({preventDefault(){}});
          const first=submit();await submit();await first;await submit();
          const pending=JSON.parse(localStorage.getItem(newChatAttemptKey())||'null');
          return {valid:title.checkValidity(),requests,pendingId:pending?.id};
        }finally{window.fetch=fetchOriginal;dialog.close();}
      });
      assert.equal(creation.valid,true);
      assert.equal(creation.requests.length,2);
      assert.equal(creation.requests[0].title,'');
      assert.equal(creation.requests[1],'receipt');
      assert.equal(creation.pendingId,creation.requests[0].id);
      await page.screenshot({path:path.join(output,'chat-visualization-'+width+'.png')});
      assert.deepEqual(errors,[]);
      console.log('PASS '+language+' '+width+'px: chart state, full screen, draft, IME and creation receipt');
      await page.close();
    }
  }finally{
    if(browser)await browser.close();
    server.kill();
  }
})().catch(error=>{console.error(error);process.exitCode=1;});
