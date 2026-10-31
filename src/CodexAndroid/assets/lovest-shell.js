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
    '.lovest-shell .list-heading{padding:4px 56px 4px 0;gap:6px;flex-wrap:wrap}',
    '.lovest-shell #sidebar{padding:0 16px 8px;gap:10px}',
    '.lovest-shell .list-heading h2{font-size:20px}',
    '.lovest-shell #sidebar #sessions{height:auto;flex:1 1 auto}',
    '.lovest-shell #sidebar.dock-list-empty #sessions{display:none}',
    '.lovest-shell #search{font-size:16px!important}',
    '.lovest-shell .session strong{font-size:15px}',
    '.lovest-shell .session small{font-size:12px}',
    '.lovest-shell .session-list-region{justify-content:flex-start}',
    '.lovest-shell .dock-empty{padding:18px 0}',
    '.lovest-shell .chat-head{padding:4px 56px 4px 4px;min-height:56px}',
    '.lovest-shell #composer{border-radius:12px;margin:0 8px 8px;padding:10px}',
    '.lovest-shell #message{font-size:16px;min-height:48px}',
    '.lovest-shell #new-chat{font-size:14px;min-height:48px;padding:8px 12px}',
    '.lovest-shell .lovest-bell{min-width:48px;min-height:48px;padding:8px;border:0;background:transparent;color:var(--text)}',
    '.lovest-shell .lovest-approval{display:block;width:100%;padding:12px;text-align:left;background:var(--soft);color:var(--text);border:0;border-radius:8px;margin:0 0 8px}',
    '.lovest-shell .lovest-approval[hidden]{display:none}',
    '.lovest-shell button,.lovest-shell select{min-height:48px}',
    '.lovest-shell .dock-chat-context{font-size:12px}',
    '.lovest-shell .dock-filters{font-size:13px}',
    '.lovest-shell .dock-keyboard-hint{display:none}',
    '.lovest-shell .chat-title-button{min-width:0}',
    '.lovest-shell #model-button{max-width:65vw;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
    '.lovest-shell .chat-list-toolbar{gap:8px}'
  ];
  let last='',scheduled=false,scrollTimer=0,restoreKey='',restoreUntil=0,userMoved=false;
  const bell=document.createElement('button');bell.type='button';bell.className='lovest-bell';
  bell.innerHTML='<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4"/></svg>';
  bell.addEventListener('click',e=>{if(e.isTrusted)native({action:'inbox'});});
  const heading=document.querySelector('.list-heading');if(!heading)return;
  heading.append(bell);
  const advanced=document.querySelector('.dock-advanced-controls'),permissions=document.getElementById('permissions-button');
  if(advanced&&permissions)advanced.append(permissions);
  const approval=document.createElement('button');approval.type='button';approval.className='lovest-approval';approval.hidden=true;
  document.getElementById('composer')?.before(approval);
  approval.onclick=()=>document.getElementById('approvals')?.scrollIntoView({block:'start'});
  function sync() {
    scheduled=false;
    const auth=!app.hidden,detail=auth&&app.classList.contains('chat-open'),modal=!!document.querySelector('dialog[open]');
    const c=context(),value={action:'state',authenticated:auth,detail,modal,threadId:detail?c.id:null,host:c.host};
    const stamp=JSON.stringify(value);if(stamp!==last){last=stamp;native(value);}
    bell.setAttribute('aria-label',label('通知收件箱','Notification inbox'));
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
