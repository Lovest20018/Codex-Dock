"""Codex-only HTTPS reverse-proxy gateway and separate authenticated local control."""
import hmac
import json
import re
import socket
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

from bridge.features.auth.auth import Auth, LoginRejected, canonical_ip
from bridge.features.auth.pairing import Pairing
from bridge.features.auth.tls import client_context
from bridge.features.sessions.store import StoreUnavailable
from bridge.clients.codex.ipc import IPCError
from qr_png import pairing_image

PROTOCOL = 4



def public_origin(value):
    if not isinstance(value, str) or len(value) > 2048 or any(ord(char) <= 32 for char in value):
        raise ValueError('请输入 HTTPS 根域名')
    url = urlsplit(value)
    if (url.scheme != 'https' or not url.hostname or url.username or url.password or url.query or url.fragment
            or url.path not in ('', '/') or not re.fullmatch(r'[A-Za-z0-9.-]+', url.hostname)
            or url.hostname.lower() in ('localhost',) or url.hostname.lower().endswith('.localhost')
            or len(url.hostname) > 253 or any(not re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?', label) for label in url.hostname.split('.'))):
        raise ValueError('请输入 HTTPS 根域名，不支持路径、凭据、参数或片段')
    import ipaddress
    try:
        ipaddress.ip_address(url.hostname)
    except ValueError:
        pass
    else:
        raise ValueError('请使用 HTTPS 域名')
    if url.port is not None and not 1 <= url.port <= 65535:
        raise ValueError('HTTPS 端口无效')
    host = url.hostname.lower()
    # Match browser Origin serialization, which omits the default port.
    return 'https://' + host + (':' + str(url.port) if url.port not in (None, 443) else '')


class Server(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True
    request_queue_size = 32

    def __init__(self, address, handler, state):
        self.state = state
        self.slots = threading.BoundedSemaphore(24)
        super().__init__(address, handler)

    def server_bind(self):
        # Never resolve hostnames as part of binding a local listener.
        socket.socket.bind(self.socket, self.server_address)
        self.server_address = self.socket.getsockname()
        self.server_name, self.server_port = self.server_address[:2]

    def process_request(self, request, client_address):
        if not self.slots.acquire(blocking=False):
            request.close()
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.slots.release()

    def handle_error(self, request, client_address):
        pass  # Never log URLs, credentials, IDs, or conversation data.

    def shutdown_request(self, request):
        # Windows can reset a rejected POST with an unread body before the
        # browser receives its error. Half-close first, then drain a bounded tail.
        try:
            request.shutdown(socket.SHUT_WR)
            deadline, remaining = time.monotonic() + .2, 1024 * 1024
            while remaining and time.monotonic() < deadline:
                request.settimeout(max(.001, deadline - time.monotonic()))
                data = request.recv(min(65536, remaining))
                if not data:
                    break
                remaining -= len(data)
        except OSError:
            pass
        finally:
            super().shutdown_request(request)


class DockAuth(Auth):
    def client(self, peer, headers, secure=False):
        # Dock accepts arbitrary external reverse proxies. No proxy is configured
        # as an identity authority, including an HTTP forward on loopback.
        address = canonical_ip(peer)
        return {'ip': address, 'peer': address, 'source': 'proxy' if secure else 'direct'}


class State:
    def __init__(self, config, bridge, web_dir):
        self.config = config
        self.origin = public_origin(config['publicUrl'])
        self.host = urlsplit(self.origin).netloc
        self.instance_id = config['instanceId']
        self.bridge = bridge
        self.auth = DockAuth(config['auth'], config['dataDir'])
        # Remote access needs no proxy-IP allowlist. Without an explicitly trusted
        # forwarding peer, upstream Auth uses its socket address for rate limits
        # and device display, never arbitrary remote X-Forwarded-For values.
        self.auth.policy['trustedProxies'] = []
        self.pairing = Pairing(self.auth, {self.origin})
        self.web_dir = Path(web_dir)
        self.stop = threading.Event()
        self.checked = False

    def check_entry(self):
        # No ambient proxy, cookies, credentials, or redirects. TLS validation
        # stays enabled; a domain must prove it reaches this worker instance.
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(),
                                            urllib.request.HTTPSHandler(context=client_context()))
        try:
            with opener.open(urllib.request.Request(self.origin + '/api/auth', headers={'Accept': 'application/json'}), timeout=8) as response:
                data = response.read(65537)
                if response.status != 200 or len(data) > 65536:
                    raise ValueError()
                value = json.loads(data)
                if (not isinstance(value, dict) or value.get('instanceId') != self.instance_id
                        or value.get('passwordless') is not False or type(value.get('authenticated')) is not bool):
                    raise ValueError()
        except urllib.error.HTTPError as error:
            self.checked = False
            if error.code == 502:
                raise ValueError('HTTPS 入口返回 502：反向代理未能连接电脑。请检查目标 IP、端口、路由映射及防火墙。') from None
            if error.code == 403:
                raise ValueError('HTTPS 入口返回 403：请保留配置域名的完整 Host（含外部端口），并检查 Origin 转发。') from None
            raise ValueError(f'HTTPS 入口返回 {error.code}，请检查反向代理；不接受跳转。') from None
        except (OSError, ValueError, urllib.error.URLError):
            self.checked = False
            raise ValueError('HTTPS 入口检测失败，请检查证书、反向代理及 Host 转发；不接受跳转。') from None
        self.checked = True
        return {'ok': True, 'instanceId': self.instance_id, 'url': self.origin}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


class BaseHandler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    server_version = 'CodexDockMobile/1'
    sys_version = ''

    def setup(self):
        super().setup()
        self.connection.settimeout(15)

    @property
    def state(self):
        return self.server.state

    def log_message(self, format, *args):
        pass

    def output(self, status, value, *, cookie=None, content_type=None):
        body = value if isinstance(value, bytes) else json.dumps(value, ensure_ascii=False, allow_nan=False).encode('utf-8')
        self.close_connection = True
        self.send_response(status)
        self.send_header('Content-Type', content_type or 'application/json; charset=utf-8')
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'no-referrer')
        self.send_header('Content-Security-Policy', "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; frame-ancestors 'none'; form-action 'self'")
        self.send_header('Connection', 'close')
        if cookie is not None:
            self.send_header('Set-Cookie', cookie)
        self.end_headers()
        self.wfile.write(body)

    def read_json(self):
        lengths = self.headers.get_all('Content-Length', [])
        if ('Transfer-Encoding' in self.headers or len(lengths) != 1 or not lengths[0].isascii()
                or not lengths[0].isdigit() or not 0 < int(lengths[0]) <= 512000
                or self.headers.get_content_type() != 'application/json'):
            raise ValueError('请求内容格式不正确')
        raw = self.rfile.read(int(lengths[0]))
        if len(raw) != int(lengths[0]):
            raise ValueError('请求内容不完整')
        def reject_constant(value):
            raise ValueError('无效 JSON 数值')
        value = json.loads(raw, parse_constant=reject_constant)
        if not isinstance(value, dict):
            raise ValueError('请求内容必须为对象')
        return value

    def handle_call(self, write):
        try:
            if len(self.path) > 4096 or not self.path.startswith('/') or self.path.startswith('//'):
                raise ValueError('请求地址无效')
            self.route(write)
        except LoginRejected as error:
            self.output(401, {'error': str(error), 'loginStatus': error.status})
        except PermissionError:
            self.output(403, {'error': '请求来源或登录验证无效'})
        except (ValueError, TypeError, KeyError, StoreUnavailable, IPCError) as error:
            # Known application messages only; no serialized payloads or stack traces.
            if isinstance(error, StoreUnavailable):
                message = '暂时无法读取 Codex 历史，请在电脑打开 Codex App。'
            elif isinstance(error, KeyError):
                message = '找不到这个本机 Codex App 聊天'
            elif isinstance(error, (json.JSONDecodeError, TypeError)):
                message = '请求内容格式不正确'
            else:
                message = str(error)[:250]
            self.output(400, {'error': message})
        except (BrokenPipeError, ConnectionResetError, TimeoutError):
            self.close_connection = True
        except Exception:
            self.output(503, {'error': '操作未能确认，请刷新后查看结果；请勿自动重复提交。'})

    def do_GET(self):
        if 'Transfer-Encoding' in self.headers or any(value != '0' for value in self.headers.get_all('Content-Length', [])):
            return self.output(400, {'error': 'GET 请求体无效'})
        self.handle_call(False)

    def do_POST(self):
        self.handle_call(True)


class ControlHandler(BaseHandler):
    def route(self, write):
        if (self.client_address[0] != '127.0.0.1' or self.headers.get_all('Host', []) != [f'127.0.0.1:{self.server.server_port}']
                or self.headers.get_all('Origin', []) or self.headers.get('Sec-Fetch-Site')
                or self.headers.get('X-Forwarded-For') or len(self.headers.get_all('Authorization', [])) != 1
                or not hmac.compare_digest(self.headers.get('Authorization', '').encode(), ('Bearer ' + self.state.config['controlToken']).encode())):
            raise PermissionError()
        if not write and self.path == '/health':
            return self.output(200, {'service': 'codex-dock-mobile', 'protocol': PROTOCOL, 'instanceId': self.state.instance_id,
                                     'publicUrl': self.state.origin, 'port': self.state.config['port'], 'checked': self.state.checked})
        if write and self.path == '/check':
            if self.read_json():
                raise ValueError('入口检测不接受额外字段')
            return self.output(200, self.state.check_entry())
        if write and self.path == '/pair':
            body = self.read_json()
            if body.get('action') == 'create':
                if set(body) != {'action'}:
                    raise ValueError('配对签发不接受额外字段')
                self.state.check_entry()
                body = {'action': 'create', 'url': self.state.origin}
            elif body.get('action') not in ('revoke', 'status') or set(body) != {'action', 'id'}:
                raise ValueError('配对管理格式不正确')
            grant = self.state.pairing.control(body)
            if body['action'] == 'create':
                grant['image'] = pairing_image(grant.pop('url'))
            return self.output(200, grant)
        if write and self.path == '/devices':
            body = self.read_json()
            if not ((body == {'action': 'list'}) or set(body) == {'action', 'id'} and body['action'] in ('revoke', 'block')
                    or set(body) == {'action', 'ip'} and body['action'] == 'unblock'):
                raise ValueError('设备管理格式不正确')
            return self.output(200, self.state.auth.manage(body))
        if write and self.path == '/manager':
            if self.read_json():
                raise ValueError('打开管理页面不接受额外字段')
            return self.output(200, self.state.workbench.manager.issue(self.server.server_port))
        if write and self.path == '/stop':
            if self.read_json():
                raise ValueError('停止请求不接受额外字段')
            self.output(200, {'ok': True})
            self.state.stop.set()
            return
        return self.output(404, {'error': '未知本机管理操作'})
