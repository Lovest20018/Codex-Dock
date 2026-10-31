/* Runs only in the isolated instrumentation APK; transport is intercepted. */
(() => {
  const get=id=>document.getElementById(id),qa=window.ComposerQA={ready:false,calls:0,error:''};
  qa.metrics=()=>JSON.stringify({width:innerWidth,height:innerHeight,scrollY,scale:visualViewport.scale,visualHeight:visualViewport.height,offset:visualViewport.offsetTop,body:document.body.getBoundingClientRect().toJSON(),composer:get('composer').getBoundingClientRect().toJSON(),input:get('message').getBoundingClientRect().toJSON(),send:get('send').getBoundingClientRect().toJSON(),collapsed:get('composer-body').hidden});
  qa.visible=()=>[get('message'),get('send')].every(e=>{const r=e.getBoundingClientRect();return r.width>20&&r.height>=36&&r.top>=visualViewport.offsetTop-1&&r.bottom<=visualViewport.offsetTop+visualViewport.height+2;})&&!get('composer-body').hidden;
  qa.edit=text=>{get('message').value=text;get('message').dispatchEvent(new Event('input',{bubbles:true}));};
  qa.reply=()=>{for(let i=0;i<40;i++){const p=document.createElement('p');p.className='message assistant';p.textContent='隔离发送测试的长回复内容 '+i;get('messages').append(p);}get('timeline').scrollTop=get('timeline').scrollHeight;};
  qa.active=()=>renderState({...state,status:'active'});
  (async()=>{
    await BridgeFeatures.load('chat');
    draftGateway='android-composer-regression-'+crypto.randomUUID();currentHost='local';currentId=crypto.randomUUID();
    codexDraftScope=null;codexDraftTouched=false;codexQuotes.reset();get('message').value='';
    workModes.open(chatKey());workModes.selection='default';get('send-mode').value='send';selectedSkills.clear();invalidSkills.clear();attachments.open(chatKey());
    bindCodexDraft({draftAccountIdentity:'android-fixture-account'});
    state={status:'idle',connected:true,title:'发送与键盘回归测试',model:'gpt-6.1-sol',effort:'xhigh',requests:[],submissions:[],historyComplete:true,draftAccountIdentity:'android-fixture-account',access:{source:'desktop',canSend:true,canInterrupt:true}};
    const originalFetch=window.fetch;
    window.fetch=async(url,options)=>{
      if(String(url).includes('/send?')){qa.calls++;return {ok:true,status:200,json:async()=>({status:'accepted'})};}
      return originalFetch.call(window,url,options);
    };
    get('app').classList.add('chat-open');get('chat').hidden=false;get('welcome').hidden=true;
    get('chat-title').textContent='发送与键盘回归测试';get('dock-chat-context').textContent='隔离模拟器 · 无真实消息';
    get('messages').replaceChildren();qa.reply();window.BridgePresentation?.openChat();get('dock-composer-more').open=false;
    workbench.open(currentId,currentHost);renderState(state);qa.ready=true;
  })().catch(error=>qa.error=String(error.stack||error));
  return true;
})()
