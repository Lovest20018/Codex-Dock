"""Exercise Dock release selection and verified pending-update state without Android or network."""
import argparse
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--jdk', type=Path, required=True)
args = parser.parse_args()
out = ROOT / 'artifacts/android-update-regression'
out.mkdir(parents=True, exist_ok=True)
upstream = ROOT / 'src/CodexMobile/upstream/mobile/android/src/io/github/try2love/codexbridge'
owned = ROOT / 'src/CodexAndroid/java/io/github/try2love/codexbridge'
sources = [owned / 'MobileRelease.java'] + [upstream / name for name in ('GatewayURL.java', 'ArtifactDownload.java', 'UpdateInstallSession.java')]
subprocess.run([str(args.jdk / 'bin/javac.exe'), '-encoding', 'UTF-8', '-d', str(out),
                *map(str, sources), str(ROOT / 'tests/android/UpdateReleaseTests.java')], cwd=ROOT, check=True)
subprocess.run([str(args.jdk / 'bin/java.exe'), '-cp', str(out), 'io.github.try2love.codexbridge.UpdateReleaseTests', str(out)], cwd=ROOT, check=True)
