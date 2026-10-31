'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path');
const {ComposerDrafts}=require(path.resolve(__dirname,'../../src/CodexMobile/upstream/web/features/chat/composer-drafts.js'));
const scope={gateway:'test-gateway',provider:'codex',account:'verified-account',host:'local',session:'12345678-1234-1234-1234-123456789abc'};
function storage(){const values=new Map();return {getItem:key=>values.get(key)??null,setItem:(key,value)=>values.set(key,value),removeItem:key=>values.delete(key)};}
test('old Dock drafts require an explicit account-scoped import and retain unknown IDs',async()=>{
  const store=storage(),source='codex-dock-mobile:'+scope.gateway+':draft:'+scope.session;
  store.setItem(source,'unsent text');store.setItem('codex-dock-mobile:'+scope.gateway+':pending:'+scope.session,JSON.stringify({id:scope.session,kind:'send'}));
  const drafts=new ComposerDrafts(store,null);assert.equal(drafts.importDock(scope,()=>false),null);assert.equal(drafts.load(scope),null);
  assert.equal(drafts.importDock(scope,()=>true).text,'unsent text');assert.equal(drafts.pending(scope).id,scope.session);
  assert.equal(store.getItem(source),'unsent text');assert.equal(drafts.importDock({...scope,account:'different-account'},()=>true),null);
  assert.equal(drafts.pending({...scope,account:'different-account'}),null);
});
test('remote and incomplete identities never import legacy local text',()=>{
  const store=storage();store.setItem('codex-dock-mobile:'+scope.gateway+':draft:'+scope.session,'private draft');
  const drafts=new ComposerDrafts(store);let called=false;const confirm=()=>{called=true;return true;};
  for(const change of [{host:'remote-fixture'},{account:''},{provider:'claude'}])assert.equal(drafts.importDock({...scope,...change},confirm),null);
  assert.equal(called,false);
});
