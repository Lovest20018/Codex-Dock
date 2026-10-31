"""Serve the real local enrollment host with a nonexistent native runtime."""
import json
import sys
import tempfile
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src/CodexMobile'))
from vendor_bootstrap import activate
UPSTREAM = activate()
from bridge.app.service import Bridge
from bridge.features.auth.auth import password_record
from gateway import PROTOCOL, Server
from local_manager import ManagerControlHandler
from workbench import Workbench


with tempfile.TemporaryDirectory() as temp:
    directory = Path(temp)
    home = directory / 'home'
    home.mkdir()
    config = {'protocol': PROTOCOL, 'publicUrl': 'https://fixture.invalid', 'port': 0, 'controlPort': 13091,
              'controlToken': 'b' * 64, 'instanceId': 'a' * 32,
              'auth': {'mode': 'password', 'username': 'fixture', 'sessionHours': 12, **password_record('isolated-manager-fixture')},
              'codexHome': str(home), 'dataDir': str(directory)}
    bridge = Bridge(home, directory, codex_bin=home / 'missing-native-runtime.exe', ipc_path=str(home / 'missing-ipc'))
    work = Workbench(config, bridge, UPSTREAM / 'web')
    control = Server(('127.0.0.1', 0), ManagerControlHandler, work.state)
    threads = [threading.Thread(target=server.serve_forever, daemon=True) for server in (control, work.public)]
    for thread in threads:
        thread.start()
    try:
        print(json.dumps({**work.manager.issue(control.server_port), 'publicPort': work.public.server_port}), flush=True)
        sys.stdin.readline()
    finally:
        control.shutdown()
        work.public.shutdown()
        for thread in threads:
            thread.join(timeout=2)
        control.server_close()
        work.close()
        bridge.close()
