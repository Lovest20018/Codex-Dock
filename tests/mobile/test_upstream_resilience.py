"""Model/account isolation and partial-list regressions for the v2.2.1 subset."""
import copy
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

import test_workbench
sys.path.append(str(test_workbench.UPSTREAM / 'tests'))
import test_bridge as native_fixture
from bridge.app.service import Bridge
from bridge.clients.codex.app_server import AppServerUnavailable
from bridge.clients.codex.catalog import Catalog, CatalogError, CatalogIdentityChanged, ModelStore
from bridge.platforms.codex_daemon_env import DaemonEnvironmentUnavailable


class CatalogResilienceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.home = self.root / 'home'
        self.project = self.root / 'project'
        self.home.mkdir()
        self.project.mkdir()
        self.catalog = Catalog(self.home, executable=self.root / 'fixture-not-installed', allow_background_refresh=False)
        self.good = {'models': [{'id': 'fixture-model', 'efforts': ['low']}], 'modelSource': 'codex'}
        self.catalog._fetch = Mock(return_value=copy.deepcopy(self.good))

    def test_empty_refresh_keeps_last_successful_rows_and_timestamp(self):
        first = self.catalog.get_kind('models', str(self.project), provider='openai')
        self.catalog._fetch.return_value = {'models': [], 'modelSource': 'codex'}
        value = self.catalog.get_kind('models', str(self.project), provider='openai', refresh=True)
        self.assertEqual(value['models'], first['models'])
        self.assertEqual(value['cache']['updatedAt'], first['cache']['updatedAt'])
        self.assertEqual(value['cache']['state'], 'stale')

    def test_first_failure_is_not_persisted_as_fresh_empty_catalog(self):
        self.catalog._fetch.side_effect = CatalogError('synthetic failure')
        value = self.catalog.get_kind('models', str(self.project), provider='openai')
        self.assertEqual(value['models'], [])
        self.assertEqual(value['cache']['state'], 'unavailable')
        self.assertIsNone(self.catalog.model_store.read(value['cache']['identity']))

    def test_changed_account_during_failure_never_returns_old_cached_models(self):
        self.catalog.get_kind('models', str(self.project), provider='openai')
        def change(*args, **kwargs):
            (self.home / 'auth.json').write_text('{"account":"new-fixture"}')
            raise CatalogError('synthetic failure')
        self.catalog._fetch.side_effect = change
        with self.assertRaises(CatalogIdentityChanged):
            self.catalog.get_kind('models', str(self.project), provider='openai', refresh=True)

    def test_identity_tracks_provider_project_layers_and_auth(self):
        first = self.catalog.model_identity(self.project, 'openai')
        self.assertNotEqual(first, self.catalog.model_identity(self.project, 'custom'))
        (self.project / '.codex').mkdir()
        (self.project / '.codex/config.toml').write_text('model_provider="custom"\n')
        second = self.catalog.model_identity(self.project, 'openai')
        self.assertNotEqual(first, second)
        (self.home / 'auth.json').write_text('{"account":"fixture-new"}')
        self.assertNotEqual(second, self.catalog.model_identity(self.project, 'openai'))
        self.assertNotEqual(first, self.catalog.model_identity(self.root / 'other-project', 'openai'))

    def test_deleted_project_changes_only_read_helper_launch_directory(self):
        missing = self.project / 'deleted/deep'
        runtime = Catalog(self.home, executable=self.root / 'fixture-not-installed', allow_background_refresh=False)
        observed = {}
        class StopFixture(Exception):
            pass
        def launch(*args, **kwargs):
            observed.update(kwargs)
            raise StopFixture()
        with patch('bridge.clients.codex.catalog.subprocess.Popen', side_effect=launch):
            with self.assertRaises(StopFixture):
                runtime._fetch(str(missing), provider='openai', kind='models')
        self.assertEqual(observed['cwd'], self.project)
        self.assertNotEqual(runtime.model_identity(missing, 'openai'), runtime.model_identity(self.project, 'openai'))


class NativeCatalogResilienceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.bridge = Bridge.__new__(Bridge)
        self.bridge.native_model_store = ModelStore(root)
        self.bridge.native_model_lock = threading.Lock()
        self.bridge.native_model_reads = {}
        self.bridge._native_model_identity = Mock(return_value='identity-a')
        self.bridge._schedule_native_model_refresh = Mock()
        self.bridge.cli_sessions = SimpleNamespace(get_kind=Mock(return_value={
            'models': [{'id': 'fixture-model', 'efforts': ['low']}], 'modelSource': 'codex'}))

    def test_native_empty_refresh_keeps_previous_timestamp(self):
        first = self.bridge._native_models('fixture-thread', self.temp.name, 'openai')
        self.bridge.cli_sessions.get_kind.return_value = {'models': []}
        value = self.bridge._native_models('fixture-thread', self.temp.name, 'openai', refresh=True)
        self.assertEqual(value['models'], first['models'])
        self.assertEqual(value['cache']['updatedAt'], first['cache']['updatedAt'])
        self.assertEqual(value['cache']['state'], 'stale')

    def test_native_failure_before_any_catalog_has_unavailable_identity(self):
        self.bridge.cli_sessions.get_kind.side_effect = AppServerUnavailable('offline')
        value = self.bridge._native_models('fixture-thread', self.temp.name, 'openai')
        self.assertEqual(value['cache'], {'state': 'unavailable', 'identity': 'identity-a'})
        self.assertIsNone(self.bridge.native_model_store.read('identity-a'))

    def test_account_change_while_failure_never_returns_previous_account_cache(self):
        self.bridge._native_models('fixture-thread', self.temp.name, 'openai')
        def change(*args, **kwargs):
            self.bridge._native_model_identity.return_value = 'identity-b'
            raise AppServerUnavailable('offline')
        self.bridge.cli_sessions.get_kind.side_effect = change
        with self.assertRaises(CatalogIdentityChanged):
            self.bridge._native_models('fixture-thread', self.temp.name, 'openai', refresh=True)

    def test_catalog_reuse_never_borrows_another_threads_current_selection(self):
        self.bridge.cli_sessions.get_kind.return_value = {
            'models': [{'id': 'fixture-model'}], 'currentModel': 'thread-a-model', 'currentEffort': 'high'}
        self.bridge._native_models('thread-a', self.temp.name, 'openai')
        cached = self.bridge._native_models('thread-b', self.temp.name, 'openai')
        self.assertNotIn('currentModel', cached)
        self.assertNotIn('currentEffort', cached)

    def test_unverified_native_environment_cannot_authorize_manual_catalog(self):
        bridge = self.bridge
        bridge.host = 'local'
        bridge.codex_home = Path(self.temp.name)
        bridge.cli_sessions.alias = None
        bridge.cli_sessions.client = SimpleNamespace(request=Mock(side_effect=lambda method, params, **kwargs:
            {'config': {'model_provider': 'custom'}} if method == 'config/read' else {'account': {}}))
        bridge.cli_sessions.model_environment_identity = Mock(side_effect=DaemonEnvironmentUnavailable('unverified'))
        with self.assertRaisesRegex(AppServerUnavailable, '无法核验'):
            Bridge._native_model_identity(bridge, self.temp.name, 'custom')


class ListAndSettingsIntegrationTests(unittest.TestCase):
    setUp = native_fixture.IntegrationTests.setUp
    tearDown = native_fixture.IntegrationTests.tearDown

    def test_broken_host_construction_preserves_local_and_good_host_rows(self):
        with patch.object(self.bridge.hosts, 'hosts', return_value={
                'bad': {'alias': 'bad-entry'}, 'good': {'alias': 'good-entry'}}), \
                patch.object(self.bridge, 'for_host', side_effect=lambda host: self.bridge if host == 'good' else (_ for _ in ()).throw(ValueError('synthetic unavailable'))):
            rows = self.bridge.list()
        self.assertTrue(any(row['id'] == native_fixture.THREAD for row in rows))
        self.assertEqual(self.bridge.host_errors, [{'host': 'bad', 'label': 'bad-entry', 'error': 'synthetic unavailable'}])

    def test_wsl_constructor_failure_preserves_local_rows(self):
        self.bridge.entries = SimpleNamespace(hosts=lambda: {'wsl:fixture': {'label': 'Fixture WSL'}}, close=lambda: None)
        with patch.object(self.bridge, 'for_host', side_effect=ValueError('fixture offline')):
            rows = self.bridge.list()
        self.assertEqual([row['id'] for row in rows], [native_fixture.THREAD])
        self.assertEqual(self.bridge.host_errors[0]['host'], 'wsl:fixture')

    def test_manual_model_and_native_default_effort_stay_on_original_owner(self):
        session = self.bridge.session(native_fixture.THREAD)
        catalog = {'models': [], 'cache': {'state': 'unavailable', 'identity': 'verified-fixture'}}
        with patch.object(self.bridge, 'catalog', return_value=catalog):
            result = self.bridge.settings(session.id, '手动/fixture', None, expected_catalog='verified-fixture')
        self.assertTrue(result['confirmed'])
        calls = [row for row in self.fixture.requests if row['method'] == 'thread-follower-update-thread-settings']
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0]['targetClientId'], 'owner')
        self.assertEqual(calls[0]['params']['threadSettings'], {'model': '手动/fixture'})

    def test_changed_identity_or_unverified_fast_mode_never_reaches_native_mutation(self):
        session = self.bridge.session(native_fixture.THREAD)
        with patch.object(self.bridge, 'catalog', return_value={'models': [], 'cache': {'identity': 'new-account'}}), \
                patch.object(self.bridge, '_call') as call:
            with self.assertRaises(CatalogIdentityChanged):
                self.bridge.settings(session.id, 'fixture', None, expected_catalog='old-account')
            with self.assertRaisesRegex(ValueError, 'Fast'):
                self.bridge.settings(session.id, 'fixture', None, fast_mode=True, expected_catalog='new-account')
            call.assert_not_called()


class CatalogHttpTests(unittest.TestCase):
    def test_identity_failure_uses_specific_code_and_retains_auth_boundary(self):
        fixture = test_workbench.WorkbenchTests(methodName='runTest')
        fixture.setUp()
        self.addCleanup(fixture.tearDown)
        path = '/api/sessions/12345678-1234-4234-9234-123456789012/catalog?kind=models'
        self.assertEqual(fixture.request(path)[0], 401)
        fixture.login()
        with patch.object(fixture.bridge, 'catalog', side_effect=CatalogIdentityChanged('fixture account changed')):
            status, body, _ = fixture.request(path)
        self.assertEqual(status, 409)
        self.assertEqual(body['code'], 'model_identity_changed')


if __name__ == '__main__':
    unittest.main()
