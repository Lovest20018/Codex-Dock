"""Local lifecycle fixture: release listeners before a deliberately delayed process exit."""
import json
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

config = json.loads(sys.stdin.buffer.read())
directory = Path(config['dataDir'])
(directory / 'fixture-process.txt').write_text(str(__import__('os').getpid()), encoding='ascii')
stop = threading.Event()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_GET(self):
        self.reply()

    def do_POST(self):
        self.rfile.read(int(self.headers.get('Content-Length', '0')))
        self.reply()

    def reply(self):
        if self.headers.get('Authorization') != 'Bearer ' + config['controlToken']:
            self.send_error(403)
            return
        body = {'service': 'codex-dock-mobile', 'protocol': config['protocol'], 'instanceId': config['instanceId'],
                'publicUrl': config['publicUrl'], 'port': config['port'], 'checked': False}
        data = json.dumps(body if self.path == '/health' else {'ok': True}).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)
        self.wfile.flush()
        if self.path == '/stop':
            stop.set()


public = ThreadingHTTPServer(('127.0.0.1', config['port']), Handler)
control = ThreadingHTTPServer(('127.0.0.1', config['controlPort']), Handler)
threads = [threading.Thread(target=server.serve_forever, kwargs={'poll_interval': .05}, daemon=True) for server in (public, control)]
for thread in threads:
    thread.start()
stop.wait(45)
for server, thread in zip((public, control), threads):
    server.shutdown()
    server.server_close()
    thread.join()
(directory / 'fixture-listeners-closed.txt').write_text('closed', encoding='ascii')
time.sleep(1.5)
