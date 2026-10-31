"""Reuse upstream's real ConPTY acceptance with Dock's runtime directory name."""
from pathlib import Path

original = Path(__file__).resolve().parents[2] / 'src/CodexMobile/upstream/tests/acceptance/windows_terminal_http.py'
source = original.read_text(encoding='utf-8')
expected = "gateway.parent / '_internal'"
if source.count(expected) != 1:
    raise RuntimeError('Review changed upstream terminal acceptance layout before running')
source = source.replace(expected, "gateway.parent / 'runtime'")
exec(compile(source, str(original), 'exec'), {'__file__': str(original), '__name__': '__main__'})
