/* Instrumentation-only: real production HTTP/IPC with a disposable native owner. */
(() => {
  const get=id=>document.getElementById(id),qa=window.SteeringQA={ready:false,error:'',requests:[]};
  qa.visible=()=>[get('message'),get('send')].every(e=>{const r=e.getBoundingClientRect();return r.width>20&&r.height>=36&&r.top>=visualViewport.offsetTop-1&&r.bottom<=visualViewport.offsetTop+visualViewport.height+2;})&&!get('composer-body').hidden;
  qa.edit=text=>{get('message').value=text;get('message').dispatchEvent(new Event('input',{bubbles:true}));};
  qa.poll=async()=>qa.server=await(await fetch('/__qa__/status')).json();
  qa.finish=async()=>{await fetch('/__qa__/finish',{method:'POST'});};
  qa.refresh=async()=>renderState(await api(sessionUrl(currentId,'',currentHost)));
  qa.selectSteer=()=>{get('send-mode').value='steer';get('send-mode').dispatchEvent(new Event('change',{bubbles:true}));};
  (async()=>{
    await BridgeFeatures.load('chat');
    const fixture=await(await fetch('/__qa__/status')).json();
    draftGateway='android-steer-regression-'+crypto.randomUUID();currentHost='local';currentId=fixture.threadId;
    codexDraftScope=null;codexDraftTouched=false;codexQuotes.reset();get('message').value='';
    workModes.open(chatKey());workModes.selection='default';get('send-mode').value='send';selectedSkills.clear();invalidSkills.clear();attachments.open(chatKey());
    const originalFetch=window.fetch;
    window.fetch=async(url,options)=>{
      const item=String(url).includes('/send?')?{body:JSON.parse(options.body),status:0,response:null}:null;
      if(item)qa.requests.push(item);
      const response=await originalFetch.call(window,url,options);
      if(item){item.response=await response.clone().json();item.status=response.status;}
      return response;
    };
    get('app').classList.add('chat-open');get('chat').hidden=false;get('welcome').hidden=true;
    get('messages').replaceChildren();get('dock-chat-context').textContent='隔离模拟器 · 真实 HTTP/IPC · 无模型任务';
    window.BridgePresentation?.openChat();get('dock-composer-more').open=true;workbench.open(currentId,currentHost);
    for(let attempt=0;attempt<50;attempt++){
      await qa.refresh();if(state.connected&&state.status==='active')break;
      await new Promise(resolve=>setTimeout(resolve,100));
    }
    if(!state.connected||state.status!=='active')throw Error('Native fixture not active: '+JSON.stringify(state));
    qa.selectSteer();await qa.poll();qa.ready=true;
  })().catch(error=>qa.error=String(error.stack||error));
  return true;
})()
