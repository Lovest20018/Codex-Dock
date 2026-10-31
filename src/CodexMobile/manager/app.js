'use strict';
let csrf='', accounts, cli, wsl;
const status=document.getElementById('status');
function tab(id){
  document.querySelectorAll('[data-tab]').forEach(b=>{const selected=b.dataset.tab===id;b.setAttribute('aria-selected',String(selected));b.tabIndex=selected?0:-1;});
  for(const name of ['saved','api','environments'])document.getElementById('panel-'+name).hidden=name!==id;
}
document.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>tab(b.dataset.tab));
document.querySelector('.manager-tabs').onkeydown=event=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();const buttons=[...document.querySelectorAll('[data-tab]')],at=buttons.indexOf(document.activeElement),index=event.key==='Home'?0:event.key==='End'?2:(at+(event.key==='ArrowRight'?1:2))%3;buttons[index].click();buttons[index].focus();};
function currentAccess(value){
  const current=value.current,phase=value.switch?.phase,labels={preparing:'正在准备账号',stopping:'正在退出桌面应用',applying:'正在应用接入',starting:'正在启动桌面应用',verifying:'正在核验连接',restoring:'正在恢复原接入',restored:'已恢复原接入',failed:'切换未完成',interrupted:'请在电脑端恢复原接入',complete:'切换完成'};
  const identity=current?.status==='ready'?(current.kind==='signedOut'?'Codex 尚未登录':current.name||current.kind):current?.status==='checking'?'正在识别当前接入…':'当前接入尚未识别，请选择已保存的接入。';
  document.getElementById('current-access').textContent=[identity,labels[phase],value.switch?.error].filter(Boolean).join(' · ');
}
async function json(path,body){
  const response=await fetch(path,{method:body?'POST':'GET',credentials:'same-origin',headers:body?{'Content-Type':'application/json','X-CSRF-Token':csrf}:{},body:body?JSON.stringify(body):undefined});
  const value=await response.json();if(!response.ok)throw Error(value.error||'操作未能确认，请刷新核对结果');return value;
}
const call=(method,value)=>json('api',{method,value});
async function refreshClients(){
  const value=await call('clients',{action:'clients'}),root=document.getElementById('clients');root.replaceChildren();
  for(const row of value.clients||[]){
    const button=document.createElement('button');button.textContent=row.name+' · '+(row.enabled?'已开启':'已关闭');
    button.onclick=async()=>{button.disabled=true;try{await call('clients',{action:'toggle-client',provider:row.id,enabled:!row.enabled});await refreshClients();}catch(error){status.textContent=error.message;button.disabled=false;}};root.append(button);
  }
  if(value.wslSupported){
    document.getElementById('wsl-label').hidden=false;
    let scan=document.createElement('button');scan.textContent='扫描 WSL';scan.onclick=async()=>{scan.disabled=true;try{await call('clients',{action:'scan-wsl'});await refreshClients();}catch(error){status.textContent=error.message;}finally{scan.disabled=false;}};root.append(scan);
    const environments=await call('cliAccess',{action:'wslAccounts',provider:'codex',operation:'environments'}),select=document.getElementById('wsl-entry'),selected=select.value;
    select.replaceChildren(new Option('请选择',''));for(const row of environments.environments||[])select.append(new Option(row.label||row.id,row.id));select.value=selected;
  }
}
async function refresh(){const refreshButton=document.getElementById('refresh');if(refreshButton.disabled)return;refreshButton.disabled=true;refreshButton.textContent='正在刷新…';try{await Promise.all([accounts.refresh(),cli.refresh(),refreshClients()]);}finally{refreshButton.disabled=false;refreshButton.textContent='刷新状态';}}
async function start(){
  const ticket=location.hash.slice(1);history.replaceState(null,'',location.pathname);
  csrf=(await json(ticket?'unlock':'session',ticket?{ticket}:undefined)).csrf;
  status.textContent='此页面仅在本机有效，授权 30 分钟。账号操作会按提示影响此电脑的 Codex。';
  document.getElementById('content').hidden=false;
  accounts=new AccountsPanel({root:document.getElementById('accounts'),desktop:true,read:()=>call('accounts',{action:'list'}),request:value=>call('accounts',value),onUpdate:currentAccess});
  cli=new CliAccessPanel({root:document.getElementById('cli'),provider:'codex',read:()=>call('cliAccess',{action:'list'}),request:value=>call('cliAccess',value),onChanged:()=>{tab('saved');accounts.refresh();}});
  accounts.refreshButton.hidden=true;
  for(const child of [...cli.root.children])if(child.tagName==='H2'||child.tagName==='BUTTON'&&child!==cli.native)child.hidden=true;
  document.getElementById('cli-rows').append(cli.rows,cli.native);
  document.getElementById('cli-feedback').append(cli.status,cli.error);
  const setBusy=cli.setBusy.bind(cli);cli.setBusy=busy=>{setBusy(busy);cli.rows.querySelectorAll('button,input,select').forEach(control=>control.disabled=busy);cli.native.disabled=busy;};
  cli.form.replaceChildren(cli.name.label,cli.baseUrl.label,cli.key.label,cli.model.label,cli.form.querySelector('.cli-access-model-hint'),cli.schemeRow,cli.form.querySelector('.cli-access-form-actions'));
  const edit=cli.edit.bind(cli);cli.edit=(...args)=>{tab('api');edit(...args);};
  const resetForm=cli.resetForm.bind(cli);cli.resetForm=()=>{resetForm();cli.cancel.hidden=false;};cli.cancel.hidden=false;
  document.getElementById('add-api').onclick=()=>{cli.resetForm();tab('api');cli.name.input.focus();};
  const cancel=cli.cancel.onclick;cli.cancel.onclick=()=>{cancel();tab('saved');};
  wsl=new WslAccountsPanel({root:document.getElementById('wsl'),provider:'codex',desktop:true,read:entry=>call('cliAccess',{action:'wslAccounts',provider:'codex',entry,operation:'list'}),request:value=>call('cliAccess',{...value,action:'wslAccounts',operation:value.action})});
  document.getElementById('wsl-entry').onchange=event=>wsl.select(event.target.value);
  await refresh();
}
document.getElementById('refresh').onclick=()=>refresh().catch(error=>status.textContent=error.message);
start().catch(error=>status.textContent=error.message);
