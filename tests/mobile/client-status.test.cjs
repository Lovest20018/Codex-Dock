'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
function fixture(){
  const app={hidden:false},context=vm.createContext({document:{hidden:false,getElementById:()=>app},setTimeout,clearTimeout,AbortController,Date});
  for(const name of ['client-lifecycle','client-navigation'])vm.runInContext(fs.readFileSync(path.resolve(__dirname,'../../src/CodexMobile/upstream/web/features/clients/'+name+'.js'),'utf8'),context);
  const controller=vm.runInContext('Object.create(ClientNavigation.prototype)',context),messages=[],calls=[];
  Object.assign(controller,{clients:[{id:'codex',enabled:true,connected:true}],clientsKnown:true,revision:1,pending:new Set(),operationFailures:new Map(),swipeSnapshots:new Map(),paint(){},notify:message=>messages.push(message),applyClients(data){this.clients=data.clients;},request:async(url,body,signal)=>{calls.push({url,body,signal});throw Error('客户端状态读取超时，请重试。');}});
  return {controller,messages,calls,app};
}
test('repeated passive status failures notify once, preserve clients, and back off',async()=>{
  const {controller,messages,calls}=fixture();
  await controller.poll();const retry=controller.statusRetryAt;
  assert.equal(messages.length,1);assert(retry>Date.now());
  await controller.poll();assert.equal(calls.length,1);
  for(let i=0;i<3;i++){controller.statusRetryAt=0;await controller.poll();}
  assert.equal(messages.length,1);assert.equal(calls.length,4);
  assert.equal(controller.clients[0].connected,true);
  assert(controller.statusRetryAt>Date.now()+59000);
  assert(calls.every(call=>call.url==='/api/clients'&&call.body===undefined&&call.signal));
});
test('explicit refresh bypasses backoff and reports its own failure',async()=>{
  const {controller,messages,calls}=fixture();await controller.poll();await controller.refresh();
  assert.equal(calls.length,2);assert.equal(messages.length,2);
});
test('successful status resets failure state and permits a later outage to notify',async()=>{
  const {controller,messages}=fixture(),failure=controller.request;await controller.poll();
  controller.request=async()=>({clients:[{id:'codex',enabled:true}],pending:false});await controller.refresh();
  assert.equal(controller.statusFailure,'');assert.equal(controller.statusRetryAt,0);assert.equal(controller.statusFailures,0);
  controller.request=failure;await controller.poll();assert.equal(messages.length,2);
});
test('a stale failure after logout produces no toast or retry state',async()=>{
  const {controller,messages}=fixture();let reject;
  controller.request=()=>new Promise((resolve,fail)=>reject=fail);
  const refresh=controller.refresh();controller.clear();reject(Error('stale failure'));await refresh;
  assert.equal(messages.length,0);assert.equal(controller.statusFailure,'');assert.equal(controller.statusRetryAt,0);
});
test('an actual timed-out read aborts and never overlaps its next background poll',async()=>{
  const {controller,messages,calls}=fixture();controller.statusTimeout=5;
  controller.request=async(url,body,signal)=>{calls.push({signal});return new Promise(()=>{});};
  const first=controller.poll();await controller.poll();await first;await controller.poll();
  assert.equal(calls.length,1);assert.equal(calls[0].signal.aborted,true);assert.equal(messages.length,1);
  assert.match(controller.statusFailure,/超时/);
});
