"""Validate and publish Windows and Android as independent releases."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = 'Lovest20018/Codex-Dock'


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def versions():
    windows = ET.parse(ROOT / 'src/CodexConfig.App/CodexConfig.App.csproj').findtext('.//Version')
    android = json.loads((ROOT / 'src/CodexAndroid/android.json').read_text(encoding='utf-8'))['versionName']
    if not all(re.fullmatch(r'\d+\.\d+\.\d+', value or '') for value in (windows, android)):
        raise ValueError('Release versions must be semantic versions.')
    return windows, android


def commit():
    return subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()


def release_metadata(platform):
    windows, android = versions()
    if platform not in ('windows', 'android'):
        raise ValueError('Choose exactly one release platform: windows or android.')
    version = windows if platform == 'windows' else android
    notes = ('release-' if platform == 'windows' else 'release-android-') + version
    review = ('review-' if platform == 'windows' else 'review-android-') + version
    for name in (notes, review):
        if not (ROOT / 'docs' / (name + '.md')).is_file():
            raise ValueError('Missing release/review notes: ' + name)
    return {'platform': platform, 'version': version, 'tag': ('v' if platform == 'windows' else 'android-v') + version,
            'windows': windows, 'android': android, 'commit': commit(), 'notes': notes}


def metadata(platform=None):
    if not platform:
        platform = 'android' if os.environ.get('GITHUB_REF_NAME', '').startswith('android-v') else 'windows'
    values = release_metadata(platform)
    if os.environ.get('GITHUB_REF_TYPE') == 'tag' and os.environ.get('GITHUB_REF_NAME') != values['tag']:
        raise ValueError('Release tag must match the selected platform source version: ' + values['tag'])
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
            output.write(''.join(f'{key}={value}\n' for key, value in values.items()))
    print(json.dumps(values, indent=2))


def prepare_android():
    meta = json.loads((ROOT / 'src/CodexAndroid/android.json').read_text(encoding='utf-8'))
    target = ROOT / 'artifacts/android-deps/zxing-core-3.5.3.jar'
    target.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(meta['dependency']['url'], timeout=90) as response:
        target.write_bytes(response.read())
    if sha(target) != meta['dependency']['sha256']:
        raise ValueError('Pinned ZXing SHA-256 mismatch.')
    print('Pinned Android dependency verified.')


def package_android():
    _, android = versions()
    stem = 'LovestCode-Android-' + android
    out = ROOT / 'artifacts'
    source = out / (stem + '-source.zip')
    subprocess.run(['git', 'archive', '--format=zip', '--prefix=' + stem + '-source/',
                    '-o', str(source), 'HEAD'], cwd=ROOT, check=True)
    manifest_path = out / (stem + '.manifest.json')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    if manifest['dirtyBuild'] or manifest['sourceCommit'] != commit() or manifest['sha256'] != sha(out / manifest['apk']):
        raise ValueError('Android manifest does not match this clean commit and APK.')
    with zipfile.ZipFile(source) as archive:
        for name, digest in manifest['inputs'].items():
            if hashlib.sha256(archive.read(stem + '-source/' + name)).hexdigest() != digest:
                raise ValueError('Android source archive input mismatch: ' + name)
    with zipfile.ZipFile(out / manifest['apk']) as archive:
        if any('Smoke' in name or name.endswith(('.p12', '.jks')) or name in (
                  'assets/steering.js', 'assets/composer.js', 'assets/presentation.js',
                  'assets/update-fixture.apk', 'assets/bad-update-fixture.apk') for name in archive.namelist()):
            raise ValueError('Test assets or signing keys found in production APK.')
    names = [stem + '.apk', source.name, manifest_path.name]
    (out / ('SHA256SUMS-Android-' + android + '.txt')).write_text(
        ''.join(f'{sha(out / name)}  {name}\n' for name in names), encoding='ascii')
    print('Android package and source inputs verified:', len(manifest['inputs']))


def windows_files(directory, windows):
    names = [f'CodexDock-{windows}-Setup-win-x64.exe', f'CodexDock-{windows}-win-x64.zip',
             f'CodexDock-{windows}-source.zip', f'SHA256SUMS-{windows}.txt', f'installer-verification-{windows}.json']
    paths = [directory / name for name in names]
    for path in paths:
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError('Missing release asset: ' + path.name)
    with zipfile.ZipFile(directory / f'CodexDock-{windows}-win-x64.zip') as archive:
        manifest = json.loads(archive.read(f'CodexDock-{windows}-win-x64/manifest.json'))
        ui = json.loads(archive.read(f'CodexDock-{windows}-win-x64/verification/ui-report.json'))
        if manifest['sourceCommit'] != commit() or manifest['version'] != windows or ui['status'] != 'PASS':
            raise ValueError('Windows package provenance or UI check failed.')
        for line in (directory / f'SHA256SUMS-{windows}.txt').read_text().splitlines():
            digest, name = line.split('  ', 1)
            actual = sha(directory / name) if '/' not in name else hashlib.sha256(archive.read(name)).hexdigest()
            if actual != digest:
                raise ValueError('Windows checksum mismatch: ' + name)
    installer = json.loads((directory / f'installer-verification-{windows}.json').read_text(encoding='utf-8-sig'))
    if installer['status'] != 'PASS' or installer['installerSha256'] != sha(directory / f'CodexDock-{windows}-Setup-win-x64.exe'):
        raise ValueError('Windows installer verification does not match the release.')
    if any(installer.get(key) is not True for key in ('runningMobileUpgrade', 'otherInstallationWorkerPreserved', 'mobileSettingsUnchanged')):
        raise ValueError('Windows installer verification must cover the running mobile upgrade and installation isolation.')
    return paths


def android_files(directory, android):
    names = [f'LovestCode-Android-{android}.apk', f'LovestCode-Android-{android}-source.zip',
             f'LovestCode-Android-{android}.manifest.json', f'SHA256SUMS-Android-{android}.txt']
    paths = [directory / name for name in names]
    for path in paths:
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError('Missing release asset: ' + path.name)
    android_manifest = json.loads((directory / f'LovestCode-Android-{android}.manifest.json').read_text())
    android_meta = json.loads((ROOT / 'src/CodexAndroid/android.json').read_text())
    if (android_manifest['sourceCommit'] != commit() or android_manifest['dirtyBuild']
            or android_manifest['versionName'] != android
            or android_manifest['sha256'] != sha(directory / f'LovestCode-Android-{android}.apk')
            or android_manifest['signingCertificateSha256'] != android_meta['signingCertificateSha256']):
        raise ValueError('Android package must originate from the selected clean release commit.')
    for line in (directory / f'SHA256SUMS-Android-{android}.txt').read_text().splitlines():
        digest, name = line.split('  ', 1)
        if Path(name).name != name or sha(directory / name) != digest:
            raise ValueError('Android checksum mismatch: ' + name)
    return paths


def release_files(directory, platform):
    meta = release_metadata(platform)
    paths = windows_files(directory, meta['version']) if platform == 'windows' else android_files(directory, meta['version'])
    combined = directory / 'release-manifest.json'
    combined.write_text(json.dumps({'sourceCommit': commit(), 'platform': platform, 'version': meta['version'],
        'assets': [{'name': path.name, 'size': path.stat().st_size, 'sha256': sha(path)} for path in paths]}, indent=2) + '\n', encoding='utf-8')
    return paths + [combined]


class GitHub:
    def __init__(self):
        self.token = os.environ['GH_TOKEN']

    def request(self, path, method='GET', body=None, upload=None):
        url = path if path.startswith('https://') else 'https://api.github.com/repos/' + REPOSITORY + path
        if urllib.parse.urlsplit(url).hostname not in ('api.github.com', 'uploads.github.com'):
            raise ValueError('Unexpected GitHub API host.')
        data = upload.read_bytes() if upload else json.dumps(body).encode() if body is not None else None
        headers = {'Authorization': 'Bearer ' + self.token, 'Accept': 'application/vnd.github+json',
                   'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'Codex-Dock-release',
                   'Content-Type': 'application/octet-stream' if upload else 'application/json'}
        try:
            with urllib.request.urlopen(urllib.request.Request(url, data=data, headers=headers, method=method), timeout=240) as response:
                result = response.read()
                return json.loads(result) if result else None
        except urllib.error.HTTPError as error:
            detail = json.loads(error.read()).get('message', 'request failed')
            raise RuntimeError(f'GitHub {method}: HTTP {error.code}: {detail}') from None


def publish(directory, platform):
    meta = release_metadata(platform)
    files = release_files(directory, platform)
    tag = meta['tag']
    api = GitHub()
    releases = api.request('/releases?per_page=100')
    prior = next((value for value in releases if value['tag_name'] == tag), None)
    if prior and not prior['draft']:
        raise ValueError('This version is already published; increment the selected platform release version.')
    reference = api.request('/git/ref/tags/' + tag)['object']
    for _ in range(5):
        if reference['type'] == 'commit':
            break
        if reference['type'] != 'tag':
            raise ValueError('Release tag does not resolve to a commit.')
        reference = api.request('/git/tags/' + reference['sha'])['object']
    if reference['type'] != 'commit' or reference['sha'] != commit():
        raise ValueError('Release tag must point to this exact source commit.')
    if prior:
        if prior['target_commitish'] != commit():
            raise ValueError('Existing draft belongs to a different commit.')
        release = prior
        for asset in release['assets']:
            api.request('/releases/assets/' + str(asset['id']), method='DELETE')
    else:
        notes = (ROOT / ('docs/' + meta['notes'] + '.md')).read_text(encoding='utf-8')
        name = ('Codex Dock for Windows ' if platform == 'windows' else 'Lovest Code for Android ') + meta['version']
        release = api.request('/releases', method='POST', body={'tag_name': tag, 'target_commitish': commit(),
            'name': name, 'body': notes, 'draft': True,
            'prerelease': False, 'make_latest': 'false'})
    for path in files:
        upload_url = release['upload_url'].split('{')[0] + '?name=' + urllib.parse.quote(path.name)
        asset = api.request(upload_url, method='POST', upload=path)
        if asset['size'] != path.stat().st_size or asset.get('digest') != 'sha256:' + sha(path):
            raise ValueError('Uploaded asset digest or size mismatch: ' + path.name)
        print('Verified uploaded asset:', path.name)
    remote = api.request('/releases/' + str(release['id']))
    if {value['name'] for value in remote['assets']} != {path.name for path in files}:
        raise ValueError('Remote release assets differ from the verified file list.')
    # Keep /releases/latest on Windows for installed versions of the Dock updater.
    api.request('/releases/' + str(release['id']), method='PATCH', body={'draft': False, 'prerelease': False, 'make_latest': 'true' if platform == 'windows' else 'false'})
    print('Published:', 'https://github.com/' + REPOSITORY + '/releases/tag/' + tag)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('metadata', 'prepare-android', 'package-android', 'verify', 'publish'))
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--platform', choices=('windows', 'android'), default=os.environ.get('RELEASE_PLATFORM') or None)
    args = parser.parse_args()
    if args.command == 'metadata':
        metadata(args.platform)
    elif args.command == 'prepare-android':
        prepare_android()
    elif args.command == 'package-android':
        package_android()
    elif args.command == 'verify':
        print('Verified release assets:', len(release_files(args.directory, args.platform)))
    else:
        publish(args.directory, args.platform)


if __name__ == '__main__':
    main()
