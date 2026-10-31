"""Protocol/receipt regression tests with a fake owner; no actual Codex IPC."""
import copy
import json
import sys
import tempfile
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'CodexMobile'))
from service import Bridge
from bridge.clients.codex.ipc import IPCError

SID = '12345678-1234-1234-1234-123456789abc'


class FakeStore:
    def __init__(self):
        self.metadata = {'id': SID, 'origin': 'desktop', 'cwd': 'C:/workspace/example', 'model_provider': 'openai'}
        self.saved = {'id': SID, 'cwd': self.metadata['cwd'], 'modelProvider': 'openai', 'turns': [], 'requests': []}
    def get(self, thread_id):
        if thread_id != SID: raise KeyError(thread_id)
        return copy.deepcopy(self.metadata)
    def list(self, **kwargs): return [copy.deepcopy(self.metadata)]
    def history(self, thread_id, **kwargs): return copy.deepcopy(self.saved)


class FakeIPC:
    def __init__(self, path, on_event, on_disconnect):
        self.on_event, self.on_disconnect = on_event, on_disconnect
        self.snapshot = {'id': SID, 'hostId': 'local', 'cwd': 'C:/workspace/example', 'modelProvider': 'openai',
                         'title': 'Fake desktop chat', 'threadRuntimeStatus': {'type': 'idle'}, 'turns': [], 'requests': []}
        self.calls = []; self.fail = False; self.available = True; self.before_send = None
    def connect(self):
        if not self.available: raise IPCError('not connected')
    def follow(self, thread_id, owner, enabled=True):
        if self.available and enabled: self.event({'type': 'snapshot', 'conversationState': copy.deepcopy(self.snapshot), 'revision': 1})
    def event(self, change, source='owner-1'):
        self.on_event({'method': 'thread-stream-state-changed', 'version': 11, 'sourceClientId': source,
                       'params': {'hostId': 'local', 'conversationId': SID, 'change': change}})
    def request(self, method, payload, target=None, timeout=None):
        if self.before_send: self.before_send()
        self.calls.append((method, payload, target))
        if self.fail: raise IPCError('lost response')
        return {'resultType': 'success', 'result': {}}
    def close(self): pass


class ServiceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.directory = Path(self.temp.name)
        self.store = FakeStore(); self.bridge = Bridge(self.directory, self.directory, store=self.store, ipc_factory=FakeIPC)
    def tearDown(self): self.bridge.close(); self.temp.cleanup()
    def send(self, text='hello', id=None): return self.bridge.mutate(SID, 'send', {'id': id or str(uuid.uuid4()), 'text': text})

    def test_send_uses_original_owner_and_inherits_thread_settings(self):
        result = self.send(); self.assertEqual(result['status'], 'accepted')
        method, payload, owner = self.bridge.ipc.calls[0]
        self.assertEqual(method, 'thread-follower-start-turn'); self.assertEqual(owner, 'owner-1')
        turn = payload['turnStart']; self.assertTrue(turn['context']['inheritThreadSettings'])
        self.assertNotIn('model', turn['request']); self.assertNotIn('cwd', turn['request']); self.assertNotIn('modelProvider', turn['request'])
        self.assertEqual(payload['conversationId'], SID)

    def test_submission_is_durable_before_dispatch_and_duplicate_does_not_repeat(self):
        id = str(uuid.uuid4())
        def durable(): self.assertEqual(json.loads((self.directory / 'operations.json').read_text())[id]['status'], 'unknown')
        self.bridge.ipc.before_send = durable
        self.send(id=id); result = self.send(id=id)
        self.assertTrue(result['duplicate']); self.assertEqual(len(self.bridge.ipc.calls), 1)
        with self.assertRaises(ValueError): self.send('different', id)
        self.bridge.ipc.available = False
        self.bridge._disconnected()
        self.assertTrue(self.send(id=id)['duplicate'])

    def test_lost_response_and_restart_never_replay(self):
        id = str(uuid.uuid4()); self.bridge.ipc.fail = True
        self.assertEqual(self.send(id=id)['status'], 'unknown'); self.bridge.close()
        self.bridge = Bridge(self.directory, self.directory, store=self.store, ipc_factory=FakeIPC)
        result = self.send(id=id)
        self.assertEqual(result['status'], 'unknown'); self.assertTrue(result['duplicate']); self.assertEqual(self.bridge.ipc.calls, [])

    def test_live_prepared_message_is_not_receipt_but_saved_native_message_is(self):
        id = str(uuid.uuid4()); self.bridge.ipc.fail = True; self.send(id=id)
        prepared = copy.deepcopy(self.bridge.ipc.snapshot)
        prepared['turns'] = [{'turnId': 'native-turn', 'status': 'completed', 'items': [{'type': 'userMessage', 'id': 'user', 'clientId': id, 'content': [{'type': 'text', 'text': 'hello'}]}]}]
        self.bridge.ipc.snapshot = prepared
        self.bridge.ipc.event({'type': 'snapshot', 'conversationState': prepared, 'revision': 2})
        self.assertEqual(self.bridge.operations[id]['status'], 'unknown')
        self.bridge.view(SID); self.assertEqual(self.bridge.operations[id]['status'], 'unknown')
        self.store.saved = prepared
        self.bridge.view(SID); self.assertEqual(self.bridge.operations[id]['status'], 'accepted')
        self.assertEqual(len(self.bridge.ipc.calls), 1)

    def test_ledger_failure_prevents_dispatch(self):
        with patch('service.atomic_json', side_effect=OSError('disk full')):
            with self.assertRaises(OSError): self.send()
        self.assertEqual(self.bridge.ipc.calls, [])

    def test_native_active_send_steer_stop_and_expected_turn_guards(self):
        self.bridge.ipc.snapshot.update(threadRuntimeStatus={'type': 'active'}, turns=[{'turnId': 'turn-1', 'status': 'inProgress', 'items': []}])
        with self.assertRaises(ValueError): self.send()
        self.assertEqual(self.bridge.mutate(SID, 'steer', {'id': str(uuid.uuid4()), 'text': 'additional'})['status'], 'accepted')
        with self.assertRaises(ValueError): self.bridge.mutate(SID, 'stop', {'id': str(uuid.uuid4()), 'turnId': 'stale-turn'})
        self.bridge.mutate(SID, 'stop', {'id': str(uuid.uuid4()), 'turnId': 'turn-1'})
        self.bridge.mutate(SID, 'stop', {'id': str(uuid.uuid4()), 'turnId': 'turn-1'})
        self.assertEqual(len(self.bridge.ipc.calls), 2)
        self.assertEqual(self.bridge.ipc.calls[-1][1]['expectedTurnId'], 'turn-1')

    def test_approvals_are_bound_to_pending_request_and_cannot_change_or_repeat(self):
        self.bridge.ipc.snapshot['requests'] = [{'id': 17, 'method': 'item/commandExecution/requestApproval', 'params': {'command': 'fake-command', 'availableDecisions': ['accept', 'decline']}}]
        body = {'id': str(uuid.uuid4()), 'requestId': 17, 'response': {'decision': 'accept'}}
        self.bridge.mutate(SID, 'respond', body)
        other = {**body, 'id': str(uuid.uuid4())}; self.assertTrue(self.bridge.mutate(SID, 'respond', other)['duplicate'])
        self.assertEqual(len(self.bridge.ipc.calls), 1)
        with self.assertRaises(ValueError): self.bridge.mutate(SID, 'respond', {**other, 'id': str(uuid.uuid4()), 'response': {'decision': 'decline'}})
        with self.assertRaises(ValueError): self.bridge.mutate(SID, 'respond', {**other, 'id': str(uuid.uuid4()), 'requestId': 999})
        self.assertEqual(self.bridge.ipc.calls[0][0], 'thread-follower-command-approval-decision')

    def test_patch_revision_and_identity_fail_closed_without_partial_mutation(self):
        self.bridge.view(SID)
        self.bridge.ipc.event({'type': 'patches', 'baseRevision': 1, 'revision': 2,
                               'patches': [{'op': 'replace', 'path': '/title', 'value': 'partial'}, {'op': 'remove', 'path': '/missing/deeper'}]})
        session = self.bridge.session(SID)
        self.assertFalse(session.connected); self.assertEqual(session.state['title'], 'Fake desktop chat')
        self.bridge.attach(session)
        self.bridge.ipc.event({'type': 'patches', 'baseRevision': 999, 'revision': 3, 'patches': []})
        self.assertFalse(session.connected)
        self.bridge.ipc.snapshot['cwd'] = 'C:/different-project'
        self.bridge.attach(session); self.assertFalse(session.connected)
        self.assertEqual(self.bridge.ipc.calls, [])

    def test_foreign_owner_and_non_desktop_origin_cannot_mutate(self):
        self.bridge.view(SID)
        self.bridge.ipc.event({'type': 'patches', 'baseRevision': 1, 'revision': 2, 'patches': [{'op': 'replace', 'path': '/title', 'value': 'foreign'}]}, source='other-owner')
        self.assertEqual(self.bridge.session(SID).state['title'], 'Fake desktop chat')
        self.bridge.close(); self.store.metadata['origin'] = 'cli'
        self.bridge = Bridge(self.directory, self.directory, store=self.store, ipc_factory=FakeIPC)
        with self.assertRaises(ValueError): self.send()

    def test_disconnected_history_is_readonly_and_strips_pending_requests(self):
        self.bridge.ipc.available = False
        self.store.saved['requests'] = [{'id': 1, 'method': 'item/fileChange/requestApproval', 'params': {}}]
        result = self.bridge.view(SID)
        self.assertFalse(result['canWrite']); self.assertEqual(result['status'], 'notLoaded'); self.assertEqual(result['requests'], [])
        with self.assertRaises(IPCError): self.send()
        self.assertEqual(self.bridge.ipc.calls, [])


if __name__ == '__main__': unittest.main()
