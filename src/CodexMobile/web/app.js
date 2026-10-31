'use strict';
(() => {
  const el = id => document.getElementById(id);
  const event = name => document.dispatchEvent(new Event(name));
  const nativeTheme = () => { if (window.BridgeNativeTheme) document.documentElement.dataset.theme = window.BridgeNativeTheme.dark ? 'dark' : 'light'; };
  document.addEventListener('bridge-native-theme', nativeTheme); nativeTheme();
  let auth = null, thread = null, current = null, generation = 0, timer = null, inFlight = false;
  let listOffset = 0, listQuery = '', listGeneration = 0, listLoading = false, renderKey = '', approvalKey = '', namespace = '';
  function read(key, scope = namespace) { try { return localStorage.getItem(scope + key); } catch { return null; } }
  function write(key, value, scope = namespace) { try { if (value === null) localStorage.removeItem(scope + key); else localStorage.setItem(scope + key, value); } catch {} }
  function pending() { try { return JSON.parse(read('pending:' + thread) || 'null'); } catch { return null; } }
  function notice(text, error = false) { el('chat-status').textContent = text; el('chat-status').classList.toggle('error', error); }
  function loginNotice(text, error = false) { el('login-status').textContent = text; el('login-status').classList.toggle('error', error); }
  function unauthenticated(lost = true) {
    generation++; clearTimeout(timer); auth = null; current = null; thread = null;
    el('app').hidden = true; el('app').classList.remove('chat-open'); el('login').hidden = false; el('sessions').replaceChildren(); el('timeline').replaceChildren(); el('approvals').replaceChildren();
    el('message').value = ''; renderKey = approvalKey = ''; loginNotice('请登录电脑上的手机网关。');
    if (lost) event('bridge-auth-lost');
  }
  async function request(path, body, timeout = 12000) {
    const controller = new AbortController(), expiry = setTimeout(() => controller.abort(), timeout);
    try {
      const options = { credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: controller.signal };
      if (body !== undefined) Object.assign(options, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(auth?.csrf ? { 'X-CSRF-Token': auth.csrf } : {}) }, body: JSON.stringify(body) });
      const response = await fetch(path, options);
      const value = await response.json();
      if (!response.ok) {
        if (response.status === 401 && !path.startsWith('/api/login')) unauthenticated();
        const error = new Error(value.error || '请求未完成'); error.status = response.status; throw error;
      }
      return value;
    } finally { clearTimeout(expiry); }
  }
  async function authenticate() {
    const value = await request('/api/auth');
    if (!value.authenticated) { unauthenticated(false); return false; }
    auth = value; namespace = 'codex-dock-mobile:' + value.draftIdentity + ':';
    el('computer-name').textContent = value.computer?.name || '本机 Codex';
    el('connected-computer').dataset.deviceName = value.computer?.name || location.host;
    event('bridge-computer');
    el('login').hidden = true; el('app').hidden = false; el('password').value = '';
    event('bridge-authenticated'); await loadList(true); return true;
  }
  function node(tag, text, className) { const value = document.createElement(tag); if (text != null) value.textContent = text; if (className) value.className = className; return value; }
  async function loadList(reset) {
    if (!auth || !reset && listLoading) return;
    const identity = auth.draftIdentity;
    if (reset) { listGeneration++; listOffset = 0; el('sessions').replaceChildren(); }
    const ticket = listGeneration, query = listQuery, offset = listOffset;
    listLoading = true; el('more').disabled = true;
    el('list-status').textContent = '正在读取已有聊天…';
    try {
      const data = await request('/api/sessions?q=' + encodeURIComponent(query) + '&offset=' + offset);
      if (!auth || auth.draftIdentity !== identity || ticket !== listGeneration) return;
      for (const row of data.sessions) {
        const button = node('button', null, 'session'); button.type = 'button'; button.dataset.id = row.id;
        button.append(node('strong', row.name || row.title || '未命名聊天'), node('small', row.cwd || '本机聊天'));
        button.setAttribute('aria-current', String(thread === row.id)); button.addEventListener('click', () => select(row.id)); el('sessions').append(button);
      }
      listOffset += data.sessions.length; el('more').hidden = data.sessions.length < 100;
      el('list-status').textContent = listOffset ? `已显示 ${listOffset} 个聊天` : '没有找到本机 Codex App 聊天。';
    } catch (error) { if (ticket === listGeneration && auth?.draftIdentity === identity) el('list-status').textContent = error.message; }
    finally { if (ticket === listGeneration) { listLoading = false; el('more').disabled = false; } }
  }
  function saveDraft() { if (thread && namespace) write('draft:' + thread, el('message').value || null); }
  window.saveDraft = saveDraft;
  function select(id) {
    saveDraft(); generation++; clearTimeout(timer); thread = id; current = null; renderKey = approvalKey = '';
    el('timeline').replaceChildren(node('p', '正在读取聊天…', 'empty')); el('approvals').replaceChildren(); el('message').value = read('draft:' + id) || '';
    el('app').classList.add('chat-open');
    for (const button of el('sessions').children) button.setAttribute('aria-current', String(button.dataset.id === id));
    document.dispatchEvent(new CustomEvent('bridge-chat-visible', { detail: { threadId: id, host: 'local' } }));
    controls(); poll(generation);
  }
  function back() {
    if (!thread) return false;
    saveDraft(); generation++; clearTimeout(timer); thread = current = null; el('app').classList.remove('chat-open');
    el('chat-title').textContent = '选择一个聊天'; el('chat-meta').textContent = ''; el('timeline').replaceChildren(); el('approvals').replaceChildren(); controls(); return true;
  }
  window.BridgeNavigation = Object.freeze({ back, accounts: () => { alert('精简版仅支持本机 Codex 聊天。账号与模型连接请在电脑管理。'); return true; } });
  el('back').addEventListener('click', back);
  function controls() {
    const hold = pending(), enabled = !!(thread && current?.canWrite && !inFlight && !hold), active = current?.status === 'active';
    el('message').disabled = !thread || inFlight;
    el('send').disabled = !enabled || current?.status !== 'idle';
    el('steer').hidden = !active; el('steer').disabled = !enabled;
    el('stop').hidden = !active; el('stop').disabled = !enabled;
    for (const button of el('approvals').querySelectorAll('button')) button.disabled = !enabled;
    el('operation-warning').hidden = !hold;
    el('acknowledge').disabled = inFlight;
    if (hold) el('operation-message').textContent = '上次操作结果尚未确认，已暂停新操作。请核对聊天和电脑上的实际结果；刷新不会自动重发。';
  }
  function render(data) {
    current = data; el('chat-title').textContent = data.title || '未命名聊天';
    el('chat-meta').textContent = [data.provider, data.model, data.cwd].filter(Boolean).join(' · ');
    const hold = pending();
    if (hold && data.operations?.some(row => row.id === hold.id && row.status === 'accepted')) {
      write('pending:' + thread, null);
      if ((hold.kind === 'send' || hold.kind === 'steer') && typeof hold.draft === 'string' && el('message').value === hold.draft) {
        el('message').value = ''; write('draft:' + thread, null);
      }
    }
    const nextKey = JSON.stringify(data.turns);
    if (nextKey !== renderKey) {
      const timeline = el('timeline'), bottom = timeline.scrollHeight - timeline.scrollTop - timeline.clientHeight < 90;
      const first = !renderKey; renderKey = nextKey; const fragment = document.createDocumentFragment();
      for (const turn of data.turns || []) {
        const section = node('section', null, 'turn');
        for (const message of turn.messages || []) {
          const block = node('article', null, 'message ' + (message.role || 'activity'));
          block.append(node('div', message.role === 'user' ? '你' : message.role === 'assistant' ? 'Codex' : message.title || '执行状态', 'role'));
          if (message.role === 'activity') {
            const detail = node('details'); detail.append(node('summary', message.title || message.kind || '详情'));
            detail.append(node('pre', typeof message.text === 'string' ? message.text : JSON.stringify(message.text || '')));
            if (message.output) detail.append(node('pre', typeof message.output === 'string' ? message.output : JSON.stringify(message.output)));
            block.append(detail);
          } else block.append(node('div', message.text || ''));
          section.append(block);
        }
        if (turn.error) section.append(node('p', typeof turn.error === 'string' ? turn.error : JSON.stringify(turn.error), 'error'));
        fragment.append(section);
      }
      if (!fragment.childNodes.length) fragment.append(node('p', '此聊天暂时没有可显示的消息。', 'empty'));
      timeline.replaceChildren(fragment); if (bottom || first) timeline.scrollTop = timeline.scrollHeight;
    }
    renderApprovals(data.requests || []); controls();
    if (!inFlight) notice(data.error || (data.connected ? data.status === 'active' ? 'Codex 正在执行，可补充消息或停止。' : '已连接电脑 Codex' : '历史为只读。请在电脑 Codex App 中打开此聊天。'), !!data.error);
  }
  function renderApprovals(requests) {
    const key = JSON.stringify(requests); if (key === approvalKey) return; approvalKey = key; el('approvals').replaceChildren();
    for (const row of requests) {
      const card = node('section', null, 'approval'), params = row.params || {}, method = row.method || '';
      card.append(node('strong', 'Codex 等待处理'));
      function action(label, response) { const button = node('button', label); button.type = 'button'; button.addEventListener('click', () => perform('respond', { requestId: row.id, response: typeof response === 'function' ? response() : response })); card.append(button); }
      if (!row.supported) card.append(node('p', '此请求请在电脑 Codex App 中处理。'));
      else if (method === 'item/commandExecution/requestApproval' || method === 'item/fileChange/requestApproval' || method === 'item/permissions/requestApproval') {
        card.append(node('pre', [params.command, params.cwd, params.reason, params.changes ? JSON.stringify(params.changes, null, 2) : '', params.permissions ? JSON.stringify(params.permissions, null, 2) : ''].filter(Boolean).join('\n')));
        const available = params.availableDecisions;
        for (const [decision, label] of [['accept', '本次批准'], ['decline', '拒绝'], ['cancel', '取消']]) {
          if (method === 'item/permissions/requestApproval' && decision === 'cancel') continue;
          if (!available || available.includes(decision)) action(label, { decision });
        }
      } else if (method === 'item/tool/requestUserInput' || method === 'tool/requestUserInput') {
        const answers = new Map();
        for (const question of params.questions || []) {
          const label = node('label', question.question || question.header || question.id); const input = node('input'); input.maxLength = 20000; input.autocomplete = 'off';
          if (question.options?.length) label.append(node('small', '\n可选：' + question.options.map(option => option.label).join(' / ')));
          label.append(input); card.append(label); answers.set(question.id, input);
        }
        action('提交回答', () => ({ answers: Object.fromEntries([...answers].map(([id, input]) => [id, [input.value]])) }));
      } else card.append(node('p', '此请求请在电脑 Codex App 中处理。'));
      el('approvals').append(card);
    }
  }
  async function poll(ticket) {
    if (!thread || !auth || ticket !== generation) return;
    const id = thread;
    try { const data = await request('/api/sessions/' + id); if (ticket === generation && thread === id) render(data); }
    catch (error) { if (ticket === generation) notice(error.message, true); }
    if (ticket === generation && auth && thread) timer = setTimeout(() => poll(ticket), document.hidden ? 10000 : 2000);
  }
  async function perform(kind, body) {
    if (!thread || inFlight || pending() || !current?.canWrite) return;
    if ((kind === 'send' || kind === 'steer') && !body.text.trim()) return;
    const id = crypto.randomUUID(), target = thread, ticket = generation, original = el('message').value, scope = namespace;
    saveDraft(); write('pending:' + target, JSON.stringify({ id, kind, at: Date.now(), ...((kind === 'send' || kind === 'steer') ? { draft: original } : {}) }));
    if (!read('pending:' + target)) { notice('浏览器无法保存操作标识，请允许此站点本地存储后重试。', true); return; }
    inFlight = true; controls(); notice('正在提交，请稍候…');
    try {
      const result = await request('/api/sessions/' + target + '/' + kind, { id, ...body }, 105000);
      if (result.status === 'accepted') {
        write('pending:' + target, null, scope);
        if ((kind === 'send' || kind === 'steer') && scope === namespace && target === thread && el('message').value === original) { el('message').value = ''; write('draft:' + target, null, scope); }
        if (ticket === generation) notice('操作已被电脑接收');
      } else {
        if (result.id && result.id !== id) write('pending:' + target, JSON.stringify({ id: result.id, kind, at: Date.now() }), scope);
        if (ticket === generation) notice('结果尚未确认，请核对聊天后再操作。', true);
      }
    } catch (error) {
      if (error.status && error.status < 500) write('pending:' + target, null, scope);
      if (ticket === generation) notice(error.status ? error.message : '网络中断，提交结果未知。请核对聊天后再操作。', true);
    } finally { inFlight = false; controls(); if (ticket === generation) { clearTimeout(timer); poll(ticket); } }
  }
  el('login-form').addEventListener('submit', async e => {
    e.preventDefault(); el('login-button').disabled = true; loginNotice('正在登录…');
    try { await request('/api/login', { username: el('username').value, password: el('password').value }); await authenticate(); }
    catch (error) { loginNotice(error.message, true); } finally { el('login-button').disabled = false; }
  });
  el('logout').addEventListener('click', async () => { saveDraft(); try { await request('/api/logout', {}); unauthenticated(false); event('bridge-logged-out'); } catch (error) { notice(error.message, true); } });
  el('message').addEventListener('input', saveDraft);
  el('composer').addEventListener('submit', e => { e.preventDefault(); perform('send', { text: el('message').value }); });
  el('steer').addEventListener('click', () => perform('steer', { text: el('message').value }));
  el('stop').addEventListener('click', () => { const active = [...(current?.turns || [])].reverse().find(turn => turn.status === 'inProgress'); if (active && confirm('停止电脑上当前正在执行的任务？')) perform('stop', { turnId: active.id }); });
  el('acknowledge').addEventListener('click', () => { if (!inFlight && confirm('已核对电脑或聊天中的实际结果？允许新操作后仍不会重发上次操作。')) { write('pending:' + thread, null); controls(); } });
  el('search-form').addEventListener('submit', e => { e.preventDefault(); listQuery = el('search').value; loadList(true); });
  el('more').addEventListener('click', () => loadList(false));
  el('refresh').addEventListener('click', () => { if (thread) { generation++; clearTimeout(timer); poll(generation); } else loadList(true); });
  document.addEventListener('visibilitychange', () => { if (!document.hidden && thread && !inFlight) { generation++; clearTimeout(timer); poll(generation); } });
  window.addEventListener('pagehide', saveDraft);
  let pairQueue = Promise.resolve();
  function consumePair() {
    const fragment = location.hash;
    if (!fragment.startsWith('#pair=')) return false;
    // Clear immediately, including when Android sends a new QR to an existing
    // WebView as fragment-only navigation. Grants are never retried on failure.
    history.replaceState(null, '', location.pathname);
    const token = fragment.slice(6); saveDraft(); unauthenticated(false);
    pairQueue = pairQueue.then(async () => {
      loginNotice('正在配对…');
      try { if (!/^[A-Za-z0-9_-]{43}$/.test(token)) throw new Error('配对码格式不正确'); await request('/api/pair', { token }); await authenticate(); }
      catch (error) { loginNotice(error.message + '，请刷新电脑上的配对码或使用密码登录。', true); }
    });
    return true;
  }
  window.addEventListener('hashchange', consumePair);
  if (!consumePair()) authenticate().catch(error => loginNotice(error.message, true));
})();
