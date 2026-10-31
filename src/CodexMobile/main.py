"""Boot configuration arrives over stdin, never in command-line arguments or logs."""
import json
import os
import re
import sys
import threading
from contextlib import contextmanager
from pathlib import Path

from gateway import PROTOCOL, ControlHandler, PublicHandler, Server, State, public_origin
from service import Bridge


@contextmanager
def worker_lock(directory):
    with (directory / 'worker.lock').open('a+b') as stream:
        if os.name == 'nt':
            import msvcrt
            stream.write(b'0')
            stream.flush()
            stream.seek(0)
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield


def validate(config):
    if not isinstance(config, dict) or set(config) != {'protocol', 'publicUrl', 'port', 'controlPort', 'controlToken', 'instanceId', 'auth', 'codexHome', 'dataDir'}:
        raise ValueError('Invalid boot configuration')
    if config['protocol'] != PROTOCOL or public_origin(config['publicUrl']) != config['publicUrl']:
        raise ValueError('Invalid protocol or origin')
    if not all(type(config[key]) is int and 1024 <= config[key] <= 65535 for key in ('port', 'controlPort')) or config['port'] == config['controlPort']:
        raise ValueError('Invalid ports')
    if not re.fullmatch(r'[a-f0-9]{64}', config['controlToken']) or not re.fullmatch(r'[a-f0-9]{32}', config['instanceId']):
        raise ValueError('Invalid identity')
    auth = config['auth']
    if (not isinstance(auth, dict) or auth.get('mode') != 'password' or not isinstance(auth.get('username'), str)
            or not 1 <= len(auth['username']) <= 200 or auth.get('iterations') != 600000 or auth.get('sessionHours') != 12
            or not re.fullmatch(r'[a-f0-9]{32}', auth.get('salt', '')) or not re.fullmatch(r'[a-f0-9]{64}', auth.get('hash', ''))):
        raise ValueError('Invalid authentication policy')
    for key in ('codexHome', 'dataDir'):
        if not isinstance(config[key], str) or not Path(config[key]).is_absolute() or not Path(config[key]).is_dir():
            raise ValueError('Explicit existing directories are required')
    return config


def run(config):
    config = validate(config)
    directory = Path(config['dataDir'])
    with worker_lock(directory):
        bridge = Bridge(config['codexHome'], directory)
        public = control = None
        try:
            state = State(config, bridge, Path(__file__).resolve().parent / 'web')
            public = Server(('0.0.0.0', config['port']), PublicHandler, state)
            control = Server(('127.0.0.1', config['controlPort']), ControlHandler, state)
            threads = [threading.Thread(target=server.serve_forever, daemon=True) for server in (public, control)]
            for thread in threads:
                thread.start()
            state.stop.wait()
            for server in (public, control):
                server.shutdown()
            for thread in threads:
                thread.join(timeout=2)
        finally:
            for server in (public, control):
                if server is not None:
                    server.server_close()
            bridge.close()


if __name__ == '__main__':
    try:
        if len(sys.argv) != 1 or sys.stdin is None:
            raise ValueError('Private boot configuration required')
        raw = sys.stdin.buffer.read(65537)
        if not 0 < len(raw) <= 65536:
            raise ValueError('Invalid boot configuration length')
        run(json.loads(raw))
    except Exception:
        # WPF reports startup failure. No secret-bearing config in diagnostics.
        sys.exit(2)
