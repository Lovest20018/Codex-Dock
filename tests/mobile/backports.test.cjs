'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const {eligible}=require('../../src/CodexMobile/upstream/web/features/workspace/downloads.js');
test('image saving requires one explicit download flag and the current gateway',()=>{
 const base='https://fixture.invalid/',scope='/d/'+'a'.repeat(32),session='12345678-1234-4234-9234-123456789012',upload='12345678-1234-4234-9234-123456789013';
 const paths=[`/api/sessions/${session}/uploads/${upload}/preview`,`/api/sessions/${session}/desktop-images/${'b'.repeat(64)}`];
 for(const path of paths){
  assert(eligible(path+'?download=1&variant=original',base));
  assert(eligible(scope+path+'?download=1','https://fixture.invalid'+scope+'/'));
  for(const value of [path,path+'?download=0',path+'?download=1&download=1',path+'?download=1&%64ownload=0',path+'?download=1&download',path+'?download=1#fragment','https://external.invalid'+path+'?download=1','https://user:pass@fixture.invalid'+path+'?download=1'])assert.equal(eligible(value,base),false,value);
  assert.equal(eligible(path+'?download=1','https://fixture.invalid'+scope+'/'),false,'Relay-scoped page cannot escape its gateway prefix');
 }
});
