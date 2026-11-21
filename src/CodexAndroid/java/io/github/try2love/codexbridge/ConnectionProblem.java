package io.github.try2love.codexbridge;

import android.webkit.WebViewClient;

/** Describes a failed main-frame load without exposing the requested URL or raw WebView diagnostics. */
final class ConnectionProblem {
    private enum Kind { CERTIFICATE, AUTHENTICATION, TIMEOUT, HTTP, NETWORK }

    private final Kind kind;
    private final int status;

    private ConnectionProblem(Kind kind, int status) {
        this.kind = kind;
        this.status = status;
    }

    static ConnectionProblem certificate() {
        return new ConnectionProblem(Kind.CERTIFICATE, 0);
    }

    static ConnectionProblem timeout() {
        return new ConnectionProblem(Kind.TIMEOUT, 0);
    }

    static ConnectionProblem fromWebError(int error) {
        switch (error) {
            case WebViewClient.ERROR_FAILED_SSL_HANDSHAKE: return certificate();
            case WebViewClient.ERROR_TIMEOUT: return timeout();
            case WebViewClient.ERROR_AUTHENTICATION:
            case WebViewClient.ERROR_PROXY_AUTHENTICATION: return new ConnectionProblem(Kind.AUTHENTICATION, 0);
            default: return new ConnectionProblem(Kind.NETWORK, 0);
        }
    }

    static ConnectionProblem fromHttpStatus(int status) {
        return new ConnectionProblem(status == 401 || status == 403 ? Kind.AUTHENTICATION : Kind.HTTP, status);
    }

    boolean canTryAlternateRoute() {
        return kind != Kind.AUTHENTICATION && (status == 0 || ConnectionDiscovery.retryableStatus(status));
    }

    String title() {
        switch (kind) {
            case CERTIFICATE: return "无法验证证书";
            case AUTHENTICATION: return "网关拒绝访问";
            case TIMEOUT: return "连接超时";
            case HTTP: return status >= 500 ? "网关服务异常" : "网关返回错误";
            default: return "无法连接电脑";
        }
    }

    String guidance() {
        switch (kind) {
            case CERTIFICATE: return "请检查网关证书是否有效、域名是否匹配，以及手机是否信任证书。";
            case AUTHENTICATION: return "请核对网关账号和反向代理的认证设置，再重新连接。";
            case TIMEOUT: return "请检查手机网络，并确认电脑上的手机服务正在运行。";
            case HTTP: return "请在电脑上检查手机服务状态和反向代理设置。";
            default: return "请确认电脑在线、手机能访问网关地址，然后重试。";
        }
    }

    int status() {
        return status;
    }
}
