"""Disposable Desktop IPC owner used by steering tests; never starts Codex."""
import copy
from contextlib import closing
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'src/CodexMobile'))
from vendor_bootstrap import activate
UPSTREAM = activate()
sys.path.insert(0, str(UPSTREAM / 'tests'))
from test_bridge import DesktopFixture, THREAD


class SteeringOwner(DesktopFixture):
    def __init__(self, home):
        rollout = home / 'sessions/steering.jsonl'
        rollout.parent.mkdir(parents=True)
        rollout.write_text('', encoding='utf-8')
        with closing(sqlite3.connect(home / 'state_5.sqlite')) as database, database:
            database.execute('CREATE TABLE threads(id TEXT PRIMARY KEY, title TEXT, cwd TEXT, '
                             'updated_at INT, archived INT, originator TEXT, source TEXT, rollout_path TEXT)')
            database.execute('INSERT INTO threads VALUES(?,?,?,?,?,?,?,?)',
                             (THREAD, '补充当前任务专项测试', str(home), 1, 0,
                              'Codex Desktop', 'vscode', str(rollout)))
        super().__init__(home)
        self.state.update(title='补充当前任务专项测试', cwd=str(home),
                          threadRuntimeStatus={'type': 'active'},
                          turns=[{'turnId': 'steering-active-turn', 'status': 'inProgress', 'items': []}])

    def send(self, message):
        super().send(message)
        if message.get('type') == 'response' and message.get('method') == 'thread-follower-steer-turn':
            request = next(row for row in reversed(self.requests) if row['requestId'] == message['requestId'])
            params = request['params']
            self.state['turns'][-1]['items'].append({
                'id': params['clientUserMessageId'], 'type': 'steeringUserMessage',
                'status': 'accepted', 'input': copy.deepcopy(params['input'])})
            self.revision += 1
            self.snapshot()

    def set_active(self, active):
        self.state['threadRuntimeStatus'] = {'type': 'active' if active else 'idle'}
        self.state['turns'][-1]['status'] = 'inProgress' if active else 'completed'
        self.revision += 1
        if self.client:
            self.snapshot()

    def status(self, bridge):
        writes = [row for row in self.requests if row.get('method') in (
            'thread-follower-steer-turn', 'thread-follower-start-turn', 'thread-follower-interrupt-turn',
            'thread-follower-update-thread-settings')]
        return {'threadId': THREAD, 'turnId': 'steering-active-turn', 'nativeCalls': copy.deepcopy(writes),
                'runtimeStatus': self.state['threadRuntimeStatus']['type'],
                'submissions': copy.deepcopy(bridge.submissions)}
