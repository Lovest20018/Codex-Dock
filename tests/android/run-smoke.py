"""Build separate instrumentation, install only on an existing isolated AVD, verify actual APK."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import threading
import zipfile

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--jdk', type=Path, required=True)
parser.add_argument('--sdk', type=Path, required=True)
parser.add_argument('--apk', type=Path, required=True)
parser.add_argument('--device', default='emulator-5562')
parser.add_argument('--signing-home', type=Path, default=Path(os.environ['LOCALAPPDATA']) / 'CodexDock/android-signing')
args = parser.parse_args()
if not args.device.startswith('emulator-'):
    parser.error('This automated test is limited to an isolated emulator.')
out = ROOT / 'artifacts/android-smoke'
out.mkdir(parents=True, exist_ok=True)
tools = args.sdk / 'build-tools/35.0.0'
android = args.sdk / 'platforms/android-35/android.jar'
env = {**os.environ, 'JAVA_HOME': str(args.jdk), 'PATH': str(args.jdk / 'bin') + os.pathsep + os.environ['PATH'],
       'CODEX_DOCK_ANDROID_STORE_PASSWORD': (args.signing_home / 'store-password.txt').read_text().strip()}


def run(*command, capture=False):
    return subprocess.run([str(a) for a in command], cwd=ROOT, env=env, check=True,
                          text=True, encoding='utf-8', errors='replace', stdout=subprocess.PIPE if capture else None).stdout


def adb(*command, capture=False):
    return run(args.sdk / 'platform-tools/adb.exe', '-s', args.device, *command, capture=capture)


# Catch missing, stale or accidentally production-embedded test classes.
with zipfile.ZipFile(args.apk) as archive:
    names = archive.namelist()
    assert not any('Smoke' in n or n.endswith(('.p12', '.jks')) for n in names)
    for name in ('assets/mobile-ui.js', 'assets/mobile-clipboard.js', 'assets/mobile-system-bars.js', 'assets/mobile-session.js'):
        assert name in names
    assert 'assets/codex.png' in names and 'assets/claude.png' not in names and 'assets/deepseek.png' not in names
manifest = out / 'AndroidManifest.xml'
manifest.write_text('''<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="io.github.lovest20018.codexdock.test"><uses-sdk android:minSdkVersion="26" android:targetSdkVersion="35"/><application android:label="Dock test"/><instrumentation android:name="io.github.lovest20018.codexdock.test.AndroidSmoke" android:targetPackage="io.github.lovest20018.codexdock"/></manifest>''')
(out / 'classes').mkdir(exist_ok=True)
(out / 'dex').mkdir(exist_ok=True)
run(tools / 'aapt2.exe', 'link', '-I', android, '--manifest', manifest, '-o', out / 'base.apk')
run(args.jdk / 'bin/javac.exe', '-encoding', 'UTF-8', '--release', '8', '-classpath', android, '-d', out / 'classes', ROOT / 'tests/android/AndroidSmoke.java')
with zipfile.ZipFile(out / 'classes.jar', 'w') as archive:
    for p in (out / 'classes').rglob('*.class'):
        archive.write(p, p.relative_to(out / 'classes').as_posix())
run(tools / 'd8.bat', '--lib', android, '--min-api', '26', '--output', out / 'dex', out / 'classes.jar')
with zipfile.ZipFile(out / 'base.apk') as source, zipfile.ZipFile(out / 'unsigned.apk', 'w') as dest:
    for item in source.infolist():
        dest.writestr(item, source.read(item))
    dest.write(out / 'dex/classes.dex', 'classes.dex')
    dest.write(ROOT / 'tests/android/presentation.js', 'assets/presentation.js')
run(tools / 'zipalign.exe', '-f', '4', out / 'unsigned.apk', out / 'aligned.apk')
run(tools / 'apksigner.bat', 'sign', '--ks', args.signing_home / 'codex-dock.p12', '--ks-key-alias', 'codex-dock', '--ks-pass', 'env:CODEX_DOCK_ANDROID_STORE_PASSWORD', '--out', out / 'test.apk', out / 'aligned.apk')
fixture = subprocess.Popen([sys.executable, '-B', ROOT / 'tests/android/gateway_fixture.py'], cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=(out / 'fixture-errors.log').open('w'), text=True)
try:
    # Read only the fixture's machine-readable endpoint, with a bounded startup.
    ready = threading.Event()
    line = []
    def read_ready():
        line.append(fixture.stdout.readline())
        ready.set()
    threading.Thread(target=read_ready, daemon=True).start()
    if not ready.wait(20):
        raise RuntimeError('Fixture startup timeout')
    endpoint = json.loads(line[0])
    port = str(endpoint['port'])
    adb('reverse', 'tcp:' + port, 'tcp:' + port)
    previous = ROOT / 'artifacts/LovestCode-Android-0.2.0.apk'
    if not previous.exists():
        previous = ROOT / 'artifacts/LovestCode-Android-0.1.1.apk'
    adb('install', '-r', out / 'test.apk')
    # Downgrading only this disposable emulator is necessary on repeated QA runs.
    if previous.exists():
        if adb('shell', 'pm', 'path', 'io.github.lovest20018.codexdock', capture=True).strip():
            adb('uninstall', 'io.github.lovest20018.codexdock')
        adb('install', previous)
        seeded = adb('shell', 'am', 'instrument', '-w', '-e', 'mode', 'seed', 'io.github.lovest20018.codexdock.test/io.github.lovest20018.codexdock.test.AndroidSmoke', capture=True)
        assert 'SEEDED' in seeded
    adb('install', '-r', args.apk.resolve())
    if previous.exists():
        upgraded = adb('shell', 'am', 'instrument', '-w', '-e', 'mode', 'upgrade', 'io.github.lovest20018.codexdock.test/io.github.lovest20018.codexdock.test.AndroidSmoke', capture=True)
        assert 'UPGRADE_PASS' in upgraded

    adb('shell', 'pm', 'grant', 'io.github.lovest20018.codexdock', 'android.permission.POST_NOTIFICATIONS')
    result = adb('shell', 'am', 'instrument', '-w', '-r', '-e', 'base', endpoint['url'], 'io.github.lovest20018.codexdock.test/io.github.lovest20018.codexdock.test.AndroidSmoke', capture=True)
    (out / 'instrumentation.txt').write_text(result, encoding='utf-8')
    print(result)
    adb('pull', '/sdcard/Android/data/io.github.lovest20018.codexdock/files', out / 'screenshots')
    report = re.search(r'INSTRUMENTATION_RESULT: report=(.*)', result)
    if not report or not report[1].startswith('{'):
        raise RuntimeError('Instrumentation failed; inspect instrumentation.txt and screenshots.')
    data = json.loads(report[1])
    assert data['status'] == 'PASS'
    data['apk'] = args.apk.name
    data['upgradeFrom'] = previous.name if previous.exists() else None
    (out / 'report.json').write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
finally:
    if 'port' in locals():
        adb('reverse', '--remove', 'tcp:' + port)
    fixture.stdin.write('\n')
    fixture.stdin.flush()
    try:
        fixture.wait(timeout=8)
    except subprocess.TimeoutExpired:
        fixture.kill()
        fixture.wait()
