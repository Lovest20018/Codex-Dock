"""Local Codex Desktop follower. No CLI, provider, shell, or generic RPC endpoint."""
import copy
import hashlib
import json
import os
import threading
import time
import uuid
from pathlib import Path

from bridge.clients.codex.ipc import DesktopIPC, IPCError
from bridge.clients.codex.transport import ipc_endpoint
from bridge.features.sessions.model import apply_patches, normalize_state, normalize_request, ordered_turns
from bridge.features.sessions.store import SessionStore, persisted_submission_ids


def identifier(value):
    if not isinstance(value, str) or str(uuid.UUID(value)) != value:
        raise ValueError('操作标识格式不正确')
    return value


def atomic_json(path, value):
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    try:
        with temporary.open('x', encoding='utf-8') as stream:
            json.dump(value, stream, ensure_ascii=False, allow_nan=False)
            stream.flush()
            os.fsync(stream.fileno())
        temporary.chmod(0o600)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


class Session:
    def __init__(self, metadata):
        self.id = metadata['id']
        self.metadata = metadata
        self.owner = None
        self.state = None
        self.revision = None
        self.connected = False
        self.discovering = False
        self.error = None
        self.touched = time.monotonic()
        self.condition = threading.Condition(threading.RLock())
        self.attach_lock = threading.Lock()
        self.action_lock = threading.Lock()


class Bridge:
    def __init__(self, codex_home, data_dir, *, store=None, ipc_factory=DesktopIPC):
        self.store = store or SessionStore(codex_home, source='desktop')
        self.live = {}
        self.lock = threading.RLock()
        self.ledger_lock = threading.RLock()
        self.ledger_path = Path(data_dir) / 'operations.json'
        if self.ledger_path.exists() and self.ledger_path.stat().st_size > 32 * 1024 * 1024:
            raise ValueError('操作记录过大，拒绝启动')
        self.operations = json.loads(self.ledger_path.read_text(encoding='utf-8')) if self.ledger_path.exists() else {}
        if not isinstance(self.operations, dict) or len(self.operations) > 50000 or any(
                not isinstance(row, dict) or row.get('status') not in ('unknown', 'accepted')
                or row.get('kind') not in ('send', 'steer', 'stop', 'respond')
                or not isinstance(row.get('threadId'), str) or not isinstance(row.get('fingerprint'), str)
                for row in self.operations.values()):
            raise ValueError('操作记录损坏，拒绝启动')
        self.ipc = ipc_factory(ipc_endpoint(codex_home), self._event, self._disconnected)
        self.closed = threading.Event()
        self.maintainer = threading.Thread(target=self._maintain, daemon=True)
        self.maintainer.start()

    def list(self, query='', offset=0):
        rows = self.store.list(query=query, limit=100, offset=offset, source='desktop')
        fields = ('id', 'title', 'name', 'cwd', 'model_provider', 'updated_at', 'updated_at_ms')
        return [{key: row.get(key) for key in fields} for row in rows]

    def session(self, thread_id):
        identifier(thread_id)
        with self.lock:
            current = self.live.get(thread_id)
        if current is None:
            metadata = self.store.get(thread_id)
            if metadata.get('origin') != 'desktop':
                raise ValueError('首版仅支持本机 Codex App 聊天')
            with self.lock:
                if len(self.live) >= 64:
                    raise ValueError('打开的聊天过多，请稍后重试')
                current = self.live.setdefault(thread_id, Session(metadata))
        current.touched = time.monotonic()
        return current

    def attach(self, session):
        with session.attach_lock:
            with session.condition:
                if session.connected or self.closed.is_set():
                    return
            try:
                metadata = self.store.get(session.id)
                if metadata.get('origin') != 'desktop':
                    raise IPCError('Not a local Desktop chat')
                session.metadata = metadata
                self.ipc.connect()
                with session.condition:
                    session.owner = None
                    session.revision = None
                    session.discovering = True
                self.ipc.follow(session.id, None)
                with session.condition:
                    session.condition.wait_for(lambda: session.connected or self.closed.is_set(), timeout=1.5)
            except (IPCError, OSError, KeyError, RuntimeError):
                with session.condition:
                    session.discovering = False
                    session.error = '请打开电脑 Codex App，并在其中打开此聊天。'

    def view(self, thread_id):
        session = self.session(thread_id)
        self.attach(session)
        with session.condition:
            state = copy.deepcopy(session.state) if session.connected else None
            connected = session.connected
            error = session.error
        if state is None:
            state = self.store.history(thread_id, turn_limit=12, item_limit=120)
        self._reconcile(session, state if not connected else None)
        result = normalize_state(state, connected, item_limit=120)
        # Saved history is never authority to approve a request or mutate a task.
        if not connected:
            result.update(requests=[], status='notLoaded')
        result['error'] = error
        result['canWrite'] = connected
        with self.ledger_lock:
            result['operations'] = [dict(id=key, **{k: row.get(k) for k in ('kind', 'status', 'at')})
                                    for key, row in self.operations.items() if row['threadId'] == thread_id][-30:]
        return result

    def _event(self, message):
        method = message.get('method')
        if method == 'client-status-changed':
            params = message.get('params', {})
            if params.get('status') == 'disconnected':
                with self.lock:
                    sessions = list(self.live.values())
                for session in sessions:
                    with session.condition:
                        if session.owner == params.get('clientId'):
                            self._invalidate(session)
            return
        if method != 'thread-stream-state-changed':
            return
        if message.get('version') != 11:
            self._disconnected()
            return
        params = message.get('params', {})
        if params.get('hostId') != 'local':
            return
        with self.lock:
            session = self.live.get(params.get('conversationId'))
        if session is None:
            return
        change = params.get('change', {})
        with session.condition:
            source = message.get('sourceClientId')
            if not isinstance(source, str) or not source:
                return
            if session.discovering and change.get('type') == 'snapshot':
                candidate = change.get('conversationState') or {}
                if candidate.get('id') != session.id:
                    return
                session.owner = source
            if source != session.owner:
                return
            try:
                if change.get('type') == 'snapshot':
                    state = copy.deepcopy(change['conversationState'])
                elif change.get('type') == 'patches':
                    if session.revision is None or change.get('baseRevision') != session.revision:
                        raise ValueError('Revision mismatch')
                    state = apply_patches(copy.deepcopy(session.state), change['patches'])
                else:
                    return
                if state.get('id') != session.id or state.get('hostId', 'local') != 'local':
                    raise ValueError('Wrong owner')
                cwd = session.metadata.get('cwd')
                provider = session.metadata.get('model_provider')
                if (cwd and os.path.normcase(os.path.normpath(state.get('cwd') or '')) != os.path.normcase(os.path.normpath(cwd))
                        or provider and state.get('modelProvider') != provider):
                    raise ValueError('Owner identity changed')
                session.state = state
                session.revision = change['revision']
                session.connected = True
                session.discovering = False
                session.error = None
                session.condition.notify_all()
            except (ValueError, KeyError, IndexError, TypeError):
                self._invalidate(session)

    def _reconcile(self, session, saved=None):
        with self.ledger_lock:
            unknown = {key for key, row in self.operations.items() if row['threadId'] == session.id
                       and row['kind'] in ('send', 'steer') and row['status'] == 'unknown'}
        if not unknown:
            return
        try:
            # Only saved native history can confirm a lost response. A live
            # snapshot may contain a merely prepared user message.
            if saved is None:
                saved = self.store.history(session.id, turn_limit=12, item_limit=120)
            receipts = persisted_submission_ids(saved, session.id) & unknown
            if receipts:
                with self.ledger_lock:
                    for key in receipts:
                        self.operations[key]['status'] = 'accepted'
                    atomic_json(self.ledger_path, self.operations)
        except (OSError, ValueError, KeyError, RuntimeError):
            pass

    @staticmethod
    def _invalidate(session):
        session.connected = False
        session.discovering = False
        session.revision = None
        session.error = '电脑连接已变化，请刷新。未确认操作不会自动重发。'
        session.condition.notify_all()

    def _disconnected(self):
        with self.lock:
            sessions = list(self.live.values())
        for session in sessions:
            with session.condition:
                self._invalidate(session)

    def _maintain(self):
        while not self.closed.wait(30):
            with self.lock:
                stale = [self.live.pop(key) for key, session in list(self.live.items()) if time.monotonic() - session.touched > 300]
            for session in stale:
                try:
                    self.ipc.follow(session.id, session.owner, enabled=False)
                except IPCError:
                    pass

    def mutate(self, thread_id, kind, body):
        operation_id = identifier(body.get('id'))
        fingerprint = hashlib.sha256(json.dumps([thread_id, kind, body], sort_keys=True, ensure_ascii=False, allow_nan=False).encode()).hexdigest()
        # This check precedes owner discovery: a retry after a lost HTTP response
        # returns the durable outcome even if the native owner has since gone.
        with self.ledger_lock:
            prior = self.operations.get(operation_id)
            if prior:
                if prior['fingerprint'] != fingerprint:
                    raise ValueError('同一操作标识不能用于不同内容')
                return {'id': operation_id, 'status': prior['status'], 'duplicate': True}
        session = self.session(thread_id)
        self.attach(session)
        with session.action_lock:
            with self.ledger_lock:
                prior = self.operations.get(operation_id)
                if prior:
                    if prior['fingerprint'] != fingerprint:
                        raise ValueError('同一操作标识不能用于不同内容')
                    return {'id': operation_id, 'status': prior['status'], 'duplicate': True}
            with session.condition:
                if not session.connected or not session.owner:
                    raise IPCError('请先在电脑 Codex App 中打开此聊天。')
                method, payload = self._prepare(session, kind, body)
                owner = session.owner
            with self.ledger_lock:
                # Another session can concurrently present the same UUID.
                prior = self.operations.get(operation_id)
                if prior:
                    if prior['fingerprint'] != fingerprint:
                        raise ValueError('同一操作标识不能用于不同内容')
                    return {'id': operation_id, 'status': prior['status'], 'duplicate': True}
                semantic = (kind + ':' + str(body.get('requestId') if kind == 'respond' else body.get('turnId'))) if kind in ('respond', 'stop') else None
                choice = hashlib.sha256(json.dumps({key: value for key, value in body.items() if key != 'id'}, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
                if semantic:
                    for key, row in self.operations.items():
                        if row['threadId'] == thread_id and row.get('semantic') == semantic:
                            if row.get('choice') != choice:
                                raise ValueError('此请求已经提交了不同操作，请核对电脑结果')
                            return {'id': key, 'status': row['status'], 'duplicate': True}
                # Fail closed if storage is full; do not drop old uncertain IDs.
                if len(self.operations) >= 50000:
                    raise ValueError('操作记录已达上限，请在电脑处理后再继续')
                self.operations[operation_id] = {'threadId': thread_id, 'kind': kind, 'status': 'unknown',
                                                  'at': time.time(), 'fingerprint': fingerprint, 'semantic': semantic, 'choice': choice}
                atomic_json(self.ledger_path, self.operations)
            with session.condition:
                if not session.connected or owner != session.owner:
                    # No dispatch occurred, but keep a conservative durable receipt.
                    return {'id': operation_id, 'status': 'unknown'}
                try:
                    if self._prepare(session, kind, body) != (method, payload):
                        return {'id': operation_id, 'status': 'unknown'}
                except ValueError:
                    return {'id': operation_id, 'status': 'unknown'}
            try:
                self.ipc.request(method, {'conversationId': thread_id, **payload}, target=owner,
                                 timeout=90 if kind == 'send' else 30)
            except IPCError:
                with session.condition:
                    self._invalidate(session)
                return {'id': operation_id, 'status': 'unknown'}
            with self.ledger_lock:
                self.operations[operation_id]['status'] = 'accepted'
                atomic_json(self.ledger_path, self.operations)
            return {'id': operation_id, 'status': 'accepted'}

    @staticmethod
    def _prepare(session, kind, body):
        state = session.state
        if kind in ('send', 'steer'):
            if set(body) != {'id', 'text'} or not isinstance(body['text'], str) or not 1 <= len(body['text'].strip()) <= 100000:
                raise ValueError('请输入 1 至 100000 字的消息')
            active = state.get('threadRuntimeStatus', {}).get('type') == 'active'
            if kind == 'send' and state.get('threadRuntimeStatus', {}).get('type') != 'idle':
                raise ValueError('请等待当前任务结束，或使用补充消息')
            if kind == 'steer' and not active:
                raise ValueError('当前任务已经结束，请使用普通发送')
            request = {'threadId': session.id, 'input': [{'type': 'text', 'text': body['text'], 'text_elements': []}],
                       'clientUserMessageId': body['id']}
            context = {'attachments': [], 'commentAttachments': []}
            if kind == 'send':
                return 'thread-follower-start-turn', {'turnStart': {'request': request, 'context': {'inheritThreadSettings': True, **context}}}
            return 'thread-follower-steer-turn', {'input': request['input'], 'clientUserMessageId': body['id'],
                                                 'restoreMessage': {'request': request, 'context': context}, 'attachments': []}
        if kind == 'stop':
            if set(body) != {'id', 'turnId'}:
                raise ValueError('停止操作格式不正确')
            active = next((turn for turn in reversed(ordered_turns(state)) if turn.get('status') == 'inProgress'), None)
            if not active or not active.get('turnId') or active['turnId'] != body['turnId']:
                raise ValueError('任务已变化，请刷新后再停止')
            return 'thread-follower-interrupt-turn', {'mode': 'user-stop', 'expectedTurnId': active['turnId']}
        if kind != 'respond' or set(body) != {'id', 'requestId', 'response'} or not isinstance(body['response'], dict):
            raise ValueError('不支持的操作')
        pending = next((row for row in state.get('requests', []) if str(row.get('id')) == str(body['requestId'])), None)
        if not pending or not normalize_request(pending)['supported']:
            raise ValueError('请求已过期或需要在电脑处理')
        method, params, response = pending.get('method'), pending.get('params', {}), body['response']
        payload = {'requestId': pending['id']}
        if method in ('item/commandExecution/requestApproval', 'item/fileChange/requestApproval'):
            decision = response.get('decision')
            if set(response) != {'decision'} or decision not in ('accept', 'decline', 'cancel') or (params.get('availableDecisions') and decision not in params['availableDecisions']):
                raise ValueError('仅支持此请求允许的本次批准、拒绝或取消')
            payload['decision'] = decision
            return ('thread-follower-command-approval-decision' if method.startswith('item/command') else 'thread-follower-file-approval-decision'), payload
        if method == 'item/permissions/requestApproval':
            if set(response) != {'decision'} or response['decision'] not in ('accept', 'decline'):
                raise ValueError('请选择本次批准或拒绝')
            payload['response'] = {'permissions': params.get('permissions', {}) if response['decision'] == 'accept' else {}, 'scope': 'turn'}
            return 'thread-follower-permissions-request-approval-response', payload
        if method in ('item/tool/requestUserInput', 'tool/requestUserInput'):
            answers = response.get('answers')
            ids = {q['id'] for q in params.get('questions', [])}
            if set(response) != {'answers'} or not isinstance(answers, dict) or not ids or set(answers) != ids or any(
                    not isinstance(value, list) or not value or not all(isinstance(item, str) and 0 < len(item) <= 20000 for item in value) for value in answers.values()):
                raise ValueError('请完整回答所有问题')
            payload['response'] = {'answers': {key: {'answers': value} for key, value in answers.items()}}
            return 'thread-follower-submit-user-input', payload
        # Plan implementation, external forms and identity challenges stay in
        # Desktop in this text-only first version. No fallback arbitrary RPC.
        raise ValueError('此请求请在电脑 Codex App 中处理')

    def close(self):
        self.closed.set()
        self.ipc.close()
        self.maintainer.join(timeout=1)
