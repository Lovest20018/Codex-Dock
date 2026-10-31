"""Locate the pinned upstream package without module aliases or user packages."""
import sys
from pathlib import Path


def activate():
    source = Path(__file__).resolve().parent / 'upstream'
    if source.is_dir():
        sys.path.insert(0, str(source))
    else:
        source = Path(getattr(sys, '_MEIPASS', Path(__file__).resolve().parent))
    return source
