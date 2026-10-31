"""Steering contracts against disposable IPC and CLI protocol fixtures."""
import copy
import sys
import tempfile
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
from steering_fixture import SteeringOwner, THREAD
from bridge.app.service import Bridge, SubmissionRejected
from bridge.clients.codex.ipc import IPCError
from bridge.clients.codex.app_server import AppServerUnavailable, AppServerUnsupported
import test_codex_cli_session as cli_fixture


class DesktopSteeringTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='dock-steer-')
        home = Path(self.temp.name)
        self.owner = SteeringOwner(home)
        self.bridge = Bridge(home, home / 'data', codex_bin=home / 'missing-codex.exe', ipc_path=self.owner.path)
        self.assertTrue(self.bridge.view(THREAD)['connected'])

    def tearDown(self):
        self.bridge.close()
        self.owner.close()
        self.temp.cleanup()

    def test_active_steer_reaches_same_owner_once_and_preserves_settings(self):
        before = copy.deepcopy(self.owner.state)
        identifier = str(uuid.uuid4())
        result = self.bridge.send(THREAD, '请先核对输入区，再继续当前任务。', identifier, 'steer')
        self.assertEqual(result['status'], 'accepted')
        self.assertTrue(self.bridge.send(THREAD, '请先核对输入区，再继续当前任务。', identifier, 'steer')['duplicate'])
        calls = self.owner.status(self.bridge)['nativeCalls']
        self.assertEqual([row['method'] for row in calls], ['thread-follower-steer-turn'])
        self.assertEqual(calls[0]['targetClientId'], 'owner')
        self.assertEqual(calls[0].get('hostId', 'local'), 'local')
        self.assertEqual(calls[0]['params']['conversationId'], THREAD)
        self.assertEqual(calls[0]['params']['clientUserMessageId'], identifier)
        self.assertEqual(calls[0]['params']['input'][0]['text'], '请先核对输入区，再继续当前任务。')
        self.assertNotIn('collaborationMode', calls[0]['params']['restoreMessage']['request'])
        for name in ('id', 'cwd', 'modelProvider', 'latestModel', 'threadRuntimeStatus'):
            self.assertEqual(self.owner.state[name], before[name])

    def test_finished_turn_rejects_steer_without_recording_or_starting_a_turn(self):
        self.owner.set_active(False)
        session = self.bridge.live[THREAD]
        with session.condition:
            self.assertTrue(session.condition.wait_for(lambda: session.state['threadRuntimeStatus']['type'] == 'idle', 3))
        identifier = str(uuid.uuid4())
        with self.assertRaises(SubmissionRejected):
            self.bridge.send(THREAD, '任务已结束时的补充', identifier, 'steer')
        self.assertNotIn(THREAD + ':' + identifier, self.bridge.submissions)
        self.assertEqual(self.owner.status(self.bridge)['nativeCalls'], [])

    def test_steer_cannot_change_work_mode(self):
        with self.assertRaisesRegex(ValueError, '不能切换工作模式'):
            self.bridge.send(THREAD, '继续', str(uuid.uuid4()), 'steer', work_mode='plan')
        self.assertEqual(self.owner.status(self.bridge)['nativeCalls'], [])

    def test_uncertain_steer_is_retained_and_not_replayed_on_retry(self):
        identifier = str(uuid.uuid4())
        with patch.object(self.bridge, '_call', side_effect=IPCError('Synthetic ACK timeout')) as dispatch:
            with self.assertRaises(IPCError):
                self.bridge.send(THREAD, '发送结果未知时不要重放', identifier, 'steer')
            self.assertEqual(self.bridge.send(THREAD, '发送结果未知时不要重放', identifier, 'steer')['status'], 'unknown')
            dispatch.assert_called_once()
        self.assertEqual(self.bridge.submissions[THREAD + ':' + identifier]['status'], 'unknown')


class CliSteeringTests(unittest.TestCase):
    def setUp(self):
        self.case = cli_fixture.CliSessionTests()
        self.case.setUp()
        self.addCleanup(self.case.temp.cleanup)
        self.addCleanup(self.case.sessions.close)
        self.sessions, self.client = self.case.sessions, self.case.fake
        self.client.thread['status'] = {'type': 'active'}
        self.client.thread['turns'] = [{'id': 'cli-current-turn', 'status': 'inProgress', 'items': []}]
        self.sessions.attach('thread-a', explicit=True)

    def test_cli_steer_binds_current_turn_and_preserves_provider_and_working_directory(self):
        self.sessions.call('thread-a', 'thread-follower-steer-turn', {
            'input': [{'type': 'text', 'text': '现在补充一句'}], 'clientUserMessageId': 'steer-fixture'})
        writes = [(method, params) for method, params in self.client.sent if method in ('turn/start', 'turn/steer', 'turn/interrupt')]
        self.assertEqual(len(writes), 1)
        method, params = writes[0]
        self.assertEqual(method, 'turn/steer')
        self.assertEqual(params['threadId'], 'thread-a')
        self.assertEqual(params['expectedTurnId'], 'cli-current-turn')
        self.assertEqual(params['clientUserMessageId'], 'steer-fixture')
        self.assertEqual(params['input'][0]['text'], '现在补充一句')
        self.assertNotIn('collaborationMode', params)
        self.assertNotIn('model', params)
        self.assertEqual(self.client.thread['modelProvider'], 'original-provider')
        self.assertEqual(self.client.thread['cwd'], '/original/project')

    def test_cli_steer_with_files_is_explicitly_rejected(self):
        with self.assertRaises(AppServerUnsupported):
            self.sessions.call('thread-a', 'thread-follower-steer-turn', {
                'input': [{'type': 'text', 'text': '检查附件'}],
                'attachments': [{'path': '/fixture/file.txt'}]})
        self.assertFalse(any(method == 'turn/steer' for method, _ in self.client.sent))

    def test_cli_steer_without_a_running_turn_does_not_start_one(self):
        with self.sessions.lock:
            self.sessions.states['thread-a']['turns'] = []
        with self.assertRaises(AppServerUnavailable):
            self.sessions.call('thread-a', 'thread-follower-steer-turn', {'input': [], 'clientUserMessageId': 'finished'})
        self.assertFalse(any(method in ('turn/start', 'turn/steer') for method, _ in self.client.sent))


if __name__ == '__main__':
    unittest.main(verbosity=2)
