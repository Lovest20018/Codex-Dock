/* Isolated display data, executed only by the separate instrumentation APK. */
(() => {
  const get=id=>document.getElementById(id);
  const names=['优化移动端聊天布局','检查网关连接状态','整理项目发布说明','为终端增加文件预览','继续上次的开发任务'];
  const initial={rows:listRows,read:listReadFinished,complete:listComplete,state,models:catalogData,results:catalogResults.models};
  window.LovestPresentationQA={
    list(){
      listRows=names.map((title,index)=>({id:'fixture-'+index,host:'local',title,projectKey:'local|demo',projectName:index%2?'Lovest Code':'移动工作台',hostLabel:'此电脑',recency:Date.now()-index*3600000,access:{kind:'app'}}));
      listReadFinished=true;listComplete=true;listRenderStamp='';renderList();get('app').classList.remove('chat-open');get('host-errors').textContent='';get('host-errors').hidden=true;
    },
    chat(){
      get('app').classList.add('chat-open');get('chat').hidden=false;get('welcome').hidden=true;
      get('chat-title').textContent='优化移动端聊天布局与输入体验';
      get('dock-chat-context').textContent='D:\\Projects\\LovestCode · 此电脑';
      get('status').textContent='已连接';get('model-name').textContent='gpt-6.1-sol';get('model-effort').textContent=' · xhigh';get('model-effort').hidden=false;
      get('message').value='';get('message').oninput();
      get('messages').innerHTML='<article class="message user"><span class="who">你</span>把手机上的聊天页面整理得更清楚一些。</article><article class="message assistant"><span class="who">Codex</span><p>已调整聊天页的布局，继续保留常用操作。</p><ul><li>标题和项目路径分成两行。</li><li>模型和高级选项保持随手可用。</li><li>输入区留出更多阅读空间。</li></ul><p>你可以先查看页面，再继续下一步开发。</p></article>';
      get('timeline').scrollTop=get('timeline').scrollHeight;
      get('dock-composer-more').open=false;
    },
    models(){
      catalogData={fastMode:{allowed:true,provider:'openai'},models:[['gpt-6.1-sol','GPT-6.1-Sol'],['gpt-6-astra','GPT-6-Astra'],['gpt-6.1-terra','GPT-6.1 Terra']].map(([id,name])=>({id,name,efforts:['low','medium','high','xhigh','max','ultra'],defaultEffort:'xhigh',fastTier:'priority'}))};
      catalogResults.models={modelSource:'api'};state={...state,provider:'openai',connected:true,effort:'xhigh'};
      get('model-select').replaceChildren(...catalogData.models.map(m=>new Option(m.name,m.id)),new Option('自定义模型…','__custom__'));
      get('model-select').value='gpt-6.1-sol';fastModeControl.open(catalogData,state,'gpt-6.1-sol');renderModelChoices();renderEfforts();
      get('model-updated').textContent='更新于：2026/10/31 18:30';get('model-error').textContent='';get('model-dialog').showModal();
    },
    restore(){listRows=initial.rows;listReadFinished=initial.read;listComplete=initial.complete;state=initial.state;catalogData=initial.models;catalogResults.models=initial.results;listRenderStamp='';renderList();}
  };
  return true;
})()
