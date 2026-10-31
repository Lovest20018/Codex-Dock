'use strict';
// Dock presentation only. Existing controls retain their business handlers.
(()=>{
  const get=id=>document.getElementById(id);
  const node=(tag,text,cls)=>{const n=document.createElement(tag);if(text)n.textContent=text;if(cls)n.className=cls;return n;};
  const button=(text,action)=>{const b=node('button',text,'plain');b.type='button';b.onclick=action;return b;};
  let ready=false,deliveryState=null,lastList={phase:'loading',count:0,error:''},settingsPage='appearance';
  function pendingBodies(){
    if(codexDraftScope)return composerDrafts.pendingRecords(codexDraftScope,{temporary:sessionStorage,temporaryKey:'pending:'+chatKey()}).map(row=>row.body);
    let value=composerDrafts.temporaryPending.get('pending:'+chatKey());try{value=JSON.parse(sessionStorage.getItem('pending:'+chatKey()))||value;}catch{}
    return value?.gateway===draftGateway&&value.target===chatKey()?[value.body||value]:[];
  }
  function sendLabel(){
    if(!ready)return;
    const text=typeof codexQuotes==='undefined'||!codexQuotes?get('message').value:codexQuotes.serialize(get('message').value);
    const check=deliveryState?.kind==='unknown'&&pendingBodies().some(body=>body.text===text);
    const label=check?'核对状态':'发送 ↑';if(get('send').textContent!==label)get('send').textContent=label;
  }
  function current(target,host,gateway,account){return target===currentId&&host===currentHost&&gateway===draftGateway&&account===codexDraftScope?.account;}
  function receipt(){
    if(!ready)return;
    const root=get('dock-delivery'),value=deliveryState;
    root.hidden=!value;root.dataset.state=value?.kind||'';
    get('dock-delivery-text').textContent=value?.text||'';
    get('dock-check-delivery').hidden=value?.kind!=='unknown';
    sendLabel();
  }
  function selections(){
    if(!ready)return;
    const text=[];
    if(get('send-mode').value!=='send')text.push(get('send-mode').selectedOptions[0]?.textContent);
    if(get('work-mode').value!=='default'&&!get('work-mode').disabled)text.push(get('work-mode').selectedOptions[0]?.textContent);
    const notify=get('notify-button');if(notify.textContent.trim()!=='提醒'&&notify.textContent.trim()!=='Notifications')text.push(notify.textContent);
    get('dock-selection').textContent=text.filter(Boolean).join(' · ');
  }
  function listView(){
    if(!ready)return;
    const {phase,count,error}=lastList,filtered=!!get('search').value.trim()||get('archived').checked||get('source-filter').value&&get('source-filter').value!=='all';
    const unavailable=phase==='error',empty=!count&&phase!=='loading';
    const title=phase==='loading'?'正在读取聊天…':phase==='disconnected'?'尚未启用 Codex 接入':unavailable?'聊天暂未更新':filtered?'没有匹配的聊天':'还没有聊天';
    const help=phase==='disconnected'?'请在连接与接入中检查并启用执行环境。':unavailable?error:filtered?'调整搜索或筛选条件后再试。':'新建聊天，或在连接与接入中检查执行环境。';
    const welcome=get('welcome');
    if(!currentId||get('chat').hidden){welcome.querySelector('h1').textContent=count?'从一条聊天继续':title;welcome.querySelector('p').textContent=count?'选择左侧聊天，查看进度或发送消息。':help;}
    const state=get('dock-list-state');state.hidden=count>0||(!empty&&phase!=='loading');
    get('sidebar').classList.toggle('dock-list-empty',!count);
    get('dock-list-title').textContent=title;get('dock-list-help').textContent=phase==='loading'?'正在读取所选环境，请稍候。':help.replace(/×\s*$/,'').trim();
    get('dock-list-retry').hidden=phase!=='error';get('dock-list-new').hidden=phase==='loading'||unavailable||filtered;get('dock-list-clear').hidden=!filtered||phase==='loading';
    get('dock-welcome-actions').hidden=count>0||phase==='loading';
    get('dock-filter-toggle').textContent='筛选 · '+[get('list-mode').selectedOptions[0]?.textContent,get('archived').checked?'已归档':'',get('source-filter-label').hidden?'':get('source-filter').selectedOptions[0]?.textContent].filter(Boolean).join(' · ');
  }
  function selectSettings(page){
    settingsPage=page;
    document.querySelectorAll('[data-dock-setting]').forEach(b=>{b.setAttribute('aria-selected',String(b.dataset.dockSetting===page));b.tabIndex=b.dataset.dockSetting===page?0:-1;});
    document.querySelectorAll('[data-dock-settings-panel]').forEach(p=>p.hidden=p.dataset.dockSettingsPanel!==page);
    get('appearance-reset').hidden=page!=='appearance';
    get('dock-settings-scope').textContent=page==='appearance'?'当前浏览器 · 即时生效':page==='notifications'?'通知策略作用于当前网关的聊天；每条聊天可单独覆盖。':'以下设置按各项标注的范围生效。';
  }
  function settings(){
    const dialog=get('appearance-dialog'),tabs=node('div',null,'dock-settings-tabs');tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','设置分类');
    const panels={};
    for(const [id,label] of [['appearance','外观与阅读'],['notifications','聊天与通知'],['connections','连接与诊断']]){
      const b=button(label,()=>selectSettings(id));b.dataset.dockSetting=id;b.id='dock-tab-'+id;b.setAttribute('role','tab');b.setAttribute('aria-controls','dock-settings-'+id);tabs.append(b);
      const panel=node('div');panel.id='dock-settings-'+id;panel.dataset.dockSettingsPanel=id;panel.setAttribute('role','tabpanel');panel.setAttribute('aria-labelledby',b.id);panels[id]=panel;
    }
    const original=[...dialog.children];
    for(const child of original){
      if(child.matches('.picker-head,.appearance-actions')||child.id==='mobile-binding-status')continue;
      const text=child.querySelector('legend')?.textContent||'';
      if(child.classList.contains('appearance-note')){child.hidden=true;continue;}
      const page=text.includes('全会话通知')?'notifications':text.includes('账号与接入')||text.includes('文件传输')||child.querySelector('#open-session-diagnostics')||child.classList.contains('settings-action-row')||child.classList.contains('file-transfer-settings')?'connections':'appearance';
      panels[page].append(child);
    }
    // Single-provider Dock has no application switcher preferences.
    clientNavigation.switchPreferences.hidden=true;clientNavigation.switchToggle.hidden=true;
    for(const id of ['appearance-clients','appearance-accounts','appearance-pushplus'])panels.appearance.querySelector('#'+id)?.closest('label')?.classList.add('dock-obsolete-shortcut');
    const transfer=dialog.querySelector('input[id*="threshold"]')?.closest('section');if(transfer)panels.connections.append(transfer);
    const actions=clientNavigation.settingsActions;panels.connections.append(actions);
    const notifications=node('p','当前网关所有聊天 · 更改后即时保存，重新读取后核对结果。','muted');notifications.setAttribute('role','status');panels.notifications.prepend(notifications);
    for(const id of ['notification-requests','notification-completions']){const input=panels.notifications.querySelector('#'+id);input.addEventListener('change',()=>{notifications.textContent='当前网关所有聊天 · 正在保存…';});new MutationObserver(()=>{if(!input.disabled)notifications.textContent=get('notification-settings-error').textContent?'通知策略保存未确认，请重新读取核对。':'当前网关所有聊天 · 已读取最新通知策略';}).observe(input,{attributes:true,attributeFilter:['disabled']});}
    for(const field of panels.connections.querySelectorAll('fieldset'))if(field.querySelector('legend')?.textContent.includes('文件传输'))field.append(node('p','当前网关的文件下载策略 · 点击保存后生效','muted'));
    const scope=node('p',null,'muted');scope.id='dock-settings-scope';scope.setAttribute('role','status');
    dialog.querySelector('.picker-head').after(tabs,scope,...Object.values(panels));
    const resetScope=node('p','当前网关所有设备 · 提交后等待保存结果','muted');get('appearance-reset-dynamic').closest('fieldset').append(resetScope);
    get('appearance-reset-dynamic').addEventListener('change',()=>{resetScope.textContent='当前网关所有设备 · 正在保存…';});
    const resetInput=get('appearance-reset-dynamic');new MutationObserver(()=>{if(!resetInput.disabled)resetScope.textContent=get('reset-dynamic-error').textContent?'当前网关所有设备 · 保存未确认，请刷新核对':'当前网关所有设备 · 已读取最新状态';}).observe(resetInput,{attributes:true,attributeFilter:['disabled']});
    get('appearance-reset').textContent='重置外观与阅读';get('appearance-reset').removeAttribute('data-i18n');
    get('appearance-reset').onclick=()=>{if(settingsPage==='appearance'&&confirm('将当前浏览器的外观与阅读设置恢复默认？'))window.BridgePresentation?.resetAppearance();};
    const diagnostics=get('open-session-diagnostics');diagnostics.textContent='连接诊断';diagnostics.removeAttribute('data-i18n');get('session-diagnostics-heading').textContent='连接诊断';get('session-diagnostics-heading').removeAttribute('data-i18n');
    tabs.addEventListener('keydown',e=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(e.key))return;e.preventDefault();const buttons=[...tabs.children],at=buttons.indexOf(document.activeElement),index=e.key==='Home'?0:e.key==='End'?2:(at+(e.key==='ArrowRight'?1:2))%3;buttons[index].click();buttons[index].focus();});
    selectSettings('appearance');
  }
  function manager(nav){
    if(!ready||!nav.manager)return;
    const {dialog}=nav.manager;
    if(!get('dock-connection-summary')){
      const title=dialog.querySelector('h2');title.textContent='连接与接入';title.removeAttribute('data-i18n');
      const summary=node('section',null,'dock-connection-summary');summary.id='dock-connection-summary';summary.setAttribute('aria-label','分层连接状态');summary.setAttribute('role','status');
      summary.append(node('h3','连接状态'));
      for(const [id,label] of [['gateway','网页到网关'],['codex','网关到 Codex'],['account','账号与 API']]){const p=node('p');p.append(node('strong',label+'：'),node('span'));p.lastChild.id='dock-state-'+id;summary.append(p);}
      const account=button('查看账号与接入',()=>{dialog.close();get('accounts-button').click();});summary.append(account,node('p','接入的编辑、登录与安装请在电脑端完成。Desktop 与 CLI 共用本机接入；WSL 接入单独管理。','muted'));
      dialog.querySelector('.picker-head').after(summary);
    }
    clients(nav);
  }
  function clients(nav){
    if(!ready)return;
    clientNavigation.switchPreferences.hidden=true;clientNavigation.switchToggle.hidden=true;
    if(nav.clientsKnown&&!nav.loading&&!nav.clients.some(c=>c.enabled)){lastList={phase:'disconnected',count:0,error:''};listView();}
    if(!get('dock-state-gateway'))return;
    get('dock-state-gateway').textContent=nav.statusFailure?'状态暂未更新'+(nav.lastStatusSuccess?' · 上次成功 '+new Date(nav.lastStatusSuccess).toLocaleTimeString():''):nav.loading?'正在读取…':nav.lastStatusSuccess?'已读取 · '+new Date(nav.lastStatusSuccess).toLocaleTimeString():'尚未确认';
    get('dock-state-codex').textContent=nav.statusFailure?'保留上次结果，请刷新核对':nav.managementClients().map(c=>(c.name||'Codex')+' · '+nav.connection(c).label).join('；')||'尚未启用接入';
    get('dock-state-account').textContent='以当前执行环境为准，切换前核对作用范围';
    nav.manager.note.textContent='安装和登录请在电脑端完成。关闭接入时可选择保留或退出电脑 App；启动、退出及切换前会重新核验任务状态。';
    nav.manager.note.removeAttribute('data-i18n');
  }
  function boot(){
    if(ready)return;ready=true;document.body.classList.add('dock-ui');
    const composer=get('composer'),tools=composer.querySelector('.composer-tools'),advanced=node('details',null,'dock-composer-more'),summary=node('summary','更多选项');advanced.id='dock-composer-more';advanced.append(summary);
    const controls=node('div',null,'dock-advanced-controls');controls.append(get('notify-button'),get('skills-button'));advanced.append(controls);tools.append(advanced);
    for(const id of ['send-mode','work-mode']){const select=get(id),label=node('label',id==='send-mode'?'发送方式':'工作模式');label.append(select);controls.append(label);}
    const selection=node('p',null,'dock-selection');selection.id='dock-selection';get('composer-body').prepend(selection);
    get('permissions-button').title='查看当前权限并调整';
    const context=node('small',null,'dock-chat-context');context.id='dock-chat-context';get('chat').querySelector('.chat-info').append(context);
    const hint=node('p','Enter 换行 · Ctrl / ⌘ + Enter 发送','dock-keyboard-hint');composer.append(hint);
    const status=node('div',null,'dock-delivery');status.id='dock-delivery';status.setAttribute('role','status');status.setAttribute('aria-live','polite');const text=node('span');text.id='dock-delivery-text';const check=button('查看消息与核对状态',()=>openMessageManagement());check.id='dock-check-delivery';status.append(text,check);get('message').before(status);
    for(const id of ['send-mode','work-mode'])get(id).addEventListener('change',selections);
    get('message').addEventListener('input',sendLabel);
    new MutationObserver(selections).observe(get('notify-button'),{childList:true,characterData:true,subtree:true});
    new MutationObserver(selections).observe(get('work-mode'),{attributes:true,attributeFilter:['disabled','hidden']});
    const footer=clientNavigation.footer,settingsButton=button('设置',()=>{get('appearance-dialog').showModal();window.loadNotificationDefaults?.();});settingsButton.id='dock-settings';footer.append(settingsButton);
    get('clients-button').textContent='连接与接入';get('clients-button').removeAttribute('data-i18n');
    clientNavigation.computer.classList.add('dock-device');clientNavigation.navigation.prepend(clientNavigation.computer);
    get('chat').querySelector('.appearance-button').onclick=()=>get('chat-details-button').click();get('chat').querySelector('.appearance-button').setAttribute('aria-label','聊天操作');get('chat').querySelector('.appearance-button').removeAttribute('data-i18n-aria-label');
    const filters=get('sidebar').querySelector('.list-filters'),filterDetails=node('details',null,'dock-filters'),filterSummary=node('summary','筛选');filterSummary.id='dock-filter-toggle';filters.before(filterDetails);filterDetails.append(filterSummary,filters);
    for(const id of ['search','list-mode','source-filter','archived'])get(id).addEventListener('change',()=>{listView();});
    const empty=node('div',null,'dock-empty');empty.id='dock-list-state';empty.setAttribute('role','status');const emptyTitle=node('h3'),emptyHelp=node('p',null,'muted');emptyTitle.id='dock-list-title';emptyHelp.id='dock-list-help';
    const retry=button('重新读取',()=>loadList(true).catch(()=>{}));retry.id='dock-list-retry';const create=button('新建聊天',()=>get('new-chat').click());create.id='dock-list-new';
    const clear=button('清除筛选',()=>{get('search').value='';get('archived').checked=false;get('source-filter').value='all';get('source-filter').dispatchEvent(new Event('change'));});clear.id='dock-list-clear';empty.append(emptyTitle,emptyHelp,retry,create,clear);get('sessions').after(empty);
    const welcomeActions=node('div',null,'dock-welcome-actions');welcomeActions.id='dock-welcome-actions';welcomeActions.append(button('新建聊天',()=>get('new-chat').click()),button('连接与接入',()=>clientNavigation.openManager()));get('welcome').append(welcomeActions);
    settings();receipt();selections();listView();
    // Native dialog focus and existing dismissal rules remain in charge.
    const bindDialog=dialog=>{if(dialog.dataset.dockFocus)return;dialog.dataset.dockFocus='true';let trigger=null;dialog.addEventListener('beforetoggle',e=>{if(e.newState==='open')trigger=document.activeElement;});dialog.addEventListener('close',()=>{if(trigger?.isConnected&&!trigger.closest('[hidden]'))trigger.focus({preventScroll:true});});};
    document.querySelectorAll('dialog').forEach(bindDialog);new MutationObserver(records=>{for(const record of records)for(const n of record.addedNodes)if(n.nodeType===1){if(n.matches('dialog'))bindDialog(n);n.querySelectorAll('dialog').forEach(bindDialog);}}).observe(document.body,{childList:true});
    // Only the available visual viewport changes; no session or panel is rebuilt.
    const viewport=window.visualViewport;let frame=0;
    if(viewport){const resize=()=>{cancelAnimationFrame(frame);frame=requestAnimationFrame(()=>{document.documentElement.style.setProperty('--dock-visible-height',Math.round(viewport.height)+'px');document.body.classList.toggle('dock-keyboard',innerHeight-viewport.height>120);});};viewport.addEventListener('resize',resize,{passive:true});resize();}
  }
  window.DockUI={
    delivery(kind,target,host,gateway,account,reason){if(!current(target,host,gateway,account))return;const labels={sending:'正在发送…',accepted:'已接收 · 等待任务更新',queued:'已加入队列 · 等待当前任务完成',unknown:'暂未确认是否接收，请先核对消息状态。',rejected:'消息明确未接收，原稿已保留，可修正后重试。'};deliveryState={kind,text:reason?reason+' '+labels[kind]:labels[kind],account:codexDraftScope?.account};receipt();},
    view(){if(ready)get('dock-chat-context').textContent=[state?.cwd,state?.hostLabel||(currentHost==='local'?'此电脑':currentHost)].filter(Boolean).join(' · ');if(deliveryState&&deliveryState.account!==codexDraftScope?.account){deliveryState=null;receipt();}selections();},
    opened(){deliveryState=null;receipt();selections();},
    queue(rows,pending){if(sending&&deliveryState?.kind==='sending')return;if(pending.length||rows.some(row=>row.status!=='queued'))deliveryState={kind:'unknown',text:'有消息结果待确认，请先核对状态。',account:codexDraftScope?.account};else if(deliveryState?.kind==='unknown')deliveryState={kind:'updated',text:'未确认记录已更新，请查看消息管理中的结果。',account:codexDraftScope?.account};receipt();},
    list(value){lastList=value;listView();},manager,clients,
    inlineError(text){return ready&&lastList.phase==='error'&&lastList.error.includes(text);},
    checkPending(body){if(deliveryState?.kind!=='unknown')return false;const fields=['text','mode','workMode','skills','attachments','references'];if(!pendingBodies().some(p=>fields.every(key=>JSON.stringify(p[key])===JSON.stringify(body[key]))))return false;openMessageManagement();return true;},
  };
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',boot,{once:true});else boot();
})();
