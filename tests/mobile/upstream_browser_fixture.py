"""Run unchanged upstream scenarios with the production Windows TCP teardown."""
import argparse
import runpy
import sys
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / 'src/CodexMobile'
sys.path.insert(0, str(SOURCE))
from vendor_bootstrap import activate
UPSTREAM = activate()
from gateway import Server

fixture = runpy.run_path(str(UPSTREAM / 'tests/browser/bootstrap-fixture.py'))
fixture['Handler'].protocol_version = 'HTTP/1.1'
parser = argparse.ArgumentParser()
parser.add_argument('--port', type=int, default=0)
options = parser.parse_args()
server = Server(('127.0.0.1', options.port), fixture['Handler'], None)
server.request_queue_size = 128
print(f'http://127.0.0.1:{server.server_port}', flush=True)
server.serve_forever()
