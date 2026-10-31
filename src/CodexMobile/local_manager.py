"""Loopback-only desktop enrollment host for upstream account panels."""
import hmac
import json
import secrets
import threading
import time
from http.cookies import SimpleCookie
from pathlib import Path
from urllib.parse import urlsplit

from gateway import ControlHandler
from bridge.features.accounts.account import AccountError


class LocalManager:
    def __init__(self, workbench, directory):
        self.workbench = workbench
        self.directory = Path(directory)
        self.lock = threading.Lock()
        self.tickets = {}
        self.sessions = {}
        self.cookie_name = 'dock_manager_' + workbench.state.instance_id

    def issue(self, port):
        ticket = secrets.token_urlsafe(32)
        with self.lock:
            # Issuing a new link invalidates links that have not been opened.
            self.tickets = {ticket: time.monotonic() + 60}
        return {'url': f'http://127.0.0.1:{port}/manager/#{ticket}'}

    def unlock(self, ticket):
        if not isinstance(ticket, str) or len(ticket) > 100:
            raise PermissionError('请从 Dock 重新打开管理页面')
        with self.lock:
            expiry = self.tickets.pop(ticket, 0)
            if expiry <= time.monotonic():
                raise PermissionError('授权链接已过期，请从 Dock 重新打开')
            token = secrets.token_urlsafe(32)
            now = time.monotonic()
            self.sessions = {key: row for key, row in self.sessions.items() if row['expires'] > now}
            if len(self.sessions) >= 16:
                self.sessions.pop(next(iter(self.sessions)))
            row = {'csrf': secrets.token_urlsafe(32), 'expires': now + 1800}
            self.sessions[token] = row
        return token, row

    def session(self, cookie):
        parsed = SimpleCookie()
        try:
            parsed.load(cookie)
            token = parsed[self.cookie_name].value
        except Exception:
            return None
        with self.lock:
            row = self.sessions.get(token)
            if row and row['expires'] > time.monotonic():
                return row
            self.sessions.pop(token, None)
        return None

    def call(self, method, value):
        if not isinstance(value, dict):
            raise ValueError('管理请求格式无效')
        bridge = self.workbench.bridge
        calls = {'accounts': bridge.accounts.control, 'account': bridge.accounts.account,
                 'cliAccess': bridge.cli_access_control,
                 'clients': self.workbench.public.desktop_sessions.control}
        if method not in calls:
            raise ValueError('未知管理操作')
        if method == 'cliAccess' and value.get('provider', 'codex') != 'codex':
            raise ValueError('此网关仅提供 Codex 接入')
        try:
            return calls[method](value)
        except AccountError as error:
            # Enrollment needs the upstream actionable message (login/runtime,
            # identity or confirmation), while unknown failures stay uncertain.
            raise ValueError(str(error)) from None

    def asset(self, path):
        own = {'/manager/': ('index.html', 'text/html; charset=utf-8'),
               '/manager/app.js': ('app.js', 'text/javascript; charset=utf-8'),
               '/manager/app.css': ('app.css', 'text/css; charset=utf-8')}
        if path in own:
            relative, kind = own[path]
            return (self.directory / relative).read_bytes(), kind
        scripts = {'i18n': 'shared/i18n.js', 'account': 'features/accounts/account.js',
                   'accounts': 'features/accounts/accounts.js', 'cli-access': 'features/accounts/cli-access.js',
                   'wsl-accounts': 'features/accounts/wsl-accounts.js'}
        for name, relative in scripts.items():
            if path == '/manager/vendor/' + name + '.js':
                return (self.workbench.public.web_dir / relative).read_bytes(), 'text/javascript; charset=utf-8'
        if path == '/manager/vendor/account.css':
            return (self.workbench.public.web_dir / 'features/accounts/account.css').read_bytes(), 'text/css; charset=utf-8'
        return None


class ManagerControlHandler(ControlHandler):
    def route(self, write):
        if not self.path.startswith('/manager/'):
            return super().route(write)
        expected = f'127.0.0.1:{self.server.server_port}'
        origins = self.headers.get_all('Origin', [])
        if (self.client_address[0] != '127.0.0.1' or self.headers.get_all('Host', []) != [expected]
                or self.headers.get('X-Forwarded-For') or self.headers.get('Sec-Fetch-Site') == 'cross-site'
                or origins and origins != ['http://' + expected]
                or write and origins != ['http://' + expected]):
            raise PermissionError('只允许本机管理页面')
        manager = self.state.workbench.manager
        path = urlsplit(self.path).path
        if self.path != path:
            raise ValueError('管理地址不接受查询参数')
        if not write:
            asset = manager.asset(path)
            if asset:
                return self.manager_output(200, asset[0], content_type=asset[1])
        if write and path == '/manager/unlock':
            value = self.read_json()
            if set(value) != {'ticket'}:
                raise ValueError('授权请求格式无效')
            token, session = manager.unlock(value['ticket'])
            cookie = f'{manager.cookie_name}={token}; Path=/manager/; HttpOnly; SameSite=Strict; Max-Age=1800'
            return self.manager_output(200, {'csrf': session['csrf']}, cookie=cookie)
        session = manager.session(self.headers.get('Cookie', ''))
        if not session:
            return self.manager_output(401, {'error': '授权已过期，请从 Dock 重新打开'})
        if not write and path == '/manager/session':
            return self.manager_output(200, {'csrf': session['csrf']})
        if write and path == '/manager/api':
            if (len(self.headers.get_all('X-CSRF-Token', [])) != 1 or not hmac.compare_digest(
                    self.headers.get('X-CSRF-Token', '').encode(), session['csrf'].encode())):
                raise PermissionError('管理授权已失效，请刷新页面')
            value = self.read_json()
            if set(value) != {'method', 'value'} or not isinstance(value['method'], str):
                raise ValueError('管理请求格式无效')
            return self.manager_output(200, manager.call(value['method'], value['value']))
        return self.manager_output(404, {'error': '未知管理操作'})

    def manager_output(self, status, value, *, content_type='application/json; charset=utf-8', cookie=None):
        body = value if isinstance(value, bytes) else json.dumps(value, ensure_ascii=False).encode('utf-8')
        self.close_connection = True
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'")
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'no-referrer')
        self.send_header('Connection', 'close')
        if cookie:
            self.send_header('Set-Cookie', cookie)
        self.end_headers()
        self.wfile.write(body)
