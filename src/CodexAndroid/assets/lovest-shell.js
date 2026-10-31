/* APK-owned presentation and navigation. No network requests or task mutations. */
(() => {
  if (window !== window.top || window.LovestShell) return;
  const app = document.getElementById('app');
  const sheet = [...document.styleSheets].find(s => s.href && new URL(s.href).origin === location.origin);
  if (!app || !sheet || !window.BridgeNavigation) return; // Keep legacy entrances on unsupported pages.
  const en = () => document.documentElement.lang.startsWith('en');
  const label = (zh, english) => en() ? english : zh;
  const native = data => window.prompt('lovest-shell:__BRIDGE_CLIPBOARD_TOKEN__', JSON.stringify({version:1, ...data}));
  function context() {
    const desktop = typeof desktopSessions !== 'undefined' && desktopSessions.isChatOpen;
    return desktop ? {id:desktopSessions.sid, host:'desktop:'+desktopSessions.provider, scope:desktopSessions.draftScope, timeline:desktopSessions.messages}
      : {id:typeof currentId==='undefined'?null:currentId, host:typeof currentHost==='undefined'?'local':currentHost,
         scope:typeof codexDraftScope==='undefined'?null:codexDraftScope, timeline:document.getElementById('timeline')};
  }
  const scrollKey = c => c.scope && typeof composerDrafts!=='undefined' ? composerDrafts.key(c.scope,'reading') : null;
  function persist() {
    const c=context();
    if (typeof saveDraft==='function') saveDraft();
    if (typeof desktopSessions!=='undefined') desktopSessions.saveDraft();
    const key=scrollKey(c);if(key && c.timeline?.clientHeight)try{localStorage.setItem(key,String(c.timeline.scrollTop));}catch{}
  }
  function prepareLeave() {
    persist();
    let draftUnsafe=typeof codexProvisionalDrafts!=='undefined'&&[...codexProvisionalDrafts.values()].some(d=>d.text?.trim());
    if(typeof desktopSessions!=='undefined')draftUnsafe ||= [...desktopSessions.provisionalDrafts.values()].some(d=>d.text?.trim());
    const c=context(),input=typeof desktopSessions!=='undefined'&&desktopSessions.isChatOpen?desktopSessions.input:document.getElementById('message');
    if(input?.value.trim()){
      const saved=c.scope&&typeof composerDrafts!=='undefined'&&composerDrafts.load(c.scope);
      draftUnsafe ||= !saved||saved.text!==input.value;
    }
    const sets=[];
    if(typeof attachments!=='undefined')sets.push(attachments);
    if(typeof desktopSessions!=='undefined'&&desktopSessions.attachments)sets.push(desktopSessions.attachments);
    const attachmentCount=sets.reduce((n,set)=>n+[...set.drafts.values()].reduce((a,rows)=>a+rows.length,0),0);
    return {version:1,draftUnsafe,attachmentCount};
  }
  const rules=[
    '.lovest-shell #sidebar .sidebar-foot,.lovest-shell .dock-device{display:none!important}',
    '.lovest-shell .dock-ui #sidebar{padding:0 16px 8px;gap:8px;background:var(--dock-panel)}',
    '.lovest-shell .dock-ui #sidebar .list-heading{display:flex;height:auto;min-height:56px;padding:4px 0!important;gap:8px;flex-wrap:nowrap}',
    '.lovest-shell .dock-ui #sidebar .list-heading h2{flex:1;min-width:0;gap:7px;font-size:20px;margin:0}',
    '.lovest-shell .dock-ui .list-heading .client-badge{font-size:11px;font-weight:500;color:var(--muted);gap:4px}',
    '.lovest-shell .dock-ui .list-heading .client-icon{width:18px;height:18px}',
    '.lovest-shell .dock-ui .list-heading .connected-computer{display:none}',
    '.lovest-shell .dock-ui .list-actions{margin:0;gap:0}',
    '.lovest-shell #sidebar #sessions{height:auto;flex:1 1 auto}',
    '.lovest-shell #sidebar.dock-list-empty #sessions{display:none}',
    '.lovest-shell .dock-ui #sidebar #search{flex:none;box-sizing:border-box;width:100%;height:44px;min-height:44px;padding:10px 14px;border-radius:12px;font-size:14px!important;background:var(--dock-surface)!important;border-color:transparent!important}',
    '.lovest-shell .dock-ui #sessions{padding:0;display:block}',
    '.lovest-shell .dock-ui .session{margin:0;padding:16px 2px;border-radius:0;border-bottom:1px solid var(--line);min-height:76px}',
    '.lovest-shell .dock-ui .session strong{font-size:15px;font-weight:550;line-height:1.5}',
    '.lovest-shell .dock-ui .session small{font-size:11px;line-height:1.5;margin-top:6px}',
    '.lovest-shell .dock-ui .session-list-region{justify-content:flex-start}',
    '.lovest-shell .dock-empty{padding:18px 0}',
    '.lovest-shell .dock-ui .chat-head{height:64px;min-height:64px;padding:4px 56px 4px 4px!important;gap:6px}',
    '.lovest-shell .dock-ui .chat-head .chat-info{display:flex;flex-direction:column;align-items:stretch;justify-content:center;gap:1px;overflow:hidden}',
    '.lovest-shell .dock-ui .chat-head .chat-title-button{display:block;height:auto;min-height:24px;flex:none;padding:0}',
    '.lovest-shell .dock-ui #chat-title,.lovest-shell .dock-ui .ds-title{font-size:15px;line-height:24px;letter-spacing:0}',
    '.lovest-shell .dock-ui .chat-head .dock-chat-context{display:block;font-size:10px;line-height:16px;max-width:100%}',
    '.lovest-shell .dock-ui .chat-head>.client-icon{width:18px;height:18px}',
    '.lovest-shell .dock-ui .chat-head>.status{font-size:10px;padding:4px 6px;height:auto;max-width:64px}',
    '.lovest-shell .dock-ui #composer{border-radius:18px;margin:0 8px 8px;padding:8px 10px;background:var(--dock-panel);box-shadow:0 2px 12px #00000005}',
    '.lovest-shell .dock-ui #message{font-size:16px;min-height:48px;height:48px;max-height:140px;padding:10px 12px;border:0;border-radius:10px;background:var(--dock-surface);resize:none}',
    '.lovest-shell .dock-ui .list-heading #new-chat{font-size:13px;height:40px;min-height:40px;padding:8px 12px;border-radius:12px;gap:4px}',
    '.lovest-shell .dock-ui #new-chat .header-icon{width:18px;height:18px}',
    '.lovest-shell .lovest-bell{display:grid;place-items:center;flex:none;width:44px;height:44px;min-height:44px;padding:10px;border:0;border-radius:12px;background:transparent;color:var(--muted)}',
    '.lovest-shell .lovest-approval{display:block;width:100%;padding:12px;text-align:left;background:var(--soft);color:var(--text);border:0;border-radius:8px;margin:0 0 8px}',
    '.lovest-shell .lovest-approval[hidden]{display:none}',
    '.lovest-shell .dock-ui button,.lovest-shell .dock-ui select{min-height:44px}',
    '.lovest-shell .dock-ui .dock-filters{font-size:12px;width:100%}',
    '.lovest-shell .dock-ui .dock-filters>summary{min-height:40px;padding:10px 2px;font-size:11px;text-align:left;list-style:none}',
    '.lovest-shell .dock-ui .dock-filters>summary::before{content:"≡";margin-right:6px;font-size:14px}',
    '.lovest-shell .dock-keyboard-hint{display:none}',
    '.lovest-shell .dock-ui .composer-tools{display:flex;flex-wrap:wrap;gap:4px;padding:0 0 4px;align-items:center}',
    '.lovest-shell .dock-ui .composer-tools .tool-button{font-size:12px;min-height:40px;padding:6px 8px;border:0;border-radius:9px;background:transparent}',
    '.lovest-shell .dock-ui #model-button{max-width:calc(100% - 124px);min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;gap:3px}',
    '.lovest-shell .dock-ui #model-name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    '.lovest-shell .dock-ui .context-usage{min-width:36px;width:36px;padding:8px}',
    '.lovest-shell .dock-ui .context-usage-ring{width:15px;height:15px}',
    '.lovest-shell .dock-ui #composer-toggle{width:36px;min-width:36px;min-height:40px;height:40px;padding:8px;border:0;background:transparent}',
    '.lovest-shell .dock-ui .dock-composer-more{font-size:12px;margin-left:auto}',
    '.lovest-shell .dock-ui .dock-composer-more>summary{display:flex;align-items:center;justify-content:flex-end;min-height:40px;padding:6px 4px;list-style:none;color:var(--muted)}',
    '.lovest-shell .dock-ui .dock-composer-more>summary::-webkit-details-marker{display:none}',
    '.lovest-shell .dock-ui .dock-composer-more[open]{order:10;flex:1 1 100%;margin:0;border-top:1px solid var(--line)}',
    '.lovest-shell .dock-ui .dock-advanced-controls{display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:8px;padding:0 0 8px;max-height:32dvh;overflow-y:auto;overscroll-behavior:contain}',
    '.lovest-shell .dock-ui .dock-advanced-controls label{grid-column:span 3;order:-1;min-width:0;gap:4px;font-size:11px}',
    '.lovest-shell .dock-ui .dock-advanced-controls select{width:100%;min-width:0;min-height:40px;padding:6px 8px;font-size:13px;border-radius:9px;background:var(--dock-surface)}',
    '.lovest-shell .dock-ui .dock-advanced-controls .tool-button{grid-column:span 2;background:var(--dock-surface);text-align:center;white-space:normal}',
    '.lovest-shell .dock-ui #composer .compose-bottom{padding:4px 0 0;gap:6px;align-items:center}',
    '.lovest-shell .dock-ui #composer .compose-actions button{min-height:40px;padding:8px 16px;border-radius:11px;font-size:13px}',
    '.lovest-shell .dock-ui .attach-button{width:40px;height:40px;min-height:40px;padding:9px}',
    '.lovest-shell .dock-ui .dock-selection{font-size:11px;margin:2px 0 4px}',
    '.lovest-shell .dock-ui .chat-list-toolbar{gap:0;align-items:stretch}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog){position:fixed;inset:auto 0 0;box-sizing:border-box;width:100%;max-width:560px;height:auto;max-height:92dvh;margin:auto auto 0;padding:20px 20px max(16px,env(safe-area-inset-bottom));border:0;border-radius:24px 24px 0 0;background:var(--dock-panel);overflow-y:auto;overscroll-behavior:contain}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog)::backdrop{background:#14182766}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .picker-head{margin-bottom:10px;gap:8px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .picker-head h2{font-size:18px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .picker-head button{font-size:12px;min-height:44px;border:0;background:transparent;padding:8px}',
    '.lovest-shell .dock-ui :is(#model-form,.ds-model-form){gap:14px}',
    '.lovest-shell .dock-ui :is(#model-updated,.ds-model-form>small.muted){font-size:10px;line-height:16px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .model-options{grid-auto-rows:minmax(44px,auto);max-height:224px;padding:4px;gap:0;border-radius:12px;background:var(--dock-surface)}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .model-choice{min-height:44px;padding:8px 12px;font-size:14px;border-radius:9px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .model-section-title{font-size:11px;margin-bottom:7px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .effort-options{grid-template-columns:repeat(3,minmax(0,1fr));padding:4px;border-radius:12px;background:var(--dock-surface)}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .effort-choice{min-height:40px;font-size:12px;padding:8px 3px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .fast-mode-control{padding:10px 0 0}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .model-fast-label{min-height:40px;font-size:13px}',
    '.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) #model-save{min-height:46px;font-size:14px;border-radius:12px}',
    '@media(max-height:500px){.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog){max-height:96dvh;padding:12px 16px}.lovest-shell .dock-ui :is(#model-dialog,.ds-model-dialog) .model-options{max-height:132px}}',
    '@media(max-height:480px){.lovest-shell .dock-ui .dock-advanced-controls{max-height:22dvh}.lovest-shell .dock-ui #message{min-height:40px;height:40px}}'
  ];
  let last='',scheduled=false,scrollTimer=0,restoreKey='',restoreUntil=0,userMoved=false;
  const bell=document.createElement('button');bell.type='button';bell.className='lovest-bell';
  bell.innerHTML='<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4"/></svg>';
  bell.addEventListener('click',e=>{if(e.isTrusted)native({action:'inbox'});});
  const heading=document.querySelector('.list-heading');if(!heading)return;
  heading.append(bell);
  const advanced=document.querySelector('.dock-advanced-controls'),permissions=document.getElementById('permissions-button');
  if(advanced&&permissions)advanced.append(permissions);
  const more=document.querySelector('.dock-composer-more>summary');
  function moreLabel(){if(more){const value=label(more.parentElement.open?'收起选项':'选项',more.parentElement.open?'Less':'Options');if(more.textContent!==value)more.textContent=value;}}
  more?.parentElement.addEventListener('toggle',moreLabel);
  const approval=document.createElement('button');approval.type='button';approval.className='lovest-approval';approval.hidden=true;
  document.getElementById('composer')?.before(approval);
  approval.onclick=()=>document.getElementById('approvals')?.scrollIntoView({block:'start'});
  function sync() {
    scheduled=false;
    const auth=!app.hidden,detail=auth&&app.classList.contains('chat-open'),modal=!!document.querySelector('dialog[open]');
    const c=context(),value={action:'state',authenticated:auth,detail,modal,threadId:detail?c.id:null,host:c.host};
    const stamp=JSON.stringify(value);if(stamp!==last){last=stamp;native(value);}
    bell.setAttribute('aria-label',label('通知收件箱','Notification inbox'));
    moreLabel();
    const pending=document.getElementById('approvals');const count=pending?.querySelectorAll('.approval').length||0;
    const hidden=!detail||!count;if(approval.hidden!==hidden)approval.hidden=hidden;const text=label('有待处理的请求 · 查看','Requests need your attention · View');if(approval.textContent!==text)approval.textContent=text;
    const key=scrollKey(c);
    if(key!==restoreKey){restoreKey=key;restoreUntil=Date.now()+8000;userMoved=false;}
    if(key&&!userMoved&&Date.now()<restoreUntil&&c.timeline?.clientHeight){
      let position=0;try{position=Number(localStorage.getItem(key))||0;}catch{}
      if(position>0&&c.timeline.scrollHeight>=position+c.timeline.clientHeight){c.timeline.scrollTop=position;restoreUntil=0;}
    }
  }
  function schedule(){if(!scheduled){scheduled=true;requestAnimationFrame(sync);}}
  window.LovestShell=Object.freeze({version:1,prepareLeave,persist,
    back:()=>window.BridgeNavigation.back(),
    openSettings:page=>{document.getElementById('dock-settings')?.click();document.querySelector('[data-dock-setting="'+page+'"]')?.click();},
    language:value=>{window.BridgeI18n?.setLanguage(value);window.BridgeI18n?.apply();}
  });
  // Only remove duplicate entrances after the native side acknowledges this version.
  if(native({action:'ready'})!=='ready')return;
  for(const rule of rules)sheet.insertRule(rule,sheet.cssRules.length);
  document.documentElement.classList.add('lovest-shell');
  new MutationObserver(schedule).observe(app,{subtree:true,childList:true,attributes:true,attributeFilter:['class','hidden','open']});
  new MutationObserver(schedule).observe(document.body,{subtree:true,attributes:true,attributeFilter:['open']});
  document.addEventListener('bridge-language',schedule);
  document.addEventListener('bridge-chat-visible',schedule);
  for(const event of ['touchstart','wheel','pointerdown'])document.addEventListener(event,()=>{userMoved=true;},{passive:true});
  document.addEventListener('scroll',()=>{clearTimeout(scrollTimer);scrollTimer=setTimeout(persist,250);},{capture:true,passive:true});
  window.addEventListener('pagehide',persist);document.addEventListener('visibilitychange',persist);
  sync();
})();
