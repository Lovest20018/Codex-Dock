const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const controller = fs.readFileSync(path.join(__dirname, '../src/CodexConfig.Core/desktop-connections.js'), 'utf8');
function fixture() {
  const native = [], host = [], states = new Map();
  const selected = { model: 'chosen-model', reasoningEffort: 'max', serviceTier: 'ultrafast' };
  const current = { generation: 'A', provider: 'openai', baseUrl: 'http://127.0.0.1:9/v1', restartRequired: false };
  const window = {}; window.top = window;
  const context = vm.createContext({ window, location: { protocol: 'app:' }, setTimeout, clearTimeout, console });
  context.fixtureBinding = payload => {
    const request = JSON.parse(payload); host.push(request);
    let value = {};
    if (request.operation === 'inspect') value = { ...current };
    if (request.operation === 'prepare') { const state = states.get(request.threadId); assert.equal(state.status, 'notLoaded'); state.provider = request.targetProvider; }
    queueMicrotask(() => context.__codexDockConnection.settle(request.id, { ok: true, value }));
  };
  vm.runInContext('(' + controller + ')("fixtureBinding")', context);
  const id = '00000000-0000-0000-0000-000000000001'; states.set(id, { status: 'idle', provider: 'openai' });
  const manager = {
    getHostId: () => 'local', getConversation: () => ({ ...selected }),
    resumeThread: parameters => runtime.dispatch(manager, 'thread/resume', parameters, undefined, send)
  };
  async function send(method, parameters, options) {
    native.push({ method, parameters, options }); const state = states.get(parameters?.threadId);
    if (method === 'thread/read') return { thread: { id: parameters.threadId, modelProvider: state.provider, status: { type: state.status }, cwd: 'C:/fixture' } };
    if (method === 'config/read') return { config: { model_provider: current.provider } };
    if (method === 'thread/unsubscribe') { state.status = 'notLoaded'; return { status: 'unsubscribed' }; }
    if (method === 'thread/resume') { state.status = 'idle'; state.provider = parameters.modelProvider; return { thread: { id: parameters.threadId }, modelProvider: state.provider, reasoningEffort: parameters.config?.model_reasoning_effort, serviceTier: parameters.serviceTier, model: parameters.model }; }
    if (method === 'thread/start') return { thread: { id }, modelProvider: 'openai' };
    if (method === 'turn/start') return { turn: { id: 'new-turn', status: 'inProgress' } };
    return {};
  }
  const runtime = context.__codexDockConnection;
  return { context, runtime, manager, send, native, host, current, states, id, selected };
}

test('reconnect refuses active operations and preserves the old controller until they finish', async () => {
  const f = fixture(); let release;
  const gate = new Promise(resolve => { release = resolve; });
  const operation = f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, async (...args) => { await gate; return f.send(...args); });
  await new Promise(resolve => setImmediate(resolve));
  f.runtime.until = 0;
  assert.equal(vm.runInContext('(' + controller + ')("fixtureBinding",true)', f.context), false);
  assert.equal(f.context.__codexDockConnection, f.runtime);
  f.runtime.until = Date.now() + 15000;
  release(); await operation;
  assert.equal(f.runtime.busy, 0);
  f.runtime.stop();
  assert.equal(vm.runInContext('(' + controller + ')("fixtureBinding",true)', f.context), true);
  assert.notEqual(f.context.__codexDockConnection, f.runtime);
  f.context.__codexDockConnection.stop();
});
test('recovery unloads an old runtime, preserves settings and sends original input once', async () => {
  const f = fixture(); const input = { threadId: f.id, clientUserMessageId: 'stable-original-uuid', input: [{ type: 'text', text: 'private-input-retained' }] };
  await f.runtime.dispatch(f.manager, 'turn/start', input, undefined, f.send);
  assert.deepEqual(f.native.map(x => x.method), ['thread/read', 'config/read', 'thread/read', 'thread/unsubscribe', 'thread/read', 'thread/resume', 'turn/start']);
  const resume = f.native.find(x => x.method === 'thread/resume').parameters;
  assert.equal(resume.model, 'chosen-model'); assert.equal(resume.config.model_reasoning_effort, 'max'); assert.equal(resume.serviceTier, 'ultrafast');
  assert.equal(f.native.at(-1).parameters, input);
  assert.ok(f.host.every(x => !JSON.stringify(x).includes('private-input-retained')));
  f.native.length = 0; await f.runtime.dispatch(f.manager, 'turn/start', input, undefined, f.send);
  assert.deepEqual(f.native.map(x => x.method), ['thread/read', 'turn/start']);
  f.current.generation = 'B'; f.native.length = 0; await f.runtime.dispatch(f.manager, 'turn/start', input, undefined, f.send);
  assert.equal(f.native.filter(x => x.method === 'thread/resume').length, 1);
  f.runtime.stop();
});
test('active turns are left on their original connection without index preparation', async () => {
  const f = fixture(); f.states.get(f.id).status = 'active'; f.states.get(f.id).provider = 'legacy';
  await f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, f.send);
  assert.deepEqual(f.native.map(x => x.method), ['thread/read', 'turn/start']);
  assert.equal(f.host.filter(x => x.operation === 'prepare').length, 0); f.runtime.stop();
});
test('optimistic desktop active state does not prevent idle native recovery', async () => {
  const f = fixture(); f.manager.getConversation = () => ({ ...f.selected, threadRuntimeStatus: { type: 'active' } });
  await f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, f.send);
  assert.equal(f.native.filter(x => x.method === 'thread/resume').length, 1); f.runtime.stop();
});
test('project entry overrides are preserved and require explicit reconciliation', async () => {
  const f = fixture(); const send = async (...args) => args[0] === 'config/read' ? { config: { model_provider: 'openai', openai_base_url: 'https://project.invalid/v1' } } : f.send(...args);
  await assert.rejects(f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, send), /项目的模型入口/);
  assert.equal(f.native.filter(x => x.method === 'thread/unsubscribe' || x.method === 'turn/start').length, 0); f.runtime.stop();
});
test('provider mismatch prepares only after unload and updates the same chat', async () => {
  const f = fixture(); f.states.get(f.id).provider = 'legacy';
  const response = await f.runtime.dispatch(f.manager, 'thread/resume', { threadId: f.id, model: 'explicit', config: { model_reasoning_effort: 'high' }, serviceTier: 'priority' }, undefined, f.send);
  assert.equal(response.thread.id, f.id); assert.equal(response.modelProvider, 'openai');
  assert.equal(response.model, 'explicit'); assert.equal(response.reasoningEffort, 'high'); assert.equal(response.serviceTier, 'priority');
  assert.equal(f.host.filter(x => x.operation === 'prepare').length, 1); f.runtime.stop();
});
test('remote hosts and unsupported methods preserve native behavior', async () => {
  const f = fixture(); f.manager.getHostId = () => 'ssh-fixture';
  await f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, f.send);
  assert.deepEqual(f.native.map(x => x.method), ['turn/start']); assert.equal(f.host.length, 0); f.runtime.stop();
});
test('identity or catalog reload requirements block input before mutation', async () => {
  const f = fixture(); f.current.restartRequired = true; f.current.reason = 'reload-required';
  await assert.rejects(f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, f.send), /reload-required/);
  assert.equal(f.native.length, 0); f.runtime.stop();
});
test('concurrent sends serialize per chat, and only one prepares the new generation', async () => {
  const f = fixture();
  await Promise.all([1, 2].map(n => f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id, clientUserMessageId: String(n) }, undefined, f.send)));
  assert.equal(f.native.filter(x => x.method === 'thread/unsubscribe').length, 1);
  assert.equal(f.native.filter(x => x.method === 'thread/resume').length, 1);
  assert.deepEqual(f.native.filter(x => x.method === 'turn/start').map(x => x.parameters.clientUserMessageId), ['1', '2']); f.runtime.stop();
});
test('generation changes during preparation preserve input instead of auto replaying', async () => {
  const f = fixture(); const send = async (...args) => { const value = await f.send(...args); if (args[0] === 'thread/unsubscribe') f.current.generation = 'B'; return value; };
  await assert.rejects(f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, send), /连接已改变/);
  assert.equal(f.native.filter(x => x.method === 'turn/start').length, 0); f.runtime.stop();
});
test('disabled or expired controller returns control to native request code', async () => {
  const f = fixture(); f.runtime.enabled = false;
  await f.runtime.dispatch(f.manager, 'turn/start', { threadId: f.id }, undefined, f.send);
  assert.deepEqual(f.native.map(x => x.method), ['turn/start']); assert.equal(f.host.length, 0); f.runtime.stop();
});
