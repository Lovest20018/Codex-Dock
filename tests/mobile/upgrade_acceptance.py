"""Cross-version EXE acceptance with fake data, cookies and uncertain receipts."""
import argparse
import hashlib
import http.client
import json
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import uuid


def free_port():
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        return listener.getsockname()[1]


parser = argparse.ArgumentParser()
parser.add_argument('--legacy', type=Path, required=True)
parser.add_argument('--worker', type=Path, required=True)
args = parser.parse_args()
with tempfile.TemporaryDirectory() as temporary:
    directory = Path(temporary)
    home, data = directory/'home', directory/'data'
    home.mkdir(); data.mkdir()
    original = '# isolated cross-version upgrade; no native chats\n'
    (home/'config.toml').write_text(original, encoding='utf-8')
    password, salt = secrets.token_urlsafe(24), secrets.token_hex(16)
    config = {'protocol': 3, 'publicUrl': 'https://upgrade.example.com', 'port': free_port(),
              'controlPort': free_port(), 'controlToken': secrets.token_hex(32),
              'instanceId': secrets.token_hex(16), 'codexHome': str(home), 'dataDir': str(data),
              'auth': {'mode': 'password', 'username': 'fixture', 'sessionHours': 12,
                       'iterations': 600000, 'salt': salt,
                       'hash': hashlib.pbkdf2_hmac('sha256', password.encode(), bytes.fromhex(salt), 600000, dklen=32).hex()}}
    cookie = None
    process = None

    def request(path, value=None, private=False):
        port = config['controlPort' if private else 'port']
        headers = {'Host': f'127.0.0.1:{port}' if private else 'upgrade.example.com',
                   'User-Agent': 'BridgeMobile/2.1.7-Android'}
        if private:
            headers['Authorization'] = 'Bearer '+config['controlToken']
        if cookie:
            headers['Cookie'] = cookie
        if value is not None:
            headers['Content-Type'] = 'application/json'
            if not private:
                headers['Origin'] = config['publicUrl']
        connection = http.client.HTTPConnection('127.0.0.1', port, timeout=3)
        try:
            connection.request('GET' if value is None else 'POST', path,
                               None if value is None else json.dumps(value), headers)
            response = connection.getresponse()
            body = json.loads(response.read())
            assert response.status == 200, (path, response.status)
            return body, response.getheader('Set-Cookie')
        finally:
            connection.close()

    def start(executable):
        global process
        process = subprocess.Popen([str(executable.resolve(strict=True))], stdin=subprocess.PIPE,
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        process.stdin.write(json.dumps(config).encode()); process.stdin.close()
        deadline = time.monotonic()+15
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise AssertionError('Worker failed to start: '+str(process.returncode))
            try:
                value, _ = request('/health', private=True)
                assert value['protocol'] == config['protocol']
                return
            except (OSError, http.client.HTTPException):
                time.sleep(.1)
        raise AssertionError('Worker startup deadline exceeded')

    def stop():
        if process and process.poll() is None:
            request('/stop', {}, private=True)
            process.wait(timeout=10)

    try:
        start(args.legacy)
        _, headers = request('/api/login', {'username': 'fixture', 'password': password})
        cookie = headers.split(';', 1)[0]
        assert request('/api/auth')[0]['authenticated']
        stop()
        thread, unknown, accepted = (str(uuid.uuid4()) for _ in range(3))
        receipts = {identifier: {'threadId': thread, 'kind': 'send', 'status': status,
                                 'fingerprint': 'fixture-fingerprint', 'at': 1}
                    for identifier, status in ((unknown, 'unknown'), (accepted, 'accepted'))}
        (data/'operations.json').write_text(json.dumps(receipts), encoding='utf-8')
        config['protocol'] = 4
        start(args.worker)
        assert request('/api/auth')[0]['authenticated'], 'Legacy Android cookie was lost'
        migrated = json.loads((data/'submissions.json').read_text(encoding='utf-8'))
        assert migrated[thread+':'+unknown]['status'] == 'unknown'
        assert migrated[thread+':'+accepted]['status'] == 'accepted'
        assert json.loads((data/'operations.json').read_text(encoding='utf-8')) == receipts
        stop(); start(args.worker)
        assert request('/api/auth')[0]['authenticated']
        assert len(json.loads((data/'submissions.json').read_text(encoding='utf-8'))) == 2
        assert (home/'config.toml').read_text(encoding='utf-8') == original and not (home/'auth.json').exists()
        print('PASS v3 -> v4 EXE upgrade: Android cookie, uncertain/accepted receipts, idempotent restart, unchanged native config')
    finally:
        if process and process.poll() is None:
            try:
                stop()
            finally:
                if process.poll() is None:
                    process.kill(); process.wait(timeout=5)
