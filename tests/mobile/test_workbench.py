"""Real adapter/HTTP regression using temporary homes, never real conversations."""
import copy
import http.client
import json
import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / 'src/CodexMobile'
sys.path.insert(0, str(SOURCE))
from vendor_bootstrap import activate
UPSTREAM = activate()
from bridge.app.service import Bridge
from bridge.features.auth.auth import password_record
from gateway import PROTOCOL, Server
from local_manager import ManagerControlHandler
from main import validate
from workbench import Workbench

ORIGIN = 'https://phone.example.com'
AGENT = 'BridgeMobile/2.1.7-Android'
PASSWORD = 'isolated-mobile-password'
AUTH = {'mode': 'password', 'username': 'tester', 'sessionHours': 12, **password_record(PASSWORD)}


class WorkbenchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        self.home = self.directory / 'home'
        self.home.mkdir()
        self.config = {'protocol': PROTOCOL, 'publicUrl': ORIGIN, 'port': 0, 'controlPort': 13091,
                       'controlToken': 'b' * 64, 'instanceId': 'a' * 32, 'auth': copy.deepcopy(AUTH),
                       'codexHome': str(self.home), 'dataDir': str(self.directory)}
        self.cookie = self.csrf = None
        self.open()

    def open(self):
        self.bridge = Bridge(self.home, self.directory, codex_bin=self.home / 'missing-fixture.exe',
                             ipc_path=str(self.home / 'missing-ipc'))
        self.work = Workbench(self.config, self.bridge, UPSTREAM / 'web')
        self.public = self.work.public
        self.control = Server(('127.0.0.1', 0), ManagerControlHandler, self.work.state)
        self.config.update(port=self.public.server_port, controlPort=self.control.server_port)
        self.threads = [threading.Thread(target=server.serve_forever, daemon=True)
                        for server in (self.public, self.control)]
        for thread in self.threads:
            thread.start()

    def close(self):
        for server in (self.public, self.control):
            server.shutdown()
        for thread in self.threads:
            thread.join(timeout=2)
        self.control.server_close()
        self.work.close()
        self.bridge.close()

    def tearDown(self):
        self.close()
        self.temp.cleanup()

    def request(self, path, body=None, *, private=False, headers=None):
        server = self.control if private else self.public
        values = {'Host': f'127.0.0.1:{server.server_port}' if private else 'phone.example.com', 'User-Agent': AGENT}
        if private:
            values['Authorization'] = 'Bearer ' + self.config['controlToken']
        if self.cookie:
            values['Cookie'] = self.cookie
        if body is not None:
            values['Content-Type'] = 'application/json'
            if not private:
                values['Origin'] = ORIGIN
            if self.csrf and not private:
                values['X-CSRF-Token'] = self.csrf
        values.update(headers or {})
        values = {key: value for key, value in values.items() if value is not None}
        conn = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=10)
        try:
            conn.request('POST' if body is not None else 'GET', path,
                         json.dumps(body).encode() if body is not None else None, values)
            response = conn.getresponse()
            payload, result_headers = response.read(), dict(response.getheaders())
            value = json.loads(payload) if result_headers.get('Content-Type', '').startswith('application/json') else payload
            return response.status, value, result_headers
        finally:
            conn.close()

    def login(self):
        code, body, headers = self.request('/api/login', {'username': 'tester', 'password': PASSWORD})
        self.assertEqual(code, 200, body)
        self.cookie = headers['Set-Cookie'].split(';')[0]
        self.csrf = body['csrf']
        self.assertIn('; Secure', headers['Set-Cookie'])
        self.assertIn('; HttpOnly', headers['Set-Cookie'])

    def test_full_auth_contract_and_original_upstream_assets(self):
        self.login()
        code, value, _ = self.request('/api/auth')
        self.assertEqual(code, 200)
        self.assertEqual(value['enabledClients'], ['codex'])
        self.assertTrue(value['notifications'])
        self.assertEqual(value['transport'], 'sse')
        self.assertEqual(value['instanceId'], self.config['instanceId'])
        for path in ('/', '/app.js', '/style.css', '/host.js', '/layout.js', '/accounts.js',
                     '/cli-access.js', '/timeline.js', '/vendor/xterm/xterm.js'):
            code, content, _ = self.request(path)
            self.assertEqual(code, 200, path)
            self.assertGreater(len(content), 100, path)

    def test_client_status_reads_stay_responsive_during_native_inspection(self):
        self.login()
        manager = self.public.desktop_sessions
        manager.client_cache = None
        entered, release = threading.Event(), threading.Event()

        def slow_inspection(refresh=False):
            entered.set()
            release.wait(3)
            return {'clients': []}

        with patch.object(manager, 'clients', side_effect=slow_inspection) as inspect:
            try:
                for _ in range(4):
                    started = time.monotonic()
                    code, value, _ = self.request('/api/clients')
                    self.assertEqual(code, 200)
                    self.assertTrue(value['pending'])
                    self.assertLess(time.monotonic() - started, 0.5)
                self.assertTrue(entered.wait(1))
                self.assertEqual(inspect.call_count, 1)
            finally:
                release.set()
                deadline = time.monotonic() + 2
                while manager.client_refreshing and time.monotonic() < deadline:
                    time.sleep(0.01)

    def test_public_host_origin_auth_and_local_control_boundaries(self):
        self.assertEqual(self.request('/api/accounts')[0], 401)
        self.login()
        self.assertEqual(self.request('/api/auth', headers={'Host': 'evil.test'})[0], 403)
        self.assertEqual(self.request('/api/auth', headers={'Origin': 'https://evil.test'})[0], 403)
        self.assertEqual(self.request('/api/logout', {}, headers={'X-CSRF-Token': None})[0], 403)
        for path in ('/health', '/stop', '/manager', '/manager/', '/api/accounts/login'):
            self.assertEqual(self.request(path)[0], 404, path)
        self.assertEqual(self.request('/health', private=True)[0], 200)
        self.assertEqual(self.request('/health', private=True, headers={'Origin': ORIGIN})[0], 403)
        self.assertEqual(self.request('/health', private=True, headers={'Authorization': None})[0], 403)
        local_host = {'Host': f'127.0.0.1:{self.public.server_port}'}
        self.assertIn('手机服务已运行', self.request('/', headers=local_host)[1].decode())
        self.assertEqual(self.request('/api/login', {}, headers={**local_host, 'Origin': 'http://' + local_host['Host']})[0], 403)

    def test_other_clients_stay_unavailable_on_all_management_routes(self):
        self.login()
        for path in ('/harness', '/harness/', '/api/harness', '/api/desktop-sessions/claude/list',
                     '/api/desktop-sessions/deepseek/list', '/api/clients/claude/reconnect'):
            self.assertEqual(self.request(path)[0], 404, path)
        self.assertEqual(self.request('/api/clients', {'provider': 'claude', 'enabled': True})[0], 400)
        self.assertEqual(self.request('/api/cli-access', {'provider': 'claude', 'action': 'importCurrent'})[0], 400)
        self.assertEqual(self.request('/api/wsl-accounts/environments?provider=claude')[0], 400)
        self.assertEqual(self.work.public.desktop_sessions.navigation_clients(), ['codex'])

    def test_shared_proxy_lock_preserves_cookie_and_native_push_after_restart(self):
        self.login()
        key = self.work.state.auth.key(self.cookie.split('=', 1)[1])
        for attempt in range(5):
            self.request('/api/login', {'username': 'tester', 'password': 'wrong-password'},
                         headers={'X-Forwarded-For': f'198.51.100.{attempt + 1}'})
        self.assertEqual(self.request('/api/login', {'username': 'tester', 'password': PASSWORD})[0], 403)
        self.assertTrue(self.request('/api/auth')[1]['authenticated'])
        self.assertTrue(self.work.valid_push_session(key))
        self.close()
        self.config['port'] = 0
        self.open()
        self.assertTrue(self.request('/api/auth')[1]['authenticated'])
        self.assertTrue(self.work.valid_push_session(key))
        identifier = self.work.state.auth.manage({'action': 'list'})['sessions'][0]['id']
        self.work.state.auth.manage({'action': 'revoke', 'id': identifier})
        self.assertFalse(self.request('/api/auth')[1]['authenticated'])
        self.assertFalse(self.work.valid_push_session(key))

    def test_local_manager_single_use_ticket_csrf_and_static_whitelist(self):
        code, value, _ = self.request('/manager', {}, private=True)
        self.assertEqual(code, 200)
        ticket = value['url'].split('#')[1]
        origin = f'http://127.0.0.1:{self.control.server_port}'
        headers = {'Origin': origin, 'Authorization': None}
        self.assertEqual(self.request('/manager/unlock', {'ticket': ticket}, private=True,
                                      headers={**headers, 'Origin': 'https://evil.test'})[0], 403)
        code, body, result_headers = self.request('/manager/unlock', {'ticket': ticket}, private=True, headers=headers)
        self.assertEqual(code, 200, body)
        self.assertEqual(self.request('/manager/unlock', {'ticket': ticket}, private=True, headers=headers)[0], 403)
        cookie = result_headers['Set-Cookie'].split(';')[0]
        session_headers = {**headers, 'Cookie': cookie}
        payload = {'method': 'clients', 'value': {'action': 'status'}}
        self.assertEqual(self.request('/manager/api', payload, private=True, headers=session_headers)[0], 403)
        session_headers['X-CSRF-Token'] = body['csrf']
        with patch.object(self.public.desktop_sessions, 'control', return_value={'clients': []}) as control:
            self.assertEqual(self.request('/manager/api', payload, private=True, headers=session_headers)[0], 200)
            control.assert_called_once_with({'action': 'status'})
        for method in ('stop', 'save', 'harness', '__dict__'):
            self.assertEqual(self.request('/manager/api', {'method': method, 'value': {}},
                                          private=True, headers=session_headers)[0], 400)
        self.assertEqual(self.request('/manager/../../main.py', private=True, headers=session_headers)[0], 404)
        for path in ('/manager/', '/manager/app.js', '/manager/vendor/accounts.js'):
            self.assertEqual(self.request(path, private=True, headers={'Authorization': None})[0], 200)

    def test_private_boot_contract_requires_protocol_four_and_existing_paths(self):
        config = {**self.config, 'port': 13090, 'controlPort': 13091}
        self.assertEqual(validate(config)['protocol'], 4)
        for change in ({'protocol': 3}, {'publicUrl': 'http://phone.example.com'}, {'codexHome': str(self.directory / 'absent')}):
            with self.assertRaises(ValueError):
                validate({**config, **change})


if __name__ == '__main__':
    unittest.main()
