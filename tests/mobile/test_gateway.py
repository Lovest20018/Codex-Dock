"""Isolated HTTP contract/security tests. All users, cookies and homes are fake."""
import base64
import concurrent.futures
import copy
import http.client
import json
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch, Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'CodexMobile'))
from bridge.features.auth.auth import password_record
from gateway import State, Server, PublicHandler, ControlHandler, public_origin, proxy_address
from main import validate

ORIGIN = 'https://phone.example.com'
AGENT = 'BridgeMobile/2.1.7-Android'
PASSWORD = 'isolated-mobile-password'
AUTH = {'mode': 'password', 'username': 'tester', 'sessionHours': 12, **password_record(PASSWORD)}
SID = '12345678-1234-1234-1234-123456789abc'


class FakeBridge:
    def __init__(self): self.calls = []
    def list(self, query, offset): return [{'id': SID, 'title': 'Isolated chat', 'cwd': 'C:/fake'}]
    def view(self, thread_id): return {'id': thread_id, 'connected': False, 'turns': [], 'requests': []}
    def mutate(self, thread_id, kind, body):
        self.calls.append((thread_id, kind, body))
        return {'id': body['id'], 'status': 'accepted'}


class HTTPTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        (self.directory / 'home').mkdir()
        self.config = {'protocol': 2, 'publicUrl': ORIGIN, 'port': 13090, 'controlPort': 13091, 'proxyAddress': '',
                       'controlToken': 'b' * 64, 'instanceId': 'a' * 32, 'auth': copy.deepcopy(AUTH),
                       'codexHome': str(self.directory / 'home'), 'dataDir': str(self.directory)}
        self.bridge = FakeBridge()
        self.state = State(self.config, self.bridge, Path(__file__).resolve().parents[2] / 'src/CodexMobile/web')
        self.public = Server(('127.0.0.1', 0), PublicHandler, self.state)
        self.control = Server(('127.0.0.1', 0), ControlHandler, self.state)
        self.config.update(port=self.public.server_port, controlPort=self.control.server_port)
        self.threads = [threading.Thread(target=server.serve_forever, daemon=True) for server in (self.public, self.control)]
        for thread in self.threads: thread.start()
        self.cookie = None; self.csrf = None

    def tearDown(self):
        for server in (self.public, self.control): server.shutdown(); server.server_close()
        for thread in self.threads: thread.join(timeout=2)
        self.temp.cleanup()

    def request(self, path, body=None, *, private=False, headers=None, raw=None):
        server = self.control if private else self.public
        values = {'Host': f'127.0.0.1:{server.server_port}' if private else 'phone.example.com', 'User-Agent': AGENT}
        if private: values['Authorization'] = 'Bearer ' + self.config['controlToken']
        if self.cookie: values['Cookie'] = self.cookie
        if body is not None or raw is not None:
            values['Content-Type'] = 'application/json'
            if not private: values['Origin'] = ORIGIN
            if self.csrf and not private: values['X-CSRF-Token'] = self.csrf
        values.update(headers or {})
        values = {key: value for key, value in values.items() if value is not None}
        data = raw if raw is not None else json.dumps(body).encode() if body is not None else None
        connection = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=5)
        try:
            connection.request('POST' if data is not None else 'GET', path, data, values)
            response = connection.getresponse(); payload = response.read(); result_headers = dict(response.getheaders())
            result = json.loads(payload) if result_headers.get('Content-Type', '').startswith('application/json') else payload
            return response.status, result, result_headers
        finally: connection.close()

    def login(self):
        code, body, headers = self.request('/api/login', {'username': 'tester', 'password': PASSWORD})
        self.assertEqual(code, 200, body)
        self.cookie = headers['Set-Cookie'].split(';')[0]; self.csrf = body['csrf']
        return headers

    def test_android_probe_cookie_and_persistent_revocation_contract(self):
        code, body, _ = self.request('/api/auth')
        self.assertEqual(code, 200); self.assertIs(body['authenticated'], False); self.assertIs(body['passwordless'], False)
        self.assertEqual(body['instanceId'], self.config['instanceId'])
        headers = self.login()
        for flag in ('Secure', 'HttpOnly', 'SameSite=Strict', 'Path=/'): self.assertIn(flag, headers['Set-Cookie'])
        self.assertRegex(self.cookie, r'^codex_mobile_session_[a-f0-9]{24}=[A-Za-z0-9_-]{43}$')
        code, body, _ = self.request('/api/auth')
        self.assertTrue(body['authenticated']); self.assertTrue(body['trustedDevice']); self.assertEqual(body['enabledClients'], ['codex'])
        self.assertEqual(body['csrf'], self.csrf); self.assertEqual(body['cookieName'], self.cookie.split('=')[0])
        # Constructing fresh Auth simulates a worker restart, using only fake data.
        restarted = State(self.config, self.bridge, self.state.web_dir)
        self.state.auth = restarted.auth
        self.assertTrue(self.request('/api/auth')[1]['authenticated'])
        _, devices, _ = self.request('/devices', {'action': 'list'}, private=True)
        self.assertEqual(len(devices['sessions']), 1); self.assertNotIn('csrf', devices['sessions'][0])
        self.assertNotIn(self.cookie.split('=')[1], (self.directory / 'auth-sessions.json').read_text())
        code, _, _ = self.request('/devices', {'action': 'revoke', 'id': devices['sessions'][0]['id']}, private=True)
        self.assertEqual(code, 200); self.assertFalse(self.request('/api/auth')[1]['authenticated'])
        self.assertEqual(self.request('/api/sessions')[0], 401)

    def test_host_origin_csrf_and_public_control_are_denied(self):
        self.assertEqual(self.request('/', headers={'Host': 'evil.example.com'})[0], 403)
        self.assertEqual(self.request('/api/auth', headers={'Origin': 'https://evil.example.com'})[0], 403)
        self.assertEqual(self.request('/api/login', {'username': 'tester', 'password': PASSWORD}, headers={'Origin': None})[0], 403)
        self.login()
        self.assertEqual(self.request('/api/logout', {}, headers={'X-CSRF-Token': None})[0], 403)
        self.assertEqual(self.request('/api/logout', {}, headers={'X-CSRF-Token': 'wrong'})[0], 403)
        self.assertEqual(self.request('/api/logout', {}, headers={'Origin': 'https://phone.example.com:444'})[0], 403)
        self.assertEqual(self.request('/api/logout', {}, headers={'Sec-Fetch-Site': 'cross-site'})[0], 403)
        for path in ('/stop', '/pair', '/devices', '/check', '/api/rpc', '/api/terminal', '/api/accounts', '/api/clients/claude/list'):
            self.assertEqual(self.request(path, {})[0], 404, path)
        self.assertFalse(self.state.stop.is_set())
        self.assertEqual(self.bridge.calls, [])

    def test_private_control_requires_bearer_loopback_host_and_no_browser_origin(self):
        self.assertEqual(self.request('/health', private=True)[0], 200)
        self.assertEqual(self.request('/health', private=True, headers={'Authorization': None})[0], 403)
        self.assertEqual(self.request('/health', private=True, headers={'Authorization': 'Bearer wrong'})[0], 403)
        self.assertEqual(self.request('/health', private=True, headers={'Origin': ORIGIN})[0], 403)
        self.assertEqual(self.request('/health', private=True, headers={'Host': 'phone.example.com'})[0], 403)
        self.assertEqual(self.request('/health', private=True, headers={'X-Forwarded-For': '192.0.2.1'})[0], 403)
        self.assertEqual(self.request('/health', private=True, headers={'Sec-Fetch-Site': 'same-origin'})[0], 403)

    def test_lan_proxy_allowlist_is_based_on_socket_peer_and_preserves_client_ip(self):
        self.state.proxy_address = '192.168.8.2'
        self.state.auth.policy['trustedProxies'] = ['192.168.8.2']
        original_setup = PublicHandler.setup
        peer = '192.168.8.3'
        def setup(handler):
            original_setup(handler)
            handler.client_address = (peer, handler.client_address[1])
        with patch.object(PublicHandler, 'setup', setup):
            for path in ('/', '/app.js', '/api/auth'):
                self.assertEqual(self.request(path, headers={'X-Forwarded-For': '192.168.8.2'})[0], 403)
            self.assertEqual(self.request('/health', private=True)[1]['lastRejectedPeer'], peer)
            peer = '192.168.8.2'
            self.assertEqual(self.request('/api/auth')[0], 200)
            # The proxy appends the real client to a potentially forged left end.
            code, body, headers = self.request('/api/login', {'username': 'tester', 'password': PASSWORD},
                                              headers={'X-Forwarded-For': '198.51.100.1, 192.0.2.77'})
            self.assertEqual(code, 200)
            self.cookie = headers['Set-Cookie'].split(';')[0]
            self.assertTrue(self.request('/api/auth', headers={'X-Forwarded-For': '192.0.2.77'})[1]['authenticated'])
            self.assertEqual(self.request('/api/auth', headers={'Host': 'wrong.example.com'})[0], 403)
            self.assertEqual(self.request('/api/logout', {}, headers={'X-Forwarded-For': '192.0.2.77', 'Origin': 'https://other.example.com'})[0], 403)
        devices = self.request('/devices', {'action': 'list'}, private=True)[1]['sessions']
        self.assertEqual(devices[0]['ip'], '192.0.2.77')
        # A persisted policy cannot add another transport peer on restart.
        self.state.auth.policy['trustedProxies'] = ['192.168.8.99']
        self.state.auth.persist()
        restarted = State(self.config, self.bridge, self.state.web_dir)
        self.assertEqual(restarted.auth.policy['trustedProxies'], [])

    def test_public_nat_proxy_peer_is_allowed_without_trusting_the_original_lan_ip(self):
        self.state.proxy_address = '198.51.100.2'
        self.state.auth.policy['trustedProxies'] = ['198.51.100.2']
        original_setup = PublicHandler.setup
        peer = '198.51.100.2'
        def setup(handler):
            original_setup(handler)
            handler.client_address = (peer, handler.client_address[1])
        with patch.object(PublicHandler, 'setup', setup):
            self.assertEqual(self.request('/api/auth')[0], 200)
            peer = '192.168.8.2'
            self.assertEqual(self.request('/api/auth', headers={'X-Forwarded-For': '198.51.100.2'})[0], 403)

    def test_local_diagnostic_page_uses_https_without_allowing_http_login(self):
        local_host = f'127.0.0.1:{self.public.server_port}'
        code, body, headers = self.request('/', headers={'Host': local_host})
        self.assertEqual(code, 200)
        self.assertIn(ORIGIN.encode(), body)
        self.assertIn('手机服务已运行'.encode(), body)
        self.assertNotIn(b'<script', body)
        self.assertNotIn('Set-Cookie', headers)
        for path in ('/api/auth', '/app.js'):
            self.assertEqual(self.request(path, headers={'Host': local_host})[0], 403)
        self.assertEqual(self.request('/api/login', {'username': 'tester', 'password': PASSWORD}, headers={'Host': local_host})[0], 403)

    def test_one_use_pairing_requires_entry_check_and_only_returns_local_image(self):
        with patch.object(self.state, 'check_entry', side_effect=ValueError('HTTPS check failed')):
            self.assertEqual(self.request('/pair', {'action': 'create'}, private=True)[0], 400)
        self.assertFalse(self.state.pairing.grants)
        with patch.object(self.state, 'check_entry', return_value={'ok': True}) as check:
            code, grant, _ = self.request('/pair', {'action': 'create'}, private=True)
            self.assertEqual(code, 200); check.assert_called_once()
        self.assertNotIn('url', grant); self.assertTrue(base64.b64decode(grant['image']).startswith(b'\x89PNG\r\n\x1a\n'))
        # Issue directly to inspect a fake token; the UI control never returns it.
        actual = self.state.pairing.control({'action': 'create', 'url': ORIGIN})
        token = actual['url'].split('#pair=')[1]
        self.assertNotIn(token, str(self.state.pairing.grants))
        code, body, headers = self.request('/api/pair', {'token': token})
        self.assertEqual(code, 200); self.assertIn('csrf', body); self.assertIn('Secure', headers['Set-Cookie'])
        self.assertEqual(self.request('/api/pair', {'token': token})[0], 403)
        self.assertEqual(self.request('/pair', {'action': 'status', 'id': actual['id']}, private=True)[1]['state'], 'used')

    def test_password_failures_block_and_private_unblock_restores_login(self):
        for _ in range(5):
            code, _, _ = self.request('/api/login', {'username': 'tester', 'password': 'wrong'}, headers={'X-Forwarded-For': '192.0.2.8'})
            self.assertEqual(code, 401)
        self.assertTrue(self.request('/api/auth', headers={'X-Forwarded-For': '192.0.2.8'})[1]['loginStatus']['blocked'])
        code, body, _ = self.request('/devices', {'action': 'list'}, private=True)
        self.assertEqual(body['autoBlocks'][0]['ip'], '192.0.2.8')
        self.assertEqual(self.request('/devices', {'action': 'unblock', 'ip': '192.0.2.8'}, private=True)[0], 200)
        self.assertEqual(self.request('/api/login', {'username': 'tester', 'password': PASSWORD}, headers={'X-Forwarded-For': '192.0.2.8'})[0], 200)

    def test_exact_route_whitelist_json_limits_and_logout(self):
        self.login()
        for path in ('/../main.py', '/web/index.html', '/app.js/../main.py', '/api/sessions/../../auth-sessions.json', '/api/sessions?q=x&q=y'):
            self.assertIn(self.request(path)[0], (400, 404), path)
        code, payload, headers = self.request('/')
        self.assertEqual(code, 200); self.assertIn(b'id="app" hidden', payload)
        self.assertIn("frame-ancestors 'none'", headers['Content-Security-Policy'])
        self.assertEqual(self.request('/api/sessions')[1]['sessions'][0]['id'], SID)
        self.assertEqual(self.request('/api/logout', raw=b'{"x":NaN}')[0], 400)
        self.assertEqual(self.request('/api/logout', raw=b'[]')[0], 400)
        self.assertEqual(self.request('/api/logout', raw=b'x' * 512001)[0], 400)
        self.assertEqual(self.request('/api/logout', {}, headers={'Content-Type': 'text/plain'})[0], 400)
        self.assertEqual(self.request('/api/logout', {})[0], 200)
        self.assertFalse(self.request('/api/auth')[1]['authenticated'])

    def test_entry_probe_is_credential_free_rejects_mismatch_and_redirect(self):
        class Response:
            status = 200
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def read(self, limit): return json.dumps({'instanceId': self_id, 'passwordless': False, 'authenticated': False}).encode()
        self_id = self.config['instanceId']
        opener = Mock(); opener.open.return_value = Response()
        with patch('gateway.urllib.request.build_opener', return_value=opener) as builder:
            self.assertTrue(self.state.check_entry()['ok'])
            request = opener.open.call_args.args[0]
            self.assertEqual(request.full_url, ORIGIN + '/api/auth')
            self.assertNotIn('Cookie', request.headers); self.assertNotIn('Authorization', request.headers)
            self.assertIsInstance(builder.call_args.args[1], __import__('gateway').NoRedirect)
            self_id = 'wrong'
            with self.assertRaises(ValueError): self.state.check_entry()
            self.assertFalse(self.state.checked)

    def test_entry_probe_reports_gateway_and_host_failures_without_response_body(self):
        for status, expected in ((502, '目标 IP'), (403, 'Host'), (302, '不接受跳转')):
            opener = Mock()
            opener.open.side_effect = __import__('urllib.error', fromlist=['HTTPError']).HTTPError(ORIGIN, status, 'hidden upstream body', {}, None)
            with patch('gateway.urllib.request.build_opener', return_value=opener):
                with self.assertRaisesRegex(ValueError, expected): self.state.check_entry()
                self.assertFalse(self.state.checked)


class ConfigTests(unittest.TestCase):
    def test_proxy_requires_one_canonical_unicast_ipv4(self):
        self.assertEqual(proxy_address(''), '')
        for value in ('192.168.8.2', '10.2.3.4', '172.16.1.2', '172.31.255.254', '198.51.100.2'):
            self.assertEqual(proxy_address(value), value)
        for value in ('0.0.0.0', '0.2.3.4', '127.0.0.1', '224.0.0.1', '255.255.255.255', '169.254.1.2', '192.168.8.0/24', '*', 'localhost', '::1', '192.168.008.2', None, 123):
            with self.assertRaises(ValueError, msg=value): proxy_address(value)

    def test_only_https_root_domains_and_explicit_boot_inputs(self):
        self.assertEqual(public_origin('https://PHONE.example.com:443/'), ORIGIN)
        for value in ('http://phone.example.com', ORIGIN + '/proxy', ORIGIN + '?token=x', ORIGIN + '#pair=x',
                      'https://user:secret@phone.example.com', 'https://localhost', 'https://a.localhost', 'https://127.0.0.1',
                      'https://[::1]', 'https://a..com', 'https://-bad.com', 'https://phone.example.com:0'):
            with self.assertRaises(ValueError, msg=value): public_origin(value)
        with self.assertRaises(ValueError): validate({})


if __name__ == '__main__': unittest.main()
