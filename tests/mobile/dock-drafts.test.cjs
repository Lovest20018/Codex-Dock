'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),path=require('node:path');
const {ComposerDrafts}=require(path.resolve(__dirname,'../../src/CodexMobile/upstream/web/features/chat/composer-drafts.js'));
const scope={gateway:'test-gateway',provider:'codex',account:'verified-account',host:'local',session:'12345678-1234-1234-1234-123456789abc'};
function storage(){const values=new Map();return {getItem:key=>values.get(key)??null,setItem:(key,value)=>values.set(key,value),removeItem:key=>values.delete(key)};}

test('caret changes persist without changing the submitted content revision',()=>{
  const drafts=new ComposerDrafts(storage(),null),fields={text:'message',selectionStart:7,selectionEnd:7};
  const submitted=drafts.save(scope,fields);
  const moved=drafts.save(scope,{...fields,selectionStart:0,selectionEnd:3});
  assert.equal(moved.revision,submitted.revision);
  assert.equal(drafts.load(scope).selectionEnd,3);
  for(const change of [{text:'new message'},{quotes:[{text:'quote'}]},{skills:['skill']},{sendMode:'queue'},{workMode:'plan'}]){
    assert(drafts.save(scope,{...fields,...change}).revision>moved.revision);
  }
});

test('confirmed provisional promotion clears only its recorded scope and revision',()=>{
  const drafts=new ComposerDrafts(storage(),null),sent=drafts.save(scope,{text:'submitted'});
  const other={...scope,account:'other'};drafts.save(other,{text:'other account'});
  const submission={id:'request',body:{text:'submitted'},promoted:{scope,revision:sent.revision,draft:sent}};
  drafts.save(scope,{text:'new draft'});
  assert.equal(drafts.clearPromoted(submission),null);assert.equal(drafts.load(scope).text,'new draft');
  assert.equal(drafts.load(other).text,'other account');
});

test('receipt and HTTP confirmation cannot clear a new draft even when its revision restarts',()=>{
  const drafts=new ComposerDrafts(storage(),null),sent=drafts.save(scope,{text:'submitted'});
  const submission={id:'request',body:{text:'submitted'},promoted:{scope,revision:sent.revision,draft:sent}};
  assert.equal(drafts.clearPromoted(submission).draft.text,'submitted');assert.equal(drafts.load(scope),null);
  drafts.save(scope,{text:'submitted'});
  assert.equal(drafts.load(scope).revision,sent.revision);
  assert.equal(drafts.clearPromoted(submission),null);assert.equal(drafts.load(scope).text,'submitted');
});
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
