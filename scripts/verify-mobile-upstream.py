"""Verify every reused upstream byte and each explicit Dock overlay."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / 'src/CodexMobile'


def verify():
    baseline = json.loads((ROOT / 'upstream-manifest.json').read_text(encoding='utf-8'))
    overlay = json.loads((ROOT / 'upstream-overlay.json').read_text(encoding='utf-8'))
    if baseline['commit'] != overlay['commit']:
        raise ValueError('Upstream and overlay commits differ')
    files = baseline['files']
    changes, omitted = overlay['files'], overlay['omitted']
    if set(changes) & set(omitted) or not (set(changes) | set(omitted)) <= set(files):
        raise ValueError('Overlay includes an unknown path')
    for relative, digest in files.items():
        path = ROOT / 'upstream' / relative
        if relative in omitted:
            if path.exists():
                raise ValueError('An omitted deployment template reappeared: ' + relative)
            continue
        expected = changes.get(relative, {}).get('sha256', digest)
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError('Upstream source differs from its reviewed manifest: ' + relative)
    print(f'Upstream verified: {len(files) - len(omitted)} files, {len(changes)} explicit overlays, commit {baseline["commit"]}')


if __name__ == '__main__':
    verify()
