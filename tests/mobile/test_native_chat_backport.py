"""Regression checks for read-only native metadata and bounded output hydration."""
import json
import os
import sqlite3
import sys
import tempfile
import threading
import unittest
import uuid
from pathlib import Path
from contextlib import closing
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src/CodexMobile'))
from vendor_bootstrap import activate
activate()

from bridge.clients.codex.ipc import StreamCompatibilityError, validate_stream_state
from bridge.features.sessions.create import CreationError, create_empty, read_deferred_item
from bridge.features.sessions.model import normalize_state, merge_saved_message_times
from bridge.features.sessions.store import SessionStore, rollout_message_times
from bridge.features.sessions.timeline import Timeline


class NativeChatTests(unittest.TestCase):
    def setUp(self):
        self.thread, self.child, self.turn, self.other = [str(uuid.uuid4()) for _ in range(4)]

    def test_native_attachment_projection_uses_the_current_store(self):
        with tempfile.TemporaryDirectory() as directory:
            store = SessionStore(directory)
            with closing(sqlite3.connect(Path(directory) / 'state_1.sqlite')) as conn:
                conn.execute('CREATE TABLE threads (id TEXT, title TEXT, cwd TEXT, updated_at INTEGER, '
                             'originator TEXT, source TEXT, archived INTEGER, attached_thread_parent_id TEXT)')
                conn.executemany('INSERT INTO threads VALUES (?,?,?,1,?,NULL,?,?)', [
                    (self.thread, 'parent', '/parent', 'Codex Desktop', 0, None),
                    (self.child, 'child', '/child', 'Codex Desktop', 0, self.thread),
                    (self.turn, 'archived child', '/archived', 'Codex Desktop', 1, self.thread),
                    (self.other, 'foreign', '/foreign', 'unrecognized', 0, self.thread),
                ])
                conn.commit()
            self.assertEqual([self.thread], [item['id'] for item in store.list()])
            self.assertEqual([self.child], [item['id'] for item in store.list(query='child')])
            result = store.attached(self.thread, limit=1)
            self.assertTrue(result['hasMore'])
            ids = {row['id'] for row in result['children'] + store.attached(self.thread, limit=1, offset=1)['children']}
            self.assertEqual({self.child, self.turn}, ids)
            self.assertEqual(self.thread, store.attached(self.child)['parent'])
            with self.assertRaises(KeyError):
                store.attached(self.other)
            self.assertEqual('/child', store.get(self.child)['cwd'])

    def test_v13_requires_a_deferred_item_to_match_its_own_turn(self):
        item = {'type': 'commandExecution', 'id': 'synthetic-item', 'status': 'completed',
                'deferredOutputTurnId': self.turn}
        state = {'id': self.child, 'cwd': '/child', 'modelProvider': 'fixture', 'attachedToThreadId': self.thread,
                 'requests': [], 'threadRuntimeStatus': {'type': 'idle'},
                 'turns': [{'turnId': self.turn, 'status': 'completed', 'items': [item]}]}
        validate_stream_state(13, state)
        for invalid in (self.other, {'id': self.turn}):
            item['deferredOutputTurnId'] = invalid
            with self.assertRaises(StreamCompatibilityError):
                validate_stream_state(13, state)
        validate_stream_state(11, state)

    def test_deferred_details_page_once_and_keep_native_row_identity(self):
        timeline, condition, calls = Timeline(), threading.RLock(), []
        message = {'id': 'one', 'kind': 'commandExecution', 'role': 'activity', 'text': 'fixture', 'deferredOutputTurnId': self.turn}
        turn = {'id': self.turn, 'status': 'completed', 'messages': [message]}
        timeline.update({'sequence': 1, 'turns': [turn]})
        row = timeline.rows[0]
        cursor = timeline.cursor(row['key'])
        def output(*target):
            calls.append(target)
            return 'x' * 20000
        first = timeline.deferred_detail(cursor, condition=condition, read_output=output)
        older = {'id': self.other, 'messages': [{'id': 'old', 'kind': 'userMessage', 'role': 'user', 'text': 'prior'}]}
        timeline.update({'sequence': 2, 'turns': [older, turn]})
        second = timeline.deferred_detail(cursor, first['next'], first['version'], condition=condition, read_output=output)
        self.assertEqual(row['key'], second['key'])
        self.assertEqual('fixture\n\n' + 'x' * 20000, first['text'] + second['text'])
        self.assertEqual([(self.turn, 'one', 'commandExecution')], calls)
        timeline.update({'sequence': 3, 'turns': [older]})
        with self.assertRaises(ValueError):
            timeline.deferred_detail(cursor, condition=condition, read_output=output)

    def test_native_deferred_read_rechecks_thread_directory_and_item(self):
        calls, result_turn, reported = [], self.turn, 'workspace'
        def run(executable, home, cwd, operation, **options):
            def request(method, params):
                calls.append((method, params))
                if method == 'thread/read':
                    return {'thread': {'id': self.child, 'cwd': reported}}
                return {'data': [{'turnId': result_turn, 'outputDeferred': False, 'item': {
                    'id': 'one', 'type': 'commandExecution', 'status': 'completed', 'aggregatedOutput': 'body'}}]}
            return operation(request)
        with patch('bridge.features.sessions.create._runtime_operation', side_effect=run):
            result = read_deferred_item('native', 'home', 'workspace', self.child, self.turn, 'one', 'commandExecution')
            self.assertEqual('body', result['aggregatedOutput'])
            self.assertEqual(('thread/items/read', {'threadId': self.child, 'turnId': self.turn, 'itemIds': ['one']}), calls[-1])
            if os.name == 'nt':
                with tempfile.TemporaryDirectory() as folder:
                    reported = str(Path(folder).resolve())
                    result = read_deferred_item('native', 'home', '\\\\?\\'+reported, self.child, self.turn, 'one', 'commandExecution')
                    self.assertEqual('body', result['aggregatedOutput'])
                reported = 'workspace'
            result_turn = self.other
            with self.assertRaises(CreationError):
                read_deferred_item('native', 'home', 'workspace', self.child, self.turn, 'one', 'commandExecution')

    def test_empty_creation_confirms_saved_history_after_creator_exit(self):
        calls = []
        def run(executable, home, cwd, operation, **options):
            calls.append('open')
            def request(method, params):
                calls.append(method)
                return {'thread': {'id': self.thread}}
            result = operation(request)
            calls.append('exit')
            return result
        with patch('bridge.features.sessions.create._runtime_operation', side_effect=run):
            self.assertEqual(self.thread, create_empty('native', 'home', 'work', ''))
        self.assertEqual(['open', 'thread/start', 'exit', 'open', 'thread/read', 'exit'], calls)

    def test_message_time_is_copied_only_with_matching_native_evidence(self):
        user = {'id': 'user', 'type': 'userMessage', 'content': []}
        output = {'id': 'assistant', 'type': 'agentMessage', 'text': 'result'}
        turn = {'turnId': self.turn, 'turnStartedAtMs': 1790000000000, 'items': [user, output]}
        view = normalize_state({'id': self.thread, 'turns': [turn]})
        rows = view['turns'][0]['messages']
        self.assertEqual(1790000000000, rows[0]['timestamp'])
        self.assertNotIn('timestamp', rows[1])
        stamp = 1790000000400
        times = rollout_message_times(json.dumps({'type': 'event_msg', 'payload': {
            'type': 'item_completed', 'thread_id': self.thread, 'turn_id': self.turn, 'completed_at_ms': stamp,
            'item': {'id': 'assistant', 'type': 'AgentMessage'}}}).encode(), self.thread)
        self.assertEqual(stamp, times[self.turn, 'assistant', 'agentMessage', 'assistant'][0])
        saved_turn = {**turn, 'items': [user, {**output, 'timestamp': stamp}]}
        wrong = normalize_state({'id': self.child, 'turns': [saved_turn]})
        merge_saved_message_times(view, wrong)
        self.assertNotIn('timestamp', rows[1])
        saved = normalize_state({'id': self.thread, 'turns': [saved_turn]})
        merge_saved_message_times(view, saved)
        self.assertEqual(stamp, rows[1]['timestamp'])


if __name__ == '__main__':
    unittest.main()
