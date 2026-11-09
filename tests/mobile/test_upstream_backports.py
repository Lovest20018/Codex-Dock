"""Selected v2.2.0 fixes through Dock's real adapter and native event model."""
import copy
import io
import json
import unittest
from unittest.mock import patch

import test_workbench as fixture_module
from bridge.clients.codex.cli_session import CliSessions
from bridge.features.accounts.models import model_catalog, valid_model_id
from bridge.features.sessions.model import normalize_item


class ProviderCompatibilityTests(unittest.TestCase):
    def test_reasoning_summary_priority_and_typed_content_fallback(self):
        def text(**fields):
            return normalize_item({'id': 'thought', 'type': 'reasoning', **fields})['text']
        self.assertEqual(text(summary=['summary'], content=['raw thought']), 'summary')
        self.assertEqual(text(summary=['', '  ', None], content=['第一段', 7, '第二段']), '第一段\n第二段')
        self.assertEqual(text(summary='bad container', content=['raw thought']), 'raw thought')
        self.assertEqual(text(summary={}, content='bad container'), '')

    def test_live_reasoning_survives_late_start_then_uses_authoritative_completion(self):
        observed = []
        sessions = CliSessions(fixture_module.UPSTREAM, lambda tid, state: observed.append(copy.deepcopy(state)), lambda *args: None)
        # No CLI or model is started: feed native event protocol into its real reducer.
        sessions.joined['fixture'] = object()
        sessions.states['fixture'] = {'turns': [{'turnId': 'turn', 'items': []}]}
        def event(method, **params):
            sessions._event({'method': method, 'params': {'threadId': 'fixture', 'turnId': 'turn', **params}})
        def item():
            return sessions.states['fixture']['turns'][0]['items'][0]
        event('item/reasoning/textDelta', itemId='thought', contentIndex=0, delta='原始')
        event('item/reasoning/textDelta', itemId='thought', contentIndex=0, delta='思考')
        event('item/started', item={'id': 'thought', 'type': 'reasoning', 'summary': [], 'content': []})
        self.assertEqual(normalize_item(item())['text'], '原始思考')
        before = copy.deepcopy(sessions.states['fixture'])
        for index in (-1, True, 1.5, '2', 1024, 10**9):
            event('item/reasoning/textDelta', itemId='thought', contentIndex=index, delta='invalid')
        event('item/reasoning/textDelta', itemId='', delta='invalid')
        self.assertEqual(sessions.states['fixture'], before)
        event('item/reasoning/summaryTextDelta', itemId='thought', summaryIndex=0, delta='最终摘要')
        self.assertEqual(normalize_item(item())['text'], '最终摘要')
        event('item/completed', item={'id': 'thought', 'type': 'reasoning', 'summary': ['服务端结果'], 'content': []})
        self.assertEqual(normalize_item(item())['text'], '服务端结果')
        self.assertGreaterEqual(len(observed), 5)

    def test_unicode_model_catalog_retains_real_ids_and_rejects_controls(self):
        accepted = ['供应商/模型-测试', 'openai/gpt-5.1', '模型:最新', 'x' * 200]
        rejected = ['', None, 4, 'bad model', 'bad\nmodel', 'bad\x00model', 'bad\u200bmodel', 'x' * 201]
        for value in accepted:
            self.assertTrue(valid_model_id(value), repr(value))
        for value in rejected:
            self.assertFalse(valid_model_id(value), repr(value))
        payload = json.dumps({'data': [{'id': value} for value in accepted + rejected]}, ensure_ascii=True).encode()
        with patch('bridge.features.accounts.models.build_opener') as opener:
            opener.return_value.open.return_value = io.BytesIO(payload)
            self.assertEqual({row['id'] for row in model_catalog('https://fixture.invalid/v1', 'synthetic-key')}, set(accepted))


class ImageDownloadHttpTests(unittest.TestCase):
    def setUp(self):
        # Compose rather than inherit the existing fixture, avoiding duplicate tests.
        self.fixture = fixture_module.WorkbenchTests(methodName='runTest')
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)

    def test_image_download_requires_login_and_forces_original_without_cache(self):
        f = self.fixture
        path = '/api/sessions/12345678-1234-4234-9234-123456789012/uploads/12345678-1234-4234-9234-123456789013/preview'
        self.assertEqual(f.request(path + '?download=1')[0], 401)
        f.login()
        image = f.directory / 'original.png'
        image.write_bytes(b'original image fixture')
        artifact = {'previewPath': str(image), 'previewSha256': 'a' * 64, 'name': '原图.png', 'previewMime': 'image/png'}
        with patch.object(f.bridge, 'upload_preview', return_value=(artifact, 'original')) as preview:
            status, body, headers = f.request(path + '?variant=thumb&download=1')
            self.assertEqual(status, 200, body)
            self.assertEqual(body, image.read_bytes())
            self.assertEqual(preview.call_args.args[2], 'original')
            self.assertTrue(headers['Content-Disposition'].startswith('attachment;'))
            self.assertEqual(headers['Cache-Control'], 'private, no-store')
            self.assertEqual(headers['Content-Type'], 'image/png')
            self.assertIn('nosniff', headers['X-Content-Type-Options'])
        # Duplicate download flags must not turn the preview into a download.
        with patch.object(f.bridge, 'upload_preview', return_value=(artifact, 'thumb')) as preview:
            status, _, headers = f.request(path + '?variant=thumb&download=1&download=1')
            self.assertEqual(status, 200)
            self.assertEqual(preview.call_args.args[2], 'thumb')
            self.assertTrue(headers['Content-Disposition'].startswith('inline;'))


if __name__ == '__main__':
    unittest.main()
