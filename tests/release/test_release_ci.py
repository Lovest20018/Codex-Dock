import contextlib
import io
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('release_ci', ROOT / 'scripts/release-ci.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleasePipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.out = Path(self.temp.name)
        self.windows, self.android = release.versions()
        self.commit_patch = patch.object(release, 'commit', return_value='a' * 40)
        self.commit_patch.start()
        self.addCleanup(self.commit_patch.stop)
        w, a = self.windows, self.android
        (self.out / f'CodexDock-{w}-Setup-win-x64.exe').write_bytes(b'fixture installer')
        (self.out / f'CodexDock-{w}-source.zip').write_bytes(b'fixture source')
        with zipfile.ZipFile(self.out / f'CodexDock-{w}-win-x64.zip', 'w') as archive:
            archive.writestr(f'CodexDock-{w}-win-x64/manifest.json', json.dumps({'sourceCommit': 'a' * 40, 'version': w}))
            archive.writestr(f'CodexDock-{w}-win-x64/verification/ui-report.json', '{"status":"PASS"}')
        windows_names = [f'CodexDock-{w}-Setup-win-x64.exe', f'CodexDock-{w}-source.zip', f'CodexDock-{w}-win-x64.zip']
        (self.out / f'SHA256SUMS-{w}.txt').write_text(''.join(f'{release.sha(self.out / name)}  {name}\n' for name in windows_names))
        (self.out / f'installer-verification-{w}.json').write_text(json.dumps({'status': 'PASS', 'installerSha256': release.sha(self.out / windows_names[0]),
            'runningMobileUpgrade': True, 'otherInstallationWorkerPreserved': True, 'mobileSettingsUnchanged': True}))
        (self.out / f'LovestCode-Android-{a}.apk').write_bytes(b'fixture APK')
        (self.out / f'LovestCode-Android-{a}-source.zip').write_bytes(b'fixture Android source')
        self.android_manifest = self.out / f'LovestCode-Android-{a}.manifest.json'
        self.android_data = {'sourceCommit': 'a' * 40, 'dirtyBuild': False, 'versionName': a,
            'sha256': release.sha(self.out / f'LovestCode-Android-{a}.apk'),
            'signingCertificateSha256': json.loads((ROOT / 'src/CodexAndroid/android.json').read_text())['signingCertificateSha256']}
        self.write_android()

    def write_android(self):
        self.android_manifest.write_text(json.dumps(self.android_data))
        names = [f'LovestCode-Android-{self.android}{suffix}' for suffix in ('.apk', '-source.zip', '.manifest.json')]
        (self.out / f'SHA256SUMS-Android-{self.android}.txt').write_text(''.join(f'{release.sha(self.out / name)}  {name}\n' for name in names))

    def test_windows_release_needs_only_windows_files(self):
        for path in list(self.out.iterdir()):
            if 'Android' in path.name:
                path.unlink()
        files = release.release_files(self.out, 'windows')
        self.assertEqual(len(files), 6)
        manifest = json.loads((self.out / 'release-manifest.json').read_text())
        self.assertEqual(manifest['sourceCommit'], 'a' * 40)
        self.assertEqual(len(manifest['assets']), 5)
        self.assertEqual(manifest['platform'], 'windows')
        self.assertTrue(all('Android' not in path.name for path in files))

    def test_mixed_source_commits_are_rejected(self):
        self.android_data['sourceCommit'] = 'b' * 40
        self.write_android()
        with self.assertRaisesRegex(ValueError, 'selected clean release commit'):
            release.release_files(self.out, 'android')

    def test_different_android_certificate_is_rejected(self):
        self.android_data['signingCertificateSha256'] = '0' * 64
        self.write_android()
        with self.assertRaises(ValueError):
            release.release_files(self.out, 'android')

    def test_changed_installer_is_rejected(self):
        (self.out / f'CodexDock-{self.windows}-Setup-win-x64.exe').write_bytes(b'changed installer')
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            release.release_files(self.out, 'windows')

    def test_failed_installer_verification_is_rejected(self):
        (self.out / f'installer-verification-{self.windows}.json').write_text('{"status":"FAIL"}')
        with self.assertRaisesRegex(ValueError, 'installer verification'):
            release.release_files(self.out, 'windows')

    def test_tag_version_mismatch_fails_before_build(self):
        with patch.dict(os.environ, {'GITHUB_REF_TYPE': 'tag', 'GITHUB_REF_NAME': 'v99.0.0'}):
            with self.assertRaisesRegex(ValueError, 'tag must match'):
                release.metadata()

    def test_missing_running_upgrade_evidence_is_rejected(self):
        report = self.out / f'installer-verification-{self.windows}.json'
        values = json.loads(report.read_text())
        for key in ('runningMobileUpgrade', 'otherInstallationWorkerPreserved', 'mobileSettingsUnchanged'):
            with self.subTest(key=key):
                changed = dict(values); changed.pop(key)
                report.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError, 'running mobile upgrade'):
                    release.release_files(self.out, 'windows')

    def test_existing_stable_release_is_never_modified(self):
        with patch.object(release, 'GitHub') as github:
            github.return_value.request.return_value = [{'tag_name': 'v' + self.windows, 'draft': False}]
            with self.assertRaisesRegex(ValueError, 'already published'):
                release.publish(self.out, 'windows')
            github.return_value.request.assert_called_once_with('/releases?per_page=100')

    def test_android_release_needs_only_android_files(self):
        for path in list(self.out.iterdir()):
            if 'Android' not in path.name:
                path.unlink()
        files = release.release_files(self.out, 'android')
        self.assertEqual(len(files), 5)
        manifest = json.loads((self.out / 'release-manifest.json').read_text())
        self.assertEqual((manifest['platform'], manifest['version']), ('android', self.android))
        self.assertEqual(len(manifest['assets']), 4)
        self.assertTrue(all('CodexDock-' not in path.name for path in files))

    def test_tag_selects_only_its_platform(self):
        for platform, tag in (('windows', 'v' + self.windows), ('android', 'android-v' + self.android)):
            with self.subTest(platform=platform), patch.dict(os.environ, {'GITHUB_REF_TYPE': 'tag', 'GITHUB_REF_NAME': tag}):
                with contextlib.redirect_stdout(io.StringIO()) as output:
                    release.metadata()
                values = json.loads(output.getvalue())
                self.assertEqual((values['platform'], values['tag']), (platform, tag))
                with self.assertRaisesRegex(ValueError, 'tag must match'):
                    release.metadata('android' if platform == 'windows' else 'windows')

    def test_release_titles_notes_assets_and_latest_are_separate(self):
        for platform, expected_latest in (('windows', 'true'), ('android', 'false')):
            with self.subTest(platform=platform), patch.object(release, 'GitHub') as github:
                files = release.release_files(self.out, platform)
                requests = []
                def request(path, method='GET', body=None, upload=None):
                    requests.append((path, method, body, upload))
                    if path == '/releases?per_page=100':
                        return []
                    if path.startswith('/git/ref/tags/'):
                        return {'object': {'type': 'commit', 'sha': 'a' * 40}}
                    if method == 'POST' and path == '/releases':
                        return {'id': 7, 'upload_url': 'https://uploads.github.com/repos/fixture/releases/7/assets{?name}'}
                    if upload:
                        return {'size': upload.stat().st_size, 'digest': 'sha256:' + release.sha(upload)}
                    if method == 'GET' and path == '/releases/7':
                        return {'assets': [{'name': item.name} for item in files]}
                    return {}
                github.return_value.request.side_effect = request
                with contextlib.redirect_stdout(io.StringIO()):
                    release.publish(self.out, platform)
                created = next(body for path, method, body, upload in requests if path == '/releases' and method == 'POST')
                self.assertEqual(created['name'], ('Codex Dock for Windows ' + self.windows) if platform == 'windows' else ('Lovest Code for Android ' + self.android))
                if platform == 'windows':
                    self.assertNotIn('# Lovest Code Android', created['body'])
                else:
                    self.assertNotIn('# Codex Dock ', created['body'])
                self.assertEqual(requests[-1][2]['make_latest'], expected_latest)
                self.assertEqual(len([upload for path, method, body, upload in requests if upload]), 6 if platform == 'windows' else 5)

    def test_wrong_remote_tag_commit_blocks_upload(self):
        with patch.object(release, 'GitHub') as github:
            github.return_value.request.side_effect = [[], {'object': {'type': 'commit', 'sha': 'b' * 40}}]
            with self.assertRaisesRegex(ValueError, 'exact source commit'):
                release.publish(self.out, 'windows')
            self.assertEqual(github.return_value.request.call_count, 2)

    def test_platform_must_be_selected_for_publication(self):
        with self.assertRaisesRegex(ValueError, 'exactly one'):
            release.publish(self.out, None)


if __name__ == '__main__':
    unittest.main()
