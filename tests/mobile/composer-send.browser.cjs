'use strict';
// Production composer with isolated transport; never sends a real Codex message.
const assert=require('node:assert/strict'),path=require('node:path'),{spawn}=require('node:child_process');
const root=path.resolve(__dirname,'../..'),{chromium}=require('playwright-core');
(async()=>{
  const child=spawn(process.env.CODEX_DOCK_TEST_PYTHON||'C:/Users/com/AppData/Local/Python/pythoncore-3.12-64/python.exe',['-B','tests/mobile/upstream_browser_fixture.py','--port','0'],{cwd:root,windowsHide:true});
  let browser;
  try{
    const url=await new Promise((resolve,reject)=>{let data='';child.stdout.on('data',chunk=>{data+=chunk;if(data.includes('\n'))resolve(data.split('\n')[0].trim());});child.once('exit',code=>reject(Error('Fixture exited '+code)));});
    browser=await chromium.launch({executablePath:process.env.CODEX_DOCK_TEST_CHROME||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
    const cases=['baseline','caret','new-text','changed-back','quote','skill','mode','state-refresh','receipt-race','provisional','provisional-caret','provisional-new-text','provisional-changed-back','provisional-receipt','provisional-receipt-new-text','provisional-receipt-same-text','account-switch','chat-switch','unknown','provisional-unknown','unbound','unbound-caret','unbound-new-text','unbound-changed-back','unbound-unknown'];
    for(const kind of cases){
      const page=await browser.newPage();
      await page.route('**/*',route=>new URL(route.request().url()).origin===url?route.continue():route.abort());
      await page.goto(url+'/?case=success');await page.waitForFunction(()=>document.getElementById('bootstrap-status')?.hidden);
      const result=await page.evaluate(async kind=>{
        await BridgeFeatures.load('chat');
        draftGateway='composer-regression-'+crypto.randomUUID();currentHost='local';currentId=crypto.randomUUID();
        codexDraftScope=null;codexDraftTouched=false;codexQuotes.reset();$('message').value='';
        workModes.open(chatKey());workModes.selection='default';$('send-mode').value='send';selectedSkills.clear();attachments.open(chatKey());
        const unbound=kind.startsWith('unbound'),provisional=unbound||kind.startsWith('provisional');
        if(!provisional)bindCodexDraft({draftAccountIdentity:'account-one'});
        const initialScope=codexDraftScope;state={status:'idle',access:{source:'desktop',canSend:true}};
        let finish,submitted,calls=0;const originalFetch=window.fetch;
        window.fetch=async(url,options)=>{
          if(String(url).includes('/send?')){calls++;submitted=JSON.parse(options.body);return new Promise(resolve=>finish=()=>resolve({ok:true,status:200,json:async()=>({status:kind.endsWith('unknown')?'unknown':'accepted'})}));}
          return originalFetch.call(window,url,options);
        };
        const edit=text=>{$('message').value=text;$('message').dispatchEvent(new Event('input',{bubbles:true}));};
        edit('review下');const pending=$('composer').onsubmit({preventDefault(){}});
        while(!finish)await new Promise(resolve=>setTimeout(resolve,5));
        if(kind.endsWith('caret')){$('message').setSelectionRange(0,2);$('message').dispatchEvent(new Event('keyup',{bubbles:true}));}
        if(kind.endsWith('new-text')&&!kind.includes('receipt'))edit('next draft');
        if(kind.endsWith('changed-back')){edit('next draft');edit('review下');}
        if(kind==='quote'){codexQuotes.quotes=[{text:'new quote',source:'',provider:'codex'}];saveDraft();}
        if(kind==='skill'){selectedSkills.add('new-skill');saveDraft();}
        if(kind==='mode'){$('send-mode').value='queue';saveDraft();}
        if(kind==='state-refresh')workModes.render({...state,status:'active'},'send',true);
        if(provisional&&!unbound)bindCodexDraft({draftAccountIdentity:'account-one'});
        const sentScope=initialScope||codexDraftScope||{gateway:draftGateway,provider:'codex',account:'account-one',host:currentHost,session:currentId};
        if(kind==='account-switch'){bindCodexDraft({draftAccountIdentity:'account-two'});edit('other account');}
        if(kind==='chat-switch'){saveDraft();currentId=crypto.randomUUID();codexDraftScope=null;codexDraftTouched=false;bindCodexDraft({draftAccountIdentity:'account-one'});edit('other chat');}
        if(kind.endsWith('receipt')||kind==='receipt-race'||kind.startsWith('provisional-receipt-'))messageManagementChanged(await composerDrafts.reconcilePending(sentScope,{scope:sentScope,receipts:[{id:submitted.id,status:'accepted'}]},{temporary:sessionStorage,temporaryKey:'pending:'+sentScope.host+'|'+sentScope.session}));
        if(kind.startsWith('provisional-receipt-'))edit(kind.endsWith('new-text')?'next draft':'review下');
        finish();await pending;
        const stored=unbound?codexProvisionalDrafts.get(chatKey()):composerDrafts.load(sentScope),pendingRecords=composerDrafts.pendingRecords(sentScope,{temporary:sessionStorage});
        return {text:$('message').value,stored,pending:pendingRecords.length,calls,activeStored:composerDrafts.load(codexDraftScope),quotes:codexQuotes.quotes,skills:[...selectedSkills],error:$('send-error').textContent};
      },kind);
      const keep=['new-text','changed-back','quote','skill','mode','provisional-new-text','provisional-changed-back','provisional-receipt-new-text','provisional-receipt-same-text','unknown','provisional-unknown','unbound-new-text','unbound-changed-back','unbound-unknown'].includes(kind);
      assert.equal(result.calls,1,kind+': one request');
      assert.equal(!!result.stored,keep,kind+': submitted draft cleanup');
      assert.equal(result.pending,kind.endsWith('unknown')?(kind==='unknown'?1:2):0,kind+': uncertain delivery retained');
      if(kind==='account-switch'||kind==='chat-switch'){
        assert.equal(result.text,kind==='account-switch'?'other account':'other chat',kind);
        assert.equal(result.activeStored.text,result.text,kind+': active draft intact');
      }else if(keep){
        assert.equal(result.text,kind.endsWith('new-text')?'next draft':'review下',kind);
      }else assert.equal(result.text,'',kind+': input cleared');
      if(kind==='quote')assert.equal(result.quotes.length,1);
      if(kind==='skill')assert.deepEqual(result.skills,['new-skill']);
      console.log('PASS '+kind);await page.close();
    }
    console.log('PASS '+cases.length+' production composer scenarios');
    const page=await browser.newPage();
    await page.route('**/*',route=>new URL(route.request().url()).origin===url?route.continue():route.abort());
    await page.goto(url+'/?case=session-menu');await page.waitForFunction(()=>document.getElementById('bootstrap-status')?.hidden&&!document.getElementById('app').hidden&&!clientNavigation.loading);
    const status=await page.evaluate(async()=>{
      const nav=clientNavigation;clearInterval(nav.timer);clearInterval(nav.connectingTimer);
      const clients=nav.clients,notifications=[];nav.notify=text=>notifications.push(text);nav.statusRetryAt=0;nav.statusFailure='';nav.readNetworkAdaptation=()=>{};
      nav.request=async()=>{throw Error('客户端状态读取超时，请重试。');};
      await nav.poll();for(let i=0;i<3;i++){nav.statusRetryAt=0;await nav.poll();}
      const passive=notifications.length;nav.openManager();while(nav.loading)await new Promise(resolve=>setTimeout(resolve,5));
      const visible=!nav.manager.status.hidden&&nav.manager.status.textContent.includes('超时')&&!nav.manager.retry.hidden;
      const preserved=JSON.stringify(nav.clients)===JSON.stringify(clients);
      nav.request=async()=>({clients,pending:false});nav.manager.retry.click();while(nav.loading)await new Promise(resolve=>setTimeout(resolve,5));
      return {passive,visible,preserved,recovered:nav.statusFailure===''&&nav.manager.status.hidden&&nav.manager.retry.hidden};
    });
    assert.deepEqual(status,{passive:1,visible:true,preserved:true,recovered:true});
    console.log('PASS repeated status outage and real management retry UI');await page.close();
  }finally{if(browser)await browser.close();child.kill();}
})().catch(error=>{console.error(error);process.exitCode=1;});
