"""v2.2.1 protocol and recovery subset through isolated native IPC fixtures."""
import copy
import sys
import threading
import time
import unittest
from concurrent.futures import ThreadPoolExecutor, TimeoutError
from types import SimpleNamespace
from unittest.mock import Mock, patch

import test_workbench

sys.path.append(str(test_workbench.UPSTREAM / 'tests'))
import test_bridge as native_fixture
from bridge.app.service import LiveSession
from bridge.clients.codex.ipc import DesktopIPC, IPCError, StreamCompatibilityError, validate_stream_state
from bridge.clients.errors import TaskStateUnavailable
from bridge.features.sessions.model import apply_patches


class StreamContractTests(unittest.TestCase):
    def state(self):
        return {'id': 'thread', 'modelProvider': 'original-provider', 'cwd': '/original/project',
                'threadRuntimeStatus': {'type': 'idle'}, 'requests': [],
                'turns': [{'status': 'completed', 'items': [{'type': 'agentMessage', 'text': 'history'}]}]}

    def test_v11_and_structurally_compatible_new_versions(self):
        validate_stream_state(11, {'id': 'thread'})
        for version in (12, 13, 14, 99):
            value = self.state()
            value['futureField'] = {'anything': True}
            value['turns'][0]['extra'] = 1
            validate_stream_state(version, value)

    def test_control_collections_must_remain_readable(self):
        changes = [('modelProvider', None), ('cwd', None), ('requests', None), ('requests', [{}]), ('turns', {}),
                   ('turns', [{'status': 'future-status', 'items': []}]),
                   ('turns', [{'status': 'completed', 'items': [None]}]),
                   ('turnHistory', {'kind': 'new-history'}), ('turnHistory', None),
                   ('threadRuntimeStatus', {'type': 'future-runtime'})]
        for version in (13, 99):
            for key, value in changes:
                state = self.state()
                state[key] = value
                with self.subTest(version=version, key=key), self.assertRaises(StreamCompatibilityError):
                    validate_stream_state(version, state)

    def test_canonical_history_and_pending_approvals(self):
        value = self.state()
        value['turnHistory'] = {'kind': 'canonical', 'history': {
            'entitiesByKey': {'key': value['turns'][0]}, 'islands': [{'entries': [{'value': 'key'}]}]}}
        value['requests'] = [{'id': 1, 'method': 'item/commandExecution/requestApproval', 'params': {}}]
        validate_stream_state(99, value)
        value['turnHistory']['history']['islands'][0]['entries'][0]['value'] = 'missing'
        with self.assertRaises(StreamCompatibilityError):
            validate_stream_state(99, value)

    def test_empty_canonical_items_before_first_output(self):
        value = self.state()
        value['turns'] = [{'status': 'inProgress', 'items': {'entitiesByKey': {}, 'islands': [], 'isComplete': True}}]
        validate_stream_state(13, value)

    def test_invalid_or_older_versions(self):
        for version in (None, True, '14', 10, -1):
            with self.subTest(version=version), self.assertRaises(StreamCompatibilityError):
                validate_stream_state(version, self.state())

    def test_rejected_patch_does_not_corrupt_last_validated_snapshot(self):
        value = self.state()
        before = copy.deepcopy(value)
        patched = apply_patches(value, [{'op': 'replace', 'path': ['requests'], 'value': None}])
        with self.assertRaises(StreamCompatibilityError):
            validate_stream_state(13, patched)
        self.assertEqual(value, before)
        with self.assertRaises(KeyError):
            apply_patches(value, [{'op': 'replace', 'path': ['turns', '0', 'status'], 'value': 'active'},
                                  {'op': 'replace', 'path': ['missing', 'child'], 'value': 'bad'}])
        self.assertEqual(value, before)


class StreamIntegrationTests(unittest.TestCase):
    tearDown = native_fixture.IntegrationTests.tearDown

    def setUp(self):
        native_fixture.IntegrationTests.setUp(self)
        # The v11 fixture predates required cwd in the v13 control snapshot.
        self.fixture.state['cwd'] = '/workspace'

    def event(self, state=None, *, change=None, version=13, owner='owner', host='local'):
        self.bridge._event({'method': 'thread-stream-state-changed', 'sourceClientId': owner, 'version': version,
                            'params': {'hostId': host, 'conversationId': native_fixture.THREAD,
                                       'change': change or {'type': 'snapshot', 'revision': 2, 'conversationState': state}}})

    def test_v13_real_ipc_retains_original_thread_provider_and_single_send(self):
        def publish(change):
            self.fixture.send({'type': 'broadcast', 'sourceClientId': 'owner',
                               'method': 'thread-stream-state-changed', 'version': 13,
                               'params': {'hostId': self.fixture.host, 'conversationId': native_fixture.THREAD, 'change': change}})
        self.fixture.publish = publish
        native_fixture.IntegrationTests.test_live_read_send_same_thread_and_idempotence(self)
        self.assertEqual(self.bridge.live[native_fixture.THREAD].desktop_stream_version, 13)

    def test_unknown_control_structure_is_read_only_and_compatible_snapshot_restores_controls(self):
        session = self.bridge.session(native_fixture.THREAD)
        self.fixture.loaded = False
        valid = copy.deepcopy(session.state)
        bad = copy.deepcopy(valid)
        bad['requests'] = None
        self.event(bad, version=99)
        self.assertFalse(session.connected)
        self.assertTrue(session.access['readOnly'])
        self.assertFalse(any(value for key, value in session.access.items() if key.startswith('can')))
        self.assertEqual(session.state, valid)
        with patch.object(self.bridge.ipc, 'request') as request, patch('bridge.app.service.open_in_desktop') as navigate:
            with self.assertRaisesRegex(IPCError, '更新电脑端 Dock'):
                self.bridge._call(session, 'thread-follower-start-turn', {})
            with self.assertRaises(IPCError):
                self.bridge.activate(session.id)
            request.assert_not_called()
            navigate.assert_not_called()
        self.event(valid, version=99)
        self.assertTrue(session.connected)
        self.assertFalse(session.access['readOnly'])
        self.assertIsNone(session.desktop_stream_error)

    def test_rejected_patch_is_atomic_and_owner_host_guards_remain(self):
        session = self.bridge.session(native_fixture.THREAD)
        self.fixture.loaded = False
        valid = copy.deepcopy(session.state)
        before_sequence = session.sequence
        self.event({'id': session.id}, owner='other-owner')
        self.event({'id': session.id}, host='other-host')
        self.assertEqual(session.sequence, before_sequence)
        self.assertTrue(session.connected)
        self.event(change={'type': 'patches', 'revision': 2, 'baseRevision': session.revision,
                           'patches': [{'op': 'replace', 'path': ['requests'], 'value': None}]})
        self.assertFalse(session.connected)
        self.assertEqual(session.state, valid)
        self.assertIsNotNone(session.desktop_stream_error)

    def test_mismatched_revision_does_not_accept_foreign_patch(self):
        session = self.bridge.session(native_fixture.THREAD)
        self.fixture.loaded = False
        valid = copy.deepcopy(session.state)
        self.event(change={'type': 'patches', 'revision': 2, 'baseRevision': 999,
                           'patches': [{'op': 'replace', 'path': ['title'], 'value': 'wrong'}]})
        self.assertFalse(session.connected)
        self.assertEqual(session.state, valid)

    def test_unrelated_future_frame_does_not_disconnect_other_threads(self):
        session = self.bridge.session(native_fixture.THREAD)
        self.bridge._event({'method': 'thread-stream-state-changed', 'version': 999,
                            'params': {'hostId': 'local', 'conversationId': 'unfollowed', 'change': {}}})
        self.assertTrue(session.connected)
        for value in (None, [], 'unknown'):
            self.bridge._event(value)
            self.bridge._event({'method': 'client-status-changed', 'params': value})
            self.bridge._event({'method': 'thread-stream-state-changed', 'params': {'hostId': 'local', 'conversationId': value}})
        self.assertTrue(session.connected)

    def test_v13_lifecycle_audit_keeps_running_and_approval_guards(self):
        def publish(change):
            self.fixture.send({'type': 'broadcast', 'sourceClientId': 'owner',
                               'method': 'thread-stream-state-changed', 'version': 13,
                               'params': {'hostId': self.fixture.host, 'conversationId': native_fixture.THREAD, 'change': change}})
        self.fixture.publish = publish
        self.bridge.assert_desktop_idle(timeout=2, identifiers=[native_fixture.THREAD])
        self.fixture.state['threadRuntimeStatus'] = {'type': 'active'}
        with self.assertRaisesRegex(ValueError, '运行|确认'):
            self.bridge.assert_desktop_idle(timeout=2, identifiers=[native_fixture.THREAD])
        self.fixture.state['threadRuntimeStatus'] = {'type': 'idle'}
        self.fixture.state['requests'] = [{'id': 'approval', 'method': 'item/commandExecution/requestApproval', 'params': {}}]
        with self.assertRaisesRegex(ValueError, '运行|确认'):
            self.bridge.assert_desktop_idle(timeout=2, identifiers=[native_fixture.THREAD])

    def test_lifecycle_deadline_rejects_a_stalled_initialization(self):
        entered, release = threading.Event(), threading.Event()
        def blocked(*args, **kwargs):
            entered.set()
            release.wait(2)
        started = time.monotonic()
        try:
            with patch('bridge.app.service.DesktopIPC.connect', side_effect=blocked):
                with self.assertRaises(TaskStateUnavailable):
                    self.bridge.assert_desktop_idle(timeout=.1, identifiers=[native_fixture.THREAD])
            self.assertTrue(entered.is_set())
            self.assertLess(time.monotonic() - started, 1)
        finally:
            release.set()

    def test_explicit_reconnect_replaces_follower_and_never_starts_native_app(self):
        session = self.bridge.session(native_fixture.THREAD)
        old_socket = self.bridge.ipc.socket
        self.bridge._disconnected()
        with patch.object(self.bridge, '_refresh_async'), patch('bridge.app.service.open_in_desktop') as navigate:
            recovered = self.bridge.activate(session.id)
        self.assertTrue(recovered.connected)
        self.assertIsNot(self.bridge.ipc.socket, old_socket)
        navigate.assert_not_called()
        self.assertEqual(recovered.state['modelProvider'], 'custom-api')

    def test_transport_recovery_waits_for_inflight_host_operation(self):
        session = self.bridge.session(native_fixture.THREAD)
        self.bridge._disconnected()
        entered = threading.Event()
        def recover():
            entered.set()
            return self.bridge.rebuild_connection(session.id)
        with patch.object(self.bridge, '_refresh_async'), ThreadPoolExecutor(max_workers=1) as pool:
            with self.bridge.accounts.gate.host('local'):
                future = pool.submit(recover)
                self.assertTrue(entered.wait(1))
                self.assertFalse(future.done())
            self.assertTrue(future.result(timeout=5).connected)

    def test_late_background_probe_cannot_reinstate_old_backoff(self):
        session = self.bridge.session(native_fixture.THREAD)
        entered, release = threading.Event(), threading.Event()
        session.connected = False
        with patch.object(self.bridge, '_attach', side_effect=lambda *a, **k: (entered.set(), release.wait(2))):
            self.bridge._refresh_async(session, force=True)
            self.assertTrue(entered.wait(1))
            self.bridge._invalidate(session, 'disconnected', runtime='desktop', reason='desktop_ipc_disconnected')
            release.set()
            with session.condition:
                self.assertTrue(session.condition.wait_for(lambda: not session.connecting, timeout=2))
                self.assertEqual(session.retry_at, 0)

    def test_unknown_write_is_never_replayed_during_recovery(self):
        session = self.bridge.session(native_fixture.THREAD)
        with patch.object(self.bridge.ipc, 'request', side_effect=IPCError('no-client-found')) as request:
            with self.assertRaises(IPCError):
                self.bridge._call(session, 'thread-follower-start-turn', {'text': 'only once'})
        self.assertEqual(request.call_count, 1)
        self.assertFalse(session.connected)
        self.assertTrue(session.rebuild_recommended)

    def test_maintenance_keeps_short_polling_during_observed_router_recovery(self):
        session = LiveSession('fixture')
        session.desktop_recovery_until = time.monotonic() + 6
        waits = []
        def wait(delay):
            waits.append(delay)
            return len(waits) > 1
        bridge = SimpleNamespace(closed=SimpleNamespace(wait=wait), store=object(), accounts=None,
            lock=threading.Lock(), live={'fixture': session}, submit_lock=threading.Lock(), submissions={},
            ipc=SimpleNamespace(client_id=None))
        native_fixture.Bridge._maintain(bridge)
        self.assertEqual(waits, [3, 3])


class IpcDeadlineTests(unittest.TestCase):
    def test_close_cancels_late_connect_before_initialization(self):
        entered, release = threading.Event(), threading.Event()
        stream = Mock()
        ipc = DesktopIPC('fixture')
        ipc.request = Mock()
        def connect(*args, **kwargs):
            entered.set()
            release.wait(1)
            return stream
        with patch('bridge.clients.codex.ipc.connect_stream', side_effect=connect), ThreadPoolExecutor(max_workers=1) as pool:
            future = pool.submit(ipc.connect, deadline=time.monotonic() + 1)
            self.assertTrue(entered.wait(1))
            ipc.close()
            release.set()
            with self.assertRaises(IPCError):
                future.result(timeout=1)
        self.assertIsNone(ipc.socket)
        ipc.request.assert_not_called()
        stream.close.assert_called_once()

    def test_late_initialization_cannot_restore_closed_connection(self):
        entered, release = threading.Event(), threading.Event()
        stream = Mock()
        ipc = DesktopIPC('fixture')
        ipc._read_loop = Mock()
        def initialize(*args, **kwargs):
            entered.set()
            release.wait(1)
            return {'result': {'clientId': 'late'}}
        ipc.request = Mock(side_effect=initialize)
        with patch('bridge.clients.codex.ipc.connect_stream', return_value=stream), ThreadPoolExecutor(max_workers=1) as pool:
            future = pool.submit(ipc.connect, deadline=time.monotonic() + 1)
            self.assertTrue(entered.wait(1))
            ipc.close()
            release.set()
            with self.assertRaises(IPCError):
                future.result(timeout=1)
        self.assertIsNone(ipc.socket)
        self.assertIsNone(ipc.client_id)

    def test_read_deadline_deducts_send_time(self):
        clock = [10.0]
        future = Mock(result=Mock(side_effect=TimeoutError))
        ipc = DesktopIPC('fixture')
        ipc._send = Mock(side_effect=lambda message: clock.__setitem__(0, 10.25))
        with patch('bridge.clients.codex.ipc.time', SimpleNamespace(monotonic=lambda: clock[0])), \
                patch('bridge.clients.codex.ipc.Future', return_value=future):
            with self.assertRaisesRegex(IPCError, '读取超时'):
                ipc.request('thread-owner-discovery', {}, timeout=6, deadline=10.5)
        self.assertEqual(ipc._send.call_args.args[0]['timeoutMs'], 500)
        future.result.assert_called_once_with(timeout=.25)
        self.assertEqual(ipc.pending, {})

    def test_expired_deadline_does_not_send(self):
        ipc = DesktopIPC('fixture')
        ipc._send = Mock()
        with patch('bridge.clients.codex.ipc.time', SimpleNamespace(monotonic=lambda: 10)):
            with self.assertRaisesRegex(IPCError, '读取超时'):
                ipc.request('thread-owner-discovery', {}, deadline=10)
        ipc._send.assert_not_called()
        self.assertEqual(ipc.pending, {})


if __name__ == '__main__':
    unittest.main()
