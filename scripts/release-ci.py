"""Validate and publish the paired Windows/Android release from one clean commit."""
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
    for name in ('release-' + windows, 'review-' + windows, 'release-android-' + android, 'review-android-' + android):
        if not (ROOT / 'docs' / (name + '.md')).is_file():
            raise ValueError('Missing release/review notes: ' + name)
    return windows, android


def commit():
    return subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()


def metadata():
    windows, android = versions()
    tag = 'v' + windows
    if os.environ.get('GITHUB_REF_TYPE') == 'tag' and os.environ.get('GITHUB_REF_NAME') != tag:
        raise ValueError('Release tag must match the Windows source version: ' + tag)
    values = {'tag': tag, 'windows': windows, 'android': android, 'commit': commit()}
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
                'assets/steering.js', 'assets/composer.js', 'assets/presentation.js') for name in archive.namelist()):
            raise ValueError('Test assets or signing keys found in production APK.')
    names = [stem + '.apk', source.name, manifest_path.name]
    (out / ('SHA256SUMS-Android-' + android + '.txt')).write_text(
        ''.join(f'{sha(out / name)}  {name}\n' for name in names), encoding='ascii')
    print('Android package and source inputs verified:', len(manifest['inputs']))


def release_files(directory, windows, android):
    names = [f'CodexDock-{windows}-Setup-win-x64.exe', f'CodexDock-{windows}-win-x64.zip',
             f'CodexDock-{windows}-source.zip', f'SHA256SUMS-{windows}.txt', f'installer-verification-{windows}.json',
             f'LovestCode-Android-{android}.apk', f'LovestCode-Android-{android}-source.zip',
             f'LovestCode-Android-{android}.manifest.json', f'SHA256SUMS-Android-{android}.txt']
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
    android_manifest = json.loads((directory / f'LovestCode-Android-{android}.manifest.json').read_text())
    android_meta = json.loads((ROOT / 'src/CodexAndroid/android.json').read_text())
    if (android_manifest['sourceCommit'] != commit() or android_manifest['dirtyBuild']
            or android_manifest['versionName'] != android
            or android_manifest['sha256'] != sha(directory / f'LovestCode-Android-{android}.apk')
            or android_manifest['signingCertificateSha256'] != android_meta['signingCertificateSha256']):
        raise ValueError('Android package must originate from the same clean release commit.')
    for line in (directory / f'SHA256SUMS-Android-{android}.txt').read_text().splitlines():
        digest, name = line.split('  ', 1)
        if Path(name).name != name or sha(directory / name) != digest:
            raise ValueError('Android checksum mismatch: ' + name)
    combined = directory / 'release-manifest.json'
    combined.write_text(json.dumps({'sourceCommit': commit(), 'windows': windows, 'android': android,
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


def publish(directory):
    windows, android = versions()
    files = release_files(directory, windows, android)
    tag = 'v' + windows
    api = GitHub()
    releases = api.request('/releases?per_page=100')
    prior = next((value for value in releases if value['tag_name'] == tag), None)
    if prior and not prior['draft']:
        raise ValueError('This version is already published; increment the Windows release version.')
    if prior:
        if prior['target_commitish'] != commit():
            raise ValueError('Existing draft belongs to a different commit.')
        release = prior
        for asset in release['assets']:
            api.request('/releases/assets/' + str(asset['id']), method='DELETE')
    else:
        notes = (ROOT / f'docs/release-{windows}.md').read_text(encoding='utf-8')
        notes += '\n\n' + (ROOT / f'docs/release-android-{android}.md').read_text(encoding='utf-8')
        release = api.request('/releases', method='POST', body={'tag_name': tag, 'target_commitish': commit(),
            'name': f'Codex Dock {windows} · Lovest Code {android}', 'body': notes, 'draft': True,
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
    api.request('/releases/' + str(release['id']), method='PATCH', body={'draft': False, 'prerelease': False, 'make_latest': 'true'})
    print('Published:', 'https://github.com/' + REPOSITORY + '/releases/tag/' + tag)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('metadata', 'prepare-android', 'package-android', 'verify', 'publish'))
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    if args.command == 'metadata':
        metadata()
    elif args.command == 'prepare-android':
        prepare_android()
    elif args.command == 'package-android':
        package_android()
    elif args.command == 'verify':
        windows, android = versions()
        print('Verified release assets:', len(release_files(args.directory, windows, android)))
    else:
        publish(args.directory)


if __name__ == '__main__':
    main()
