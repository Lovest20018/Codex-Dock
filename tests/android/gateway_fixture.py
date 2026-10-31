"""Actual Dock gateway behind a loopback-only HTTP fixture for an isolated AVD.

Maps the test origin and Secure cookie attribute for emulator-local HTTP only;
does not change production auth code. No native Codex runtime or user data.
"""
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import sys
import tempfile
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'src/CodexMobile'))
from vendor_bootstrap import activate
UPSTREAM = activate()
from bridge.app.service import Bridge
from bridge.features.auth.auth import password_record
from gateway import PROTOCOL
from workbench import Workbench


with tempfile.TemporaryDirectory(prefix='dock-android-fixture-') as temp:
    directory = Path(temp)
    home = directory / 'home'
    home.mkdir()
    owner = None
    if '--steer' in sys.argv:
        sys.path.insert(0, str(ROOT / 'tests/mobile'))
        from steering_fixture import SteeringOwner
        owner = SteeringOwner(home)
    config = {'protocol': PROTOCOL, 'publicUrl': 'https://fixture.invalid', 'port': 0,
              'controlPort': 13091, 'controlToken': 'b' * 64, 'instanceId': 'a' * 32,
              'auth': {'mode': 'password', 'username': 'fixture', 'sessionHours': 12,
                       **password_record('isolated-android-fixture')},
              'codexHome': str(home), 'dataDir': str(directory)}
    bridge = Bridge(home, directory, codex_bin=home / 'missing-native-runtime.exe',
                    ipc_path=owner.path if owner else str(home / 'missing-ipc'))
    work = Workbench(config, bridge, UPSTREAM / 'web')

    class Proxy(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.1'

        def log_message(self, *_):
            pass

        def do_GET(self):
            self.forward()

        def do_POST(self):
            self.forward()

        def forward(self):
            if owner and self.path.startswith('/__qa__/'):
                length = int(self.headers.get('Content-Length', '0'))
                if self.path == '/__qa__/finish' and self.command == 'POST':
                    self.rfile.read(length)
                    owner.set_active(False)
                value = json.dumps(owner.status(bridge), ensure_ascii=False).encode('utf-8')
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(value)))
                self.end_headers()
                self.wfile.write(value)
                return
            headers = dict(self.headers)
            headers['Host'] = 'fixture.invalid'
            if 'Origin' in headers:
                headers['Origin'] = 'https://fixture.invalid'
            body = self.rfile.read(int(headers.get('Content-Length', '0')))
            connection = http.client.HTTPConnection('127.0.0.1', work.public.server_port, timeout=12)
            try:
                connection.request(self.command, self.path, body, headers)
                response = connection.getresponse()
                self.send_response(response.status)
                for key, value in response.getheaders():
                    if key.lower() not in ('connection', 'transfer-encoding'):
                        if key.lower() == 'set-cookie':
                            value = value.replace('; Secure', '')
                        self.send_header(key, value)
                self.send_header('Connection', 'close')
                self.end_headers()
                while chunk := response.read(65536):
                    self.wfile.write(chunk)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                pass
            finally:
                connection.close()
                self.close_connection = True

    proxy = ThreadingHTTPServer(('127.0.0.1', 0), Proxy)
    for server in (work.public, proxy):
        threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        print(json.dumps({'port': proxy.server_port, 'url': f'http://127.0.0.1:{proxy.server_port}'}), flush=True)
        sys.stdin.readline()
    finally:
        proxy.shutdown()
        work.public.shutdown()
        proxy.server_close()
        work.close()
        bridge.close()
        if owner:
            owner.close()
