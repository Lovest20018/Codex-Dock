"""Codex feature regressions from the pinned upstream Windows snapshot.

Windows machines without symlink privilege report those fixtures as skipped,
matching upstream's newer guarded path fixtures. Assertions are unchanged.
The changed login-lock behavior is covered by Dock's own auth/HTTP regressions.
"""
import fnmatch
import os
from contextlib import ExitStack
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2] / 'src/CodexMobile/upstream'
sys.path[:0] = [str(ROOT / 'tests'), str(ROOT)]
os.chdir(ROOT)
patterns = ('test_account*.py', 'test_auth.py', 'test_cli*.py', 'test_goal*.py',
            'test_ipc*.py', 'test_model*.py', 'test_session*.py', 'test_submission*.py',
            'test_wsl*.py', 'test_remote_catalog.py', 'test_remote_cli_models.py',
            'test_uploads_activity.py', 'test_history*.py', 'test_workspace.py',
            'test_windows_workspace.py', 'test_windows_discovery.py',
            'test_windows_session.py', 'test_windows_codex_quit.py',
            'test_code_comment_files.py', 'test_context_and_directory_links.py',
            'test_download_ranges.py', 'test_git_workspace.py', 'test_mobile_updates.py')

symlink_to = Path.symlink_to
def platform_symlink(self, *args, **kwargs):
    try:
        return symlink_to(self, *args, **kwargs)
    except OSError as error:
        if getattr(error, 'winerror', None) == 1314:
            raise unittest.SkipTest('Windows symlink privilege is unavailable') from None
        raise

if len(sys.argv) == 3 and sys.argv[1] == '--module':
    # Upstream modules patch shared HTTP/WSL fixtures. Keep those patches from
    # leaking between modules by using a separate interpreter for each module.
    suite = unittest.defaultTestLoader.loadTestsFromName(sys.argv[2])
    if sys.argv[2] == 'test_download_ranges' and os.name == 'nt':
        # This fixture creates its link inside a server callback, where a
        # SkipTest would be converted to HTTP 500. Probe before starting it.
        with tempfile.TemporaryDirectory() as directory:
            try:
                (Path(directory) / 'link').symlink_to(Path(directory), target_is_directory=True)
            except OSError as error:
                if getattr(error, 'winerror', None) != 1314:
                    raise
                import test_download_ranges
                case = test_download_ranges.DownloadHttpTests
                method = 'test_artifact_parent_symlink_swap_cannot_escape'
                setattr(case, method, unittest.skip('Windows symlink privilege is unavailable')(getattr(case, method)))
    with ExitStack() as fixtures:
        fixtures.enter_context(patch.object(Path, 'symlink_to', platform_symlink))
        if sys.argv[2] in ('test_client_scan', 'test_wsl_entries'):
            # Inventory-only fixture paths must not depend on this machine's
            # WSL service latency. Per-case inventory mocks still override it.
            fixtures.enter_context(patch('bridge.platforms.windows.wsl.running_distributions', return_value=[]))
        result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)

selected = [path for path in sorted((ROOT / 'tests').glob('test_*.py'))
            if any(fnmatch.fnmatchcase(path.name, pattern) for pattern in patterns)]
print('Upstream Codex feature modules:', len(selected), flush=True)
failed = []
for path in selected:
    print('\nMODULE', path.stem, flush=True)
    completed = subprocess.run([sys.executable, '-B', str(Path(__file__).resolve()), '--module', path.stem])
    if completed.returncode:
        failed.append(path.stem)
print('\nModule summary:', len(selected) - len(failed), 'passed;', len(failed), 'failed', flush=True)
if failed:
    print('Failed modules:', ', '.join(failed), flush=True)
sys.exit(1 if failed else 0)
