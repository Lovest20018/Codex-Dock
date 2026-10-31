'use strict';
let csrf='', accounts, cli, wsl;
const status=document.getElementById('status');
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
    let scan=document.createElement('button');scan.textContent='扫描 WSL';scan.onclick=async()=>{scan.disabled=true;try{await call('clients',{action:'scan-wsl'});await refreshClients();}catch(error){status.textContent=error.message;}finally{scan.disabled=false;}};root.append(scan);
    const environments=await call('cliAccess',{action:'wslAccounts',provider:'codex',operation:'environments'}),select=document.getElementById('wsl-entry'),selected=select.value;
    select.replaceChildren(new Option('请选择',''));for(const row of environments.environments||[])select.append(new Option(row.label||row.id,row.id));select.value=selected;
  }
}
async function refresh(){await Promise.all([accounts.refresh(),cli.refresh(),refreshClients()]);}
async function start(){
  const ticket=location.hash.slice(1);history.replaceState(null,'',location.pathname);
  csrf=(await json(ticket?'unlock':'session',ticket?{ticket}:undefined)).csrf;
  status.textContent='此页面仅在本机有效，授权 30 分钟。账号操作会按提示影响此电脑的 Codex。';
  document.getElementById('content').hidden=false;
  accounts=new AccountsPanel({root:document.getElementById('accounts'),desktop:true,read:()=>call('accounts',{action:'list'}),request:value=>call('accounts',value)});
  cli=new CliAccessPanel({root:document.getElementById('cli'),provider:'codex',read:()=>call('cliAccess',{action:'list'}),request:value=>call('cliAccess',value),onChanged:()=>accounts.refresh()});
  wsl=new WslAccountsPanel({root:document.getElementById('wsl'),provider:'codex',desktop:true,read:entry=>call('cliAccess',{action:'wslAccounts',provider:'codex',entry,operation:'list'}),request:value=>call('cliAccess',{...value,action:'wslAccounts',operation:value.action})});
  document.getElementById('wsl-entry').onchange=event=>wsl.select(event.target.value);
  await refresh();
}
document.getElementById('refresh').onclick=()=>refresh().catch(error=>status.textContent=error.message);
start().catch(error=>status.textContent=error.message);
