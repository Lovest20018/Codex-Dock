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
        (self.out / f'installer-verification-{w}.json').write_text(json.dumps({'status': 'PASS', 'installerSha256': release.sha(self.out / windows_names[0])}))
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

    def test_verified_packages_produce_combined_manifest(self):
        files = release.release_files(self.out, self.windows, self.android)
        self.assertEqual(len(files), 10)
        manifest = json.loads((self.out / 'release-manifest.json').read_text())
        self.assertEqual(manifest['sourceCommit'], 'a' * 40)
        self.assertEqual(len(manifest['assets']), 9)

    def test_mixed_source_commits_are_rejected(self):
        self.android_data['sourceCommit'] = 'b' * 40
        self.write_android()
        with self.assertRaisesRegex(ValueError, 'same clean release commit'):
            release.release_files(self.out, self.windows, self.android)

    def test_different_android_certificate_is_rejected(self):
        self.android_data['signingCertificateSha256'] = '0' * 64
        self.write_android()
        with self.assertRaises(ValueError):
            release.release_files(self.out, self.windows, self.android)

    def test_changed_installer_is_rejected(self):
        (self.out / f'CodexDock-{self.windows}-Setup-win-x64.exe').write_bytes(b'changed installer')
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            release.release_files(self.out, self.windows, self.android)

    def test_failed_installer_verification_is_rejected(self):
        (self.out / f'installer-verification-{self.windows}.json').write_text('{"status":"FAIL"}')
        with self.assertRaisesRegex(ValueError, 'installer verification'):
            release.release_files(self.out, self.windows, self.android)

    def test_tag_version_mismatch_fails_before_build(self):
        with patch.dict(os.environ, {'GITHUB_REF_TYPE': 'tag', 'GITHUB_REF_NAME': 'v99.0.0'}):
            with self.assertRaisesRegex(ValueError, 'tag must match'):
                release.metadata()

    def test_existing_stable_release_is_never_modified(self):
        with patch.object(release, 'GitHub') as github:
            github.return_value.request.return_value = [{'tag_name': 'v' + self.windows, 'draft': False}]
            with self.assertRaisesRegex(ValueError, 'already published'):
                release.publish(self.out)
            github.return_value.request.assert_called_once_with('/releases?per_page=100')


if __name__ == '__main__':
    unittest.main()
