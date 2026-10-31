# Selected upstream MIT regression tests; see src/CodexMobile/UPSTREAM.md.
import concurrent.futures
import json
import os
import tempfile
import threading
import time
import unittest
from pathlib import Path
from unittest.mock import patch
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'CodexMobile'))
from vendor_bootstrap import activate
activate()
from bridge.features.auth.auth import Auth, LoginRejected, access_policy, password_record, session_hours
from bridge.features.auth.pairing import Pairing, phone_origin
ROOT = Path(__file__).resolve().parents[2] / 'artifacts' / 'mobile-tests'
ROOT.mkdir(parents=True, exist_ok=True)
ORIGIN = 'http://192.0.2.1:8787'

class AuthTests(unittest.TestCase):
    def setUp(self):
        (ROOT / '.tmp').mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT / '.tmp')
        self.directory = Path(self.temp.name)
        self.config = {'mode': 'none', 'sessionHours': 0}

    def tearDown(self):
        self.temp.cleanup()

    def auth(self):
        return Auth(self.config, self.directory)

    def test_permanent_login_survives_restart_without_storing_bearer(self):
        auth = self.auth()
        self.assertEqual(auth.cookie_name, self.auth().cookie_name)
        self.assertRegex(auth.cookie_name, r'^codex_mobile_session_[0-9a-f]{24}$')
        token, session = auth.new_session('192.0.2.7', 'iPhone Safari')
        self.assertEqual(session['expires'], 0)
        raw = (self.directory / 'auth-sessions.json').read_text()
        self.assertNotIn(token, raw)
        self.assertIn('cookieName', raw)
        with patch('bridge.features.auth.auth.time.time', return_value=session['created'] + 50 * 365 * 86400):
            restarted = self.auth()
            self.assertEqual(restarted.get(token)['csrf'], session['csrf'])
            self.assertEqual(restarted.cookie_age(token), 400 * 86400)
        row = restarted.manage({})['sessions'][0]
        self.assertEqual(row['ip'], '192.0.2.7')
        self.assertEqual(row['userAgent'], 'iPhone Safari')
        self.assertNotIn('csrf', row)
        self.assertIsNone(restarted.get(row['id']))
        if os.name != 'nt':
            self.assertEqual((self.directory / 'auth-sessions.json').stat().st_mode & 0o777, 0o600)

    def test_finite_deadline_does_not_slide_on_visit_or_restart(self):
        self.config['sessionHours'] = 2
        with patch('bridge.features.auth.auth.time.time', return_value=1000):
            token, session = self.auth().new_session('192.0.2.1')
            self.assertEqual(session['expires'], 8200)
        with patch('bridge.features.auth.auth.time.time', return_value=4000):
            auth = self.auth()
            auth.get(token, {'ip': '192.0.2.2', 'peer': '192.0.2.2', 'source': 'direct'}, 'Safari')
            self.assertEqual(auth.cookie_age(token), 4200)
            self.assertEqual(auth.get(token)['expires'], 8200)
        with patch('bridge.features.auth.auth.time.time', return_value=8200):
            self.assertIsNone(self.auth().get(token))

    def test_default_and_invalid_durations(self):
        self.assertEqual(Auth({'mode': 'none'}).hours, 12)
        for value in (-1, True, 1.5, '12', None, 87601):
            with self.subTest(value=value), self.assertRaises(ValueError):
                session_hours(value)
        self.assertEqual(session_hours(0), 0)

    def test_logout_and_local_revoke_persist(self):
        auth = self.auth()
        first, _ = auth.new_session('192.0.2.1')
        second, _ = auth.new_session('192.0.2.2')
        auth.logout(first)
        auth.manage({'action': 'revoke', 'id': auth.key(second)})
        self.assertIsNone(self.auth().get(first))
        self.assertIsNone(self.auth().get(second))

    def test_auth_setting_changes_invalidate_logins_but_keep_ip_rules(self):
        for field, value in [('username', 'changed'), ('hash', 'changed'), ('mode', 'password')]:
            self.config = {'mode': 'none', 'sessionHours': 0}
            auth = self.auth()
            auth.manage({'action': 'save', 'policy': {'blocklist': ['192.0.2.9']}})
            token, _ = auth.new_session('192.0.2.1')
            self.config[field] = value
            changed = self.auth()
            self.assertIsNone(changed.get(token))
            self.assertFalse(changed.permitted('192.0.2.9'))
            self.config = {'mode': 'none', 'sessionHours': 0}
            self.assertIsNone(self.auth().get(token))

    def test_duration_change_keeps_trusted_device_but_password_change_revokes(self):
        self.config['sessionHours'] = 12
        token, _ = self.auth().new_session('192.0.2.1', 'BridgeMobile/0.1-iOS')
        self.config['sessionHours'] = 1
        self.assertIsNotNone(self.auth().get(token))
        self.config['hash'] = 'changed-password'
        self.assertIsNone(self.auth().get(token))

    def test_block_ip_revokes_all_matching_sessions_and_denies_relogin(self):
        auth = self.auth()
        first, _ = auth.new_session('192.0.2.7')
        second, _ = auth.new_session('192.0.2.7')
        other, _ = auth.new_session('192.0.2.8')
        result = auth.manage({'action': 'block', 'id': auth.key(first)})
        self.assertEqual(result['policy']['blocklist'], ['192.0.2.7'])
        restarted = self.auth()
        self.assertIsNone(restarted.get(first))
        self.assertIsNone(restarted.get(second))
        self.assertIsNotNone(restarted.get(other))
        with self.assertRaises(PermissionError):
            restarted.login('', '', '192.0.2.7')

    def test_shared_proxy_login_lock_preserves_sessions_and_explicit_policy_still_revokes(self):
        self.config.update(mode='password', username='tester', **password_record('isolated-password'))
        auth = self.auth()
        address = '198.51.100.2'
        client = {'ip': address, 'peer': address, 'source': 'proxy'}
        phone, _ = auth.new_session(address, 'BridgeMobile/2.1.7-Android', client)
        browser, _ = auth.new_session(address, 'Safari', client)
        for _ in range(auth.MAX_FAILURES):
            with self.assertRaises(LoginRejected):
                auth.login('tester', 'wrong', address)
        self.assertTrue(auth.login_status(address)['blocked'])
        self.assertEqual(auth.login_status(address)['attemptsRemaining'], 0)
        for token in (phone, browser):
            self.assertIsNotNone(auth.get(token, client))
        self.assertIsNone(auth.get('invalid-cookie', client))
        # Correct credentials and pairing cannot create another session until
        # desktop unblocks this login source; valid cookies need no new login.
        with self.assertRaises(LoginRejected):
            auth.login('tester', 'isolated-password', address)
        with self.assertRaises(PermissionError):
            auth.new_session(address)
        restarted = self.auth()
        restarted.manage({'action': 'save', 'policy': {}})
        for token in (phone, browser):
            self.assertIsNotNone(restarted.get(token, client))
        self.assertTrue(restarted.login_status(address)['blocked'])
        restarted.manage({'action': 'unblock', 'ip': address})
        self.assertEqual(restarted.login_status(address)['attemptsRemaining'], auth.MAX_FAILURES)
        restarted.manage({'action': 'revoke', 'id': auth.key(phone)})
        self.assertIsNone(restarted.get(phone, client))
        self.assertIsNotNone(restarted.get(browser, client))
        restarted.manage({'action': 'block', 'id': auth.key(browser)})
        self.assertIsNone(self.auth().get(browser, client))
        self.assertFalse(self.auth().permitted(address))

    def test_allowlist_blacklist_priority_and_ipv6(self):
        auth = self.auth()
        token, _ = auth.new_session('192.0.2.8')
        auth.manage({'action': 'save', 'policy': {'allowlistEnabled': True,
                    'allowlist': ['2001:db8:0::1', '192.0.2.7'], 'blocklist': ['192.0.2.7']}})
        self.assertTrue(auth.permitted('2001:db8::1'))
        self.assertFalse(auth.permitted('192.0.2.7'))
        self.assertFalse(auth.permitted('192.0.2.8'))
        self.assertIsNone(self.auth().get(token))
        for policy in ({'allowlistEnabled': True}, {'allowlist': ['bad']}, {'blocklist': [123]},
                       {'trustedProxies': ['::1%eth0']}, {'allowlistEnabled': 'true'}):
            with self.assertRaises(ValueError):
                access_policy(policy)

    def test_client_headers_cannot_override_an_untrusted_peer(self):
        auth = self.auth()
        headers = {'Host': 'example.com', 'X-Forwarded-For': '192.0.2.77', 'CF-Connecting-IP': '192.0.2.88'}
        self.assertEqual(auth.client('192.0.2.1', headers, True)['ip'], '192.0.2.1')
        self.assertEqual(auth.client('127.0.0.1', headers, False)['ip'], '127.0.0.1')
        self.assertEqual(auth.client('127.0.0.1', headers, True)['ip'], '192.0.2.77')
        headers['X-Forwarded-For'] = '192.0.2.99, 192.0.2.7'
        self.assertEqual(auth.client('127.0.0.1', headers, True)['ip'], '192.0.2.7')
        auth.manage({'action': 'save', 'policy': {'trustedProxies': ['192.0.2.1']}})
        self.assertEqual(auth.client('192.0.2.1', headers, True)['ip'], '192.0.2.7')
        headers['Host'] = 'test.trycloudflare.com'
        self.assertEqual(auth.client('127.0.0.1', headers, True)['ip'], '192.0.2.88')
        self.assertEqual(auth.client('::ffff:192.0.2.7', {}, False)['ip'], '192.0.2.7')
        headers = {'Host': 'example.com', 'X-Forwarded-For': 'invalid'}
        self.assertEqual(auth.client('127.0.0.1', headers, True)['source'], 'proxy')

    def test_corrupt_persistent_policy_fails_closed(self):
        (self.directory / 'auth-sessions.json').write_text('{not json')
        with self.assertRaises(ValueError):
            self.auth()


    def test_last_seen_is_persisted_at_most_once_per_minute(self):
        auth = self.auth()
        with patch('bridge.features.auth.auth.time.time', return_value=1000):
            token, _ = auth.new_session('192.0.2.7', 'Safari')
        client = {'ip': '192.0.2.7', 'peer': '192.0.2.7', 'source': 'direct'}
        with patch.object(auth, 'persist', wraps=auth.persist) as write:
            with patch('bridge.features.auth.auth.time.time', return_value=1030):
                auth.get(token, client, 'Safari')
                write.assert_not_called()
            with patch('bridge.features.auth.auth.time.time', return_value=1061):
                auth.get(token, client, 'Safari')
                write.assert_called_once()
        self.assertEqual(self.auth().manage({})['sessions'][0]['lastSeen'], 1061)

class PairingTests(unittest.TestCase):
    def setUp(self):
        self.auth = Auth({'mode': 'none'})
        self.pairing = Pairing(self.auth, {ORIGIN, 'https://example.com'})

    def grant(self, origin=ORIGIN):
        return self.pairing.control({'action': 'create', 'url': origin})

    def redeem(self, grant, origin=ORIGIN):
        return self.pairing.exchange(grant['url'].split('#pair=')[1], origin, 'client')

    def test_replay_refresh_origin_and_session_lifetime(self):
        first = self.grant()
        with self.assertRaises(PermissionError): self.redeem(first, 'https://example.com')
        session_token, session = self.redeem(first)
        self.assertAlmostEqual(session['expires'] - time.time(), 43200, delta=2)
        with self.assertRaises(PermissionError): self.redeem(first)
        self.assertEqual(self.pairing.control({'action': 'status', 'id': first['id']})['state'], 'used')
        old = self.grant(); fresh = self.grant()
        with self.assertRaises(PermissionError): self.redeem(old)
        self.redeem(fresh)
        self.assertIsNotNone(self.auth.get(session_token))

    def test_expiry_revoke_restart_and_hash_storage(self):
        grant = self.grant()
        raw = grant['url'].split('#pair=')[1]
        self.assertNotIn(raw, str(self.pairing.grants))
        with patch('bridge.features.auth.pairing.time.time', return_value=grant['expires']+1):
            with self.assertRaises(PermissionError): self.redeem(grant)
            self.assertEqual(self.pairing.control({'action': 'status', 'id': grant['id']})['state'], 'expired')
        fresh = self.grant()
        self.pairing.control({'action': 'revoke', 'id': fresh['id']})
        with self.assertRaises(PermissionError): self.redeem(fresh)
        restarted = Pairing(self.auth, {ORIGIN})
        with self.assertRaises(PermissionError): restarted.exchange(raw, ORIGIN, 'new-client')

    def test_mobile_pairing_trusts_device_and_resets_password_failures(self):
        self.auth.failures['192.0.2.7'] = 3
        grant = self.grant()
        token, row = self.pairing.exchange(grant['url'].split('#pair=')[1], ORIGIN,
                                          '192.0.2.7', 'BridgeMobile/0.1-iOS')
        self.assertEqual(row['expires'], 0)
        self.assertEqual(self.auth.login_status('192.0.2.7')['attemptsRemaining'], 5)
        self.auth.manage({'action': 'revoke', 'id': self.auth.key(token)})
        self.assertIsNone(self.auth.get(token))

    def test_concurrent_redemption_has_exactly_one_winner(self):
        grant = self.grant()
        def redeem(_):
            try: self.redeem(grant); return True
            except PermissionError: return False
        with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
            self.assertEqual(sum(pool.map(redeem, range(12))), 1)

    def test_multiple_entries_independent_and_guesses_limited(self):
        lan, public = self.grant(), self.grant('https://example.com')
        self.pairing.control({'action': 'revoke', 'id': lan['id']})
        self.redeem(public, 'https://example.com')
        for _ in range(8):
            with self.assertRaises(PermissionError): self.pairing.exchange('x'*43, ORIGIN, 'attacker')
        with self.assertRaisesRegex(PermissionError, '5'): self.pairing.exchange('x'*43, ORIGIN, 'attacker')
        for url in ['http://localhost:8787', 'http://127.0.0.1', 'http://[::1]', 'https://a.localhost', 'https://evil.test', ORIGIN+'/?token=abc']:
            with self.assertRaises(ValueError): self.grant(url)
