"""Freeze the pinned workbench and its native/resource dependencies."""
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
from importlib.metadata import distribution

import certifi

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/CodexMobile'
UPSTREAM = SOURCE / 'upstream'
ARTIFACTS = ROOT / 'artifacts'


def main():
    runpy.run_path(str(ROOT / 'scripts/verify-mobile-upstream.py'))['verify']()
    # Use the upstream lock, metadata checks and digest verification unchanged.
    conpty = runpy.run_path(str(UPSTREAM / 'scripts/bundle-conpty.py'))['bundle']()
    helpers = ARTIFACTS / 'mobile-native'
    helpers.mkdir(exist_ok=True)
    framework = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Microsoft.NET/Framework64/v4.0.30319'
    helper = helpers / 'codex-quit-helper.exe'
    subprocess.run([str(framework / 'csc.exe'), '/nologo', '/target:exe', '/platform:x64',
                    '/out:' + str(helper), *['/reference:' + str(framework / 'WPF' / name)
                    for name in ('UIAutomationClient.dll', 'UIAutomationTypes.dll', 'WindowsBase.dll')],
                    str(UPSTREAM / 'bridge/platforms/windows/codex-quit-helper.cs')], check=True)
    subprocess.run([str(helper), '--self-check'], check=True)
    command = [sys.executable, '-m', 'PyInstaller', '--noconfirm', '--clean', '--onedir', '--console',
               '--noupx', '--name', 'CodexDock.Mobile', '--contents-directory', 'runtime',
               '--distpath', str(ARTIFACTS / 'mobile-worker'), '--workpath', str(ARTIFACTS / 'mobile-build'),
               '--specpath', str(ARTIFACTS / 'mobile-spec'), '--paths', str(UPSTREAM)]
    def data(source, destination):
        command.extend(['--add-data', str(source) + ';' + destination])
    data(UPSTREAM / 'web', 'web')
    data(SOURCE / 'manager', 'manager')
    data(SOURCE / 'upstream-manifest.json', 'licenses')
    data(SOURCE / 'upstream-overlay.json', 'licenses')
    for name in ('UPSTREAM-LICENSE', 'UPSTREAM.md'):
        data(SOURCE / name, 'licenses')
    # Remote adapters inject Python source; frozen bytecode cannot replace it.
    for relative in json.loads((UPSTREAM / 'scripts/gateway-resources.json').read_text(encoding='utf-8')):
        if relative.startswith('bridge/'):
            data(UPSTREAM / relative, str(Path(relative).parent))
    data(certifi.where(), 'bridge/features/auth')
    package = distribution('certifi')
    license_file = next(file for file in package.files if file.name == 'LICENSE')
    data(package.locate_file(license_file), 'licenses/certifi')
    for relative in ('conpty.dll', 'x86/OpenConsole.exe', 'x64/OpenConsole.exe', 'arm64/OpenConsole.exe'):
        command.extend(['--add-binary', str(conpty / relative) + ';' + str(Path('conpty') / Path(relative).parent)])
    for name in ('LICENSE', 'version.json'):
        data(conpty / name, 'conpty')
    command.extend(['--add-binary', str(helper) + ';client-helpers'])
    for platform in ('linux', 'macos', 'windows'):
        command.extend(['--hidden-import', 'bridge.platforms.' + platform + '.interfaces'])
    command.append(str(SOURCE / 'main.py'))
    subprocess.run(command, cwd=ROOT, check=True)


if __name__ == '__main__':
    main()
