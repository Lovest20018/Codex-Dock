"""Dock lifecycle adapter for the pinned upstream Codex workbench."""
import html
from pathlib import Path
from urllib.parse import urlsplit

from bridge.api.httpd import GatewayServer, Handler
from bridge.clients.manager import DesktopSessions
from bridge.features.notifications.channels import Notifications
from bridge.features.notifications.addresses import AddressNotifications
from bridge.features.network.discovery import ConnectionDiscovery
from bridge.features.accounts.cli_access import CliAccess
from bridge.features.accounts.wsl_accounts import WslAccounts
from gateway import State
from local_manager import LocalManager
from bridge.api.assets import served_asset_bytes


class CodexSessions(DesktopSessions):
    """Reuse native Codex/WSL management while excluding other providers."""

    def __init__(self, directory, bridge=None, *, gateway_running=False):
        super().__init__(directory, bridge, gateway_running=gateway_running, providers=('codex',))

    @staticmethod
    def codex(provider):
        return provider == 'codex' or isinstance(provider, str) and provider.startswith('wsl:')

    def enabled(self, provider):
        return self.codex(provider) and super().enabled(provider)

    def navigation_clients(self):
        return [provider for provider in super().navigation_clients() if self.codex(provider)]

    def clients(self, refresh=False):
        result = super().clients(refresh)
        return {**result, 'clients': [row for row in result['clients'] if self.codex(row['id'])]}

    def clients_status(self):
        result = super().clients_status()
        return {**result, 'clients': [row for row in result['clients'] if self.codex(row['id'])]}

    def toggle_client(self, value):
        if not self.codex(value.get('provider')):
            raise ValueError('此网关仅提供 Codex 接入')
        return super().toggle_client(value)

    def scan(self, setup=True, *, scan_wsl=True):
        # Discovery must not configure an unrelated Claude/DSH installation.
        if scan_wsl and self.bridge.entries is not None:
            self.bridge.entries.scan()
        self.client_cache = None
        return self.clients(refresh=True)

    def scan_wsl(self):
        if self.bridge.entries is None:
            raise ValueError('当前电脑不支持 WSL 接入')
        result = self.bridge.entries.scan()
        self.client_cache = None
        return {**self.clients(refresh=True), 'errors': result.get('errors', [])}

    def control(self, value):
        if value.get('action', 'status') not in ('status', 'clients', 'scan', 'scan-wsl', 'toggle-client'):
            raise ValueError('此网关仅提供 Codex 接入')
        if value.get('action', 'status') == 'status':
            return {'backends': self.clients_status()}
        return super().control(value)


class CodexHandler(Handler):
    def static_output(self, path, content_type):
        # Dock presentation shares the existing public URLs and upstream order.
        # output() retains request checks, CSP, compression and no-store headers.
        extra = {'/app.js': 'dock-ui.js', '/style.css': 'dock-ui.css'}.get(path)
        if extra:
            data = served_asset_bytes(self.server.web_dir, path)
            data += b'\n' + (Path(__file__).resolve().parent / 'ui' / extra).read_bytes()
            return self.output(200, data, content_type)
        return super().static_output(path, content_type)

    def _handle_method(self, write):
        path = urlsplit(self.path).path
        if (not write and self.path == '/' and self.client_address[0] == '127.0.0.1'
                and self.headers.get_all('Host', []) == [f'127.0.0.1:{self.server.server_port}']
                and not self.headers.get('Origin') and self.headers.get('Sec-Fetch-Site') != 'cross-site'):
            entry = html.escape(next(iter(self.server.origins)), quote=True)
            page = ('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>Codex Dock · 手机连接</title>'
                    '<h1>手机服务已运行</h1><p>请通过配置的 HTTPS 域名登录：</p>'
                    f'<p><a href="{entry}">{entry}</a></p>'
                    '<p>若域名返回 502，请检查反向代理目标、路由端口映射和电脑防火墙。</p>'
                    '<p>本机 HTTP 地址仅用于诊断，不提供登录。</p></html>').encode('utf-8')
            return self.output(200, page, 'text/html; charset=utf-8')
        if (path == '/harness' or path.startswith('/harness/')
                or path.startswith('/api/desktop-sessions/')
                or path.startswith('/api/clients/claude') or path == '/api/harness'):
            self.check_request(write)
            if not self.authorized(write):
                return
            return self.output(404, {'error': '此网关仅提供 Codex 接入'})
        return super()._handle_method(write)


class CodexCliAccess(CliAccess):
    def public(self):
        return {'codex': super().public()['codex']}

    def change(self, value):
        if not isinstance(value, dict) or value.get('provider') != 'codex':
            raise ValueError('此网关仅提供 Codex 接入')
        return super().change(value)


class CodexWslAccounts(WslAccounts):
    def _registry(self, provider):
        if provider != 'codex':
            raise ValueError('此网关仅提供 Codex 接入')
        return super()._registry(provider)


class Workbench:
    def __init__(self, config, bridge, web_dir):
        self.bridge = bridge
        self.public = self.notifications = self.address_notifications = None
        self.closed = False
        try:
            self.open(config, bridge, web_dir)
        except Exception:
            self.close()
            raise

    def open(self, config, bridge, web_dir):
        self.state = State(config, bridge, web_dir)
        directory = Path(config['dataDir'])
        options = {'auth': config['auth'], 'origins': [self.state.origin],
                   'publicUrl': self.state.origin, 'localAccess': False}
        bridge.cli_access.close()
        bridge.cli_access = CodexCliAccess(bridge)
        if bridge.wsl_accounts:
            bridge.wsl_accounts.close()
            bridge.wsl_accounts = CodexWslAccounts(bridge)
        self.public = GatewayServer(('0.0.0.0', config['port']), bridge, options,
                                    web_dir, directory, desktop_sessions_factory=CodexSessions)
        self.public.RequestHandlerClass = CodexHandler
        self.public.auth = self.state.auth
        self.public.pairing = self.state.pairing
        self.public.instance_id = config['instanceId']
        self.public.connection_discovery = ConnectionDiscovery(directory, self.public)
        self.notifications = Notifications(bridge, directory, lambda: self.public.origins,
                                           lambda: self.state.origin, desktop_sessions=self.public.desktop_sessions)
        self.public.notifications = self.notifications
        self.notifications.mobile_push.session_valid = self.valid_push_session
        self.address_notifications = AddressNotifications(directory,
            lambda: [self.state.origin] if self.state.checked else [], config['instanceId'])
        self.state.workbench = self
        self.manager = LocalManager(self, Path(__file__).resolve().parent / 'manager')

    def valid_push_session(self, key):
        auth = self.state.auth
        with auth.lock:
            row = auth.sessions.get(key)
            return bool(row and auth.valid(row) and auth.permitted(row['ip']))

    def start(self):
        self.notifications.start()
        self.address_notifications.start()
        # Client launch/recovery remains an explicit action in the workbench.
        self.bridge.accounts.monitor.start()

    def close(self):
        if self.closed:
            return
        self.closed = True
        if self.address_notifications:
            self.address_notifications.close()
        if self.notifications:
            self.notifications.close()
        self.bridge.accounts.monitor.close()
        if self.public:
            if self.public.harness:
                self.public.harness.close()
            if self.public.desktop_sessions:
                self.public.desktop_sessions.close()
            self.public.server_close()
