package io.github.try2love.codexbridge;
import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.webkit.*;
import android.widget.*;
import io.github.lovest20018.codexdock.R;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

final class ChatHost {
    private final MainActivity activity;

    ChatHost(MainActivity activity) { this.activity = activity; }

    private GatewayMenu gatewayPopup;

    WebView web;

    String origin = "";

    private Future<?> connectionTask;

    private Runnable connectionDeadline;

    private boolean connectionReady;

    boolean connectionFailed;

    private ConnectionProblem connectionProblem;

    private String failedUrl;

    private ScrollView connectionErrorPanel;

    private int credentialAttempt;

    private ConnectionDiscovery routeDiscovery;

    private boolean routeAttempted;

    private String pendingStorage;

    private ValueCallback<Uri[]> files;

    private WebView fileView;

    private int fileGeneration;

    int generation;

    private int navigationRequest;

    private boolean authLossAttempted;

    ImageButton chatMenu;

    FrameLayout webViewport;

    private boolean shellReady;

    boolean chatDetail;

    boolean webModal;

    boolean gatewayAuthenticated;

    private int switchRequest;

    void syncWebTheme() {
        if (web != null && GatewayURL.sameOrigin(web.getUrl(), origin))
            web.evaluateJavascript("window.BridgeNativeTheme={theme:"
                            + JSONObject.quote(SettingsUi.theme(activity)) + ",dark:" + activity.ui.dark
                            + "};document.dispatchEvent(new Event(\"bridge-native-theme\"));",
                    null);
    }

    void showChat() {
        activity.selectedPage = "chat";
        activity.hideNative();
        activity.computerList.stopComputerChecks();
        if (webViewport != null)
            webViewport.setVisibility(View.VISIBLE);
        if (web == null) {
            activity.nativeHost.setVisibility(View.VISIBLE);
            LinearLayout empty = activity.column();
            empty.setPadding(activity.dp(16), activity.dp(24), activity.dp(16), activity.dp(16));
            activity.add(activity.text(activity.L("继续你的聊天"), 24), empty, 8);
            activity.add(activity.text(activity.L("连接电脑，查看任务进度和结果。"), 16), empty, 20);
            activity.add(
                    activity.button(activity.L("连接电脑"), () -> activity.selectPage("computers")), empty, 8);
            activity.nativeHost.addView(empty);
        }
        activity.header(web != null && chatDetail);
        activity.renderTabs();
        if (connectionFailed)
            renderConnectionError();
        else if (web != null) {
            web.onResume();
            web.requestLayout();
            web.evaluateJavascript("window.dispatchEvent(new Event('focus'))", null);
        }
    }

    void webSettings(String page) {
        if (web == null) {
            activity.message(activity.L("请先连接并登录电脑"));
            return;
        }
        showChat();
        web.evaluateJavascript("window.LovestShell?.openSettings(" + JSONObject.quote(page) + ")", null);
    }

    private String pendingComputerAction;

    private String pendingComputerOrigin;

    void openComputerAction(String address, String action) {
        pendingComputerAction = action;
        pendingComputerOrigin = address;
        if (address.equals(origin) && web != null && !connectionFailed) {
            showChat();
            if (gatewayAuthenticated)
                runComputerAction();
        } else
            connect(activity.computerList.resumeUrl(address));
    }

    void runComputerAction() {
        if (pendingComputerAction == null || !gatewayAuthenticated || !"chat".equals(activity.selectedPage)
                || !origin.equals(pendingComputerOrigin))
            return;
        String action = pendingComputerAction;
        pendingComputerAction = null;
        if ("accounts".equals(action))
            webAccounts();
        else
            webAction("#open-session-diagnostics");
    }

    void webAction(String selector) {
        if (web != null && GatewayURL.sameOrigin(web.getUrl(), origin))
            web.evaluateJavascript("document.querySelector(" + JSONObject.quote(selector) + ")?.click()", null);
    }

    void rememberConnection() {
        final String address = origin;
        final int ticket = generation;
        final String snapshot = CookieManager.getInstance().getCookie(address + "/");
        activity.worker.execute(() -> {
            try {
                JSONObject auth = EventClient.request(address, "/api/auth", null, null);
                String name = auth.optString("cookieName", "codex_mobile_session");
                String binding =
                        BindingSelection.cookie(CookieManager.getInstance().getCookie(address + "/"), name);
                JSONObject data = ConnectionDiscovery.descriptor(auth, binding);
                activity.runOnUiThread(() -> {
                    String current = BindingSelection.match(
                            snapshot, CookieManager.getInstance().getCookie(address + "/"), name);
                    if (ticket != generation || !activity.computerList.saved().contains(address)
                            || current.isEmpty() || !binding.equals(current))
                        return;
                    if (auth.optBoolean("authenticated") && auth.optBoolean("trustedDevice")
                            && DeviceBinding.name(name))
                        try {
                            DeviceBinding.save(activity, address, name, EventClient.tokenValue(current));
                        } catch (Exception ignored) {
                        }
                    else if (!auth.optBoolean("authenticated"))
                        DeviceBinding.clear(activity, address);
                    activity.computers.rememberConnection(address, data == null ? null : data.toString(),
                            auth.optBoolean("authenticated") ? name : null);
                });
            } catch (Exception ignored) {
            }
        });
    }

    void loadWithRecovery(String address, String url, WebView current, int ticket) {
        if (ticket != generation || web != current || !"chat".equals(activity.selectedPage)
                || !address.equals(origin) || !activity.computerList.saved().contains(address))
            return;
        final String before =
                EventClient.sessionCookie(CookieManager.getInstance().getCookie(address + "/"), address);
        cancelCredentialRecovery();
        final int attempt = credentialAttempt;
        final DeviceBinding.Credential saved = DeviceBinding.load(activity, address);
        if (saved == null || saved.cookie().equals(before)) {
            current.loadUrl(url);
            return;
        }
        connectionTask = activity.worker.submit(() -> {
            if (Thread.currentThread().isInterrupted())
                return;
            Boolean valid = null;
            if (saved != null && !saved.cookie().equals(before)) {
                Boolean existing = before.isEmpty()
                        ? false
                        : DeviceBinding.verify(address,
                                new DeviceBinding.Credential(before.substring(0, before.indexOf('=')),
                                        before.substring(before.indexOf('=') + 1)));
                if (Boolean.FALSE.equals(existing)) {
                    valid = DeviceBinding.verify(address, saved);
                }
            }
            final Boolean verified = valid;
            activity.runOnUiThread(() -> {
                if (ticket != generation || attempt != credentialAttempt || web != current
                        || !address.equals(origin) || !activity.computerList.saved().contains(address))
                    return;
                connectionTask = null;
                DeviceBinding.Credential latest = DeviceBinding.load(activity, address);
                boolean sameBinding = latest != null && latest.cookie().equals(saved.cookie());
                if (Boolean.FALSE.equals(verified) && sameBinding)
                    DeviceBinding.clear(activity, address);
                if (!Boolean.TRUE.equals(verified) || !sameBinding
                        || !before.equals(EventClient.sessionCookie(
                                CookieManager.getInstance().getCookie(address + "/"), address))) {
                    current.loadUrl(url);
                    return;
                }
                EventClient.setName(address, saved.name);
                CookieManager.getInstance().setCookie(address + "/",
                        saved.cookie() + "; Path=" + GatewayURL.cookiePath(address)
                                + "; HttpOnly; SameSite=Strict; Max-Age=34560000"
                                + (address.startsWith("https://") ? "; Secure" : ""),
                        ok -> {
                            DeviceBinding.Credential retained = DeviceBinding.load(activity, address);
                            if (!activity.computerList.saved().contains(address) || retained == null
                                    || !retained.cookie().equals(saved.cookie())) {
                                if (ok
                                        && saved.cookie().equals(EventClient.sessionCookie(
                                                CookieManager.getInstance().getCookie(address + "/"), address)))
                                    CookieManager.getInstance().setCookie(address + "/",
                                            saved.name + "=; Path=" + GatewayURL.cookiePath(address)
                                                    + "; Max-Age=0");
                                return;
                            }
                            if (ticket != generation || attempt != credentialAttempt || web != current
                                    || !address.equals(origin))
                                return;
                            if (ok) {
                                activity.computers.setCookieName(address, saved.name);
                                CookieManager.getInstance().flush();
                                MonitorService.bindingsChanged();
                            }
                            current.loadUrl(url);
                        });
            });
        });
    }

    void recoverConnection(String requested) {
        if (routeAttempted || !activity.resumed || web == null || !"chat".equals(activity.selectedPage))
            return;
        routeAttempted = true;
        final String address = origin, encoded = activity.computers.connection(address),
                     cookie = EventClient.sessionCookie(
                             CookieManager.getInstance().getCookie(address + "/"), address);
        final int ticket = generation;
        if (encoded == null || cookie.isEmpty())
            return;
        final ConnectionDiscovery resolver = new ConnectionDiscovery();
        routeDiscovery = resolver;
        activity.worker.execute(() -> {
            try {
                String next = resolver.resolve(address, new JSONObject(encoded), cookie);
                activity.runOnUiThread(() -> {
                    if (ticket != generation || !activity.resumed || routeDiscovery != resolver
                            || next.equals(address) || !activity.computerList.saved().contains(address)
                            || !cookie.equals(EventClient.sessionCookie(
                                    CookieManager.getInstance().getCookie(address + "/"), address)))
                        return;
                    final String[] snapshot = {null};
                    Runnable relocate = () -> {
                        String existing =
                                EventClient.sessionCookie(CookieManager.getInstance().getCookie(next + "/"), next);
                        if (!existing.isEmpty() && !existing.equals(cookie))
                            return;
                        if (ticket != generation || !activity.resumed || routeDiscovery != resolver
                                || !activity.computerList.saved().contains(address)
                                || !cookie.equals(EventClient.sessionCookie(
                                        CookieManager.getInstance().getCookie(address + "/"), address)))
                            return;
                        CookieManager.getInstance().setCookie(next + "/",
                                cookie + "; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=34560000", ok -> {
                                    if (!ok)
                                        return;
                                    if (ticket != generation || !activity.resumed || routeDiscovery != resolver
                                            || !activity.computerList.saved().contains(address)
                                            || !cookie.equals(EventClient.sessionCookie(
                                                    CookieManager.getInstance().getCookie(address + "/"),
                                                    address))) {
                                        String name = cookie.substring(0, cookie.indexOf('='));
                                        if (existing.isEmpty() && !activity.computerList.saved().contains(next)
                                                && cookie.equals(BindingSelection.cookie(
                                                        CookieManager.getInstance().getCookie(next + "/"),
                                                        name))) {
                                            CookieManager.getInstance().setCookie(
                                                    next + "/", name + "=; Path=/; Secure; Max-Age=0");
                                        }
                                        return;
                                    }
                                    activity.computers.migrate(address, next, encoded);
                                    activity.computerList.forgetReachability(address);
                                    DeviceBinding.Credential backup = DeviceBinding.load(activity, address);
                                    if (backup != null && backup.cookie().equals(cookie)
                                            && DeviceBinding.load(activity, next) == null)
                                        try {
                                            DeviceBinding.save(activity, next, backup.name, backup.token);
                                            DeviceBinding.clear(activity, address);
                                        } catch (Exception ignored) {
                                        }
                                    EventClient.setName(next, EventClient.cookieName(address));
                                    EventClient.forget(address);
                                    connectNow(next + requested.substring(address.length()));
                                    pendingStorage = snapshot[0];
                                    routeAttempted = true;
                                });
                    };
                    if (web != null && GatewayURL.sameOrigin(web.getUrl(), address))
                        web.evaluateJavascript(
                                "(()=>{if(typeof saveDraft==='function')saveDraft();return JSON.stringify(Object.fromEntries(Object.entries(localStorage)));})()",
                                value -> {
                                    if (ticket != generation)
                                        return;
                                    try {
                                        Object parsed = new org.json.JSONTokener(value).nextValue();
                                        if (parsed instanceof String && ((String) parsed).length() < 524288) {
                                            JSONObject data = new JSONObject((String) parsed);
                                            snapshot[0] = data.toString();
                                        }
                                    } catch (Exception ignored) {
                                    }
                                    relocate.run();
                                });
                    else
                        relocate.run();
                });
            } catch (Exception ignored) {
            }
        });
    }

    void webAccounts() {
        if (web != null && GatewayURL.sameOrigin(web.getUrl(), origin))
            web.evaluateJavascript(
                    "(()=>{if(typeof window.BridgeNavigation?.accounts==='function')return window.BridgeNavigation.accounts();document.getElementById('accounts-button')?.click();return true;})()",
                    null);
    }

    void dismissGatewayMenu() {
        GatewayMenu popup = gatewayPopup;
        gatewayPopup = null;
        if (popup != null)
            popup.dismiss();
    }

    void gatewayMenu(View anchor) {
        if (web == null || !anchor.isShown() || !anchor.isEnabled())
            return;
        dismissGatewayMenu();
        final WebView current = web;
        final String address = origin;
        final int ticket = generation;
        GatewayMenu menu = new GatewayMenu(activity.ui.context);
        gatewayPopup = menu;
        menu.setOnDismissListener(() -> {
            if (gatewayPopup == menu)
                gatewayPopup = null;
        });
        menu.add(activity.L("返回电脑列表"), "ic_menu_monitor", activity.computerList::home);
        menu.separator();
        if (!shellReady)
            menu.add(activity.L("账号与接入"), "ic_menu_user_round", this::webAccounts);
        menu.add(activity.L("通知收件箱"), "ic_menu_bell", activity.notifications::inbox);
        menu.separator();
        if (!shellReady)
            menu.add(activity.L("外观与显示"), "ic_menu_palette", () -> webAction("[data-open-appearance]"));
        menu.add(activity.L("我的"), "ic_menu_settings", () -> activity.selectPage("me"));
        menu.separator();
        menu.add(activity.L("刷新页面"), "ic_menu_refresh_cw", () -> {
            if (web == current && generation == ticket) {
                beforeLeave(() -> {
                    routeAttempted = false;
                    current.reload();
                });
            }
        });
        current.evaluateJavascript(
                "(()=>{const style=getComputedStyle(document.documentElement),color=name=>style.getPropertyValue(name).trim();return {surface:color('--raised'),text:color('--text'),muted:color('--muted'),line:color('--line'),pressed:color('--surface')};})()",
                value -> {
                    if (web != current || generation != ticket || gatewayPopup != menu || !anchor.isShown()
                            || !anchor.isEnabled() || !GatewayURL.sameOrigin(current.getUrl(), address))
                        return;
                    menu.palette(value);
                    if (!menu.isShowing())
                        menu.show(anchor);
                });
        anchor.postDelayed(() -> {
            if (web == current && generation == ticket && gatewayPopup == menu && anchor.isShown()
                    && anchor.isEnabled() && !menu.isShowing())
                menu.show(anchor);
        }, 120);
    }

    void cancelConnectionDeadline() {
        if (connectionDeadline != null) {
            activity.homeHandler.removeCallbacks(connectionDeadline);
            connectionDeadline = null;
        }
    }

    void cancelCredentialRecovery() {
        credentialAttempt++;
        if (connectionTask != null) {
            connectionTask.cancel(true);
            connectionTask = null;
        }
    }

    void cancelRouteRecovery() {
        if (routeDiscovery != null) {
            routeDiscovery.cancel();
            routeDiscovery = null;
        }
        routeAttempted = false;
    }

    void leaveConnectionPage() {
        switchRequest++;
        navigationRequest++;
        pendingComputerAction = null;
        if (web != null && !connectionReady && !connectionFailed)
            clear();
        else {
            cancelConnectionDeadline();
            cancelCredentialRecovery();
            cancelRouteRecovery();
        }
    }

    void connectionError(String url, WebView current, int ticket, ConnectionProblem problem) {
        if (ticket != generation || web != current || connectionFailed || !"chat".equals(activity.selectedPage))
            return;
        cancelConnectionDeadline();
        connectionFailed = true;
        connectionProblem = problem;
        failedUrl = url;
        activity.status.setVisibility(View.GONE);
        dismissGatewayMenu();
        chatDetail = false;
        webModal = false;
        gatewayAuthenticated = false;
        activity.header(false);
        activity.renderTabs();
        renderConnectionError();
        if (problem.canTryAlternateRoute())
            recoverConnection(url);
    }

    private void renderConnectionError() {
        if (webViewport == null || web == null || connectionProblem == null || failedUrl == null)
            return;
        if (connectionErrorPanel != null)
            webViewport.removeView(connectionErrorPanel);
        final String retryUrl = failedUrl;
        final WebView current = web;
        final int ticket = generation;
        ScrollView panel = new ScrollView(activity.ui.context);
        connectionErrorPanel = panel;
        panel.setFillViewport(true);
        panel.setBackgroundColor(activity.pageColor());
        LinearLayout content = activity.column();
        content.setPadding(activity.dp(16), activity.dp(20), activity.dp(16), activity.dp(16));
        LinearLayout errorCard = activity.card();
        String reason = activity.L(connectionProblem.title())
                + (connectionProblem.status() == 0 ? "" : " · HTTP " + connectionProblem.status());
        TextView title = activity.text(reason, 20);
        title.setTypeface(null, android.graphics.Typeface.BOLD);
        if (Build.VERSION.SDK_INT >= 28)
            title.setAccessibilityHeading(true);
        title.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        activity.add(title, errorCard, 6);
        TextView target = activity.text(activity.computerList.computerName(origin) + "\n" + origin, 14);
        target.setTextColor(activity.mutedColor());
        activity.add(target, errorCard, 6);
        activity.add(activity.text(activity.L(connectionProblem.guidance()), 16), errorCard, 12);
        Button retry = activity.button(activity.L("重试连接"), () -> {
            if (web == current && generation == ticket && connectionFailed)
                beforeLeave(() -> connectNow(retryUrl));
        });
        retry.setTextColor(activity.ui.accent);
        activity.add(retry, errorCard, 0);
        activity.add(errorCard, content, 0);
        panel.addView(content, new ScrollView.LayoutParams(-1, -2));
        current.setVisibility(View.INVISIBLE);
        webViewport.addView(panel, new FrameLayout.LayoutParams(-1, -1));
    }

    private void resetConnectionError() {
        if (connectionErrorPanel != null && webViewport != null)
            webViewport.removeView(connectionErrorPanel);
        connectionErrorPanel = null;
        connectionProblem = null;
        failedUrl = null;
        connectionFailed = false;
        if (web != null)
            web.setVisibility(View.VISIBLE);
    }

    void watchConnection(String url, WebView current, int ticket) {
        cancelConnectionDeadline();
        connectionDeadline = () -> connectionError(url, current, ticket, ConnectionProblem.timeout());
        activity.homeHandler.postDelayed(connectionDeadline, 12000);
    }

    void clear() {
        dismissGatewayMenu();
        cancelConnectionDeadline();
        cancelCredentialRecovery();
        cancelRouteRecovery();
        pendingStorage = null;
        if (files != null) {
            files.onReceiveValue(null);
            files = null;
            fileView = null;
        }
        activity.computerList.clearRows();
        activity.resetExitGesture();
        generation++;
        resetConnectionError();
        if (web != null) {
            web.stopLoading();
            web.destroy();
            web = null;
        }
        activity.updateBars(activity.pageColor(), activity.pageColor());
        if (activity.body != null && webViewport != null)
            activity.body.removeView(webViewport);
        webViewport = null;
        shellReady = false;
        chatDetail = false;
        webModal = false;
        gatewayAuthenticated = false;
        connectionReady = false;
    }

    boolean previewShell(String target) {
        return target != null
                && (target.equals(origin + "/html-preview-shell") || target.equals(origin + "/visualize-shell"));
    }

    void connect(String url) {
        try {
            if (web != null && !connectionFailed && GatewayURL.targetBase(url).equals(origin)) {
                showChat();
                if (GatewayURL.isChatPage(url, origin) && Uri.parse(url).getFragment() != null) {
                    if (reuseChat(url, origin, ++navigationRequest))
                        return;
                } else
                    return;
            }
        } catch (Exception e) {
            activity.message(activity.L("聊天链接无效"));
            return;
        }
        beforeLeave(() -> connectNow(url));
    }

    void beforeLeave(Runnable action) {
        if (web == null || !connectionReady || connectionFailed && !shellReady
                || !GatewayURL.sameOrigin(web.getUrl(), origin)) {
            action.run();
            return;
        }
        final WebView current = web;
        final int ticket = generation, request = ++switchRequest;
        current.evaluateJavascript(
                "(()=>{if(window.LovestShell)return window.LovestShell.prepareLeave();if(typeof saveDraft==='function')saveDraft();return {version:0,draftUnsafe:!!document.getElementById('message')?.value.trim(),attachmentCount:document.querySelector('.attachment-list:not([hidden])')?.children.length||0};})()",
                value -> {
                    if (web != current || ticket != generation || request != switchRequest)
                        return;
                    try {
                        JSONObject data = new JSONObject(value);
                        if (data.optBoolean("draftUnsafe")) {
                            activity.message(activity.L("草稿尚未保存，请先返回聊天确认登录与保存状态。"));
                            return;
                        }
                        if (data.optInt("attachmentCount") > 0) {
                            new AlertDialog.Builder(activity.ui.context)
                                    .setMessage(activity.L(
                                            "重新加载或切换电脑会移除尚未发送的附件。可留在当前聊天等待上传或发送完成。文字草稿已保存。"))
                                    .setNegativeButton(activity.L("留在当前聊天"), (d, w) -> showChat())
                                    .setPositiveButton(activity.L("放弃附件并继续"),
                                            (d, w) -> {
                                                if (web == current && ticket == generation
                                                        && request == switchRequest)
                                                    action.run();
                                            })
                                    .show();
                            return;
                        }
                        action.run();
                    } catch (Exception e) {
                        activity.message(activity.L("无法确认草稿状态，请返回聊天重试。"));
                    }
                });
    }

    void connectNow(String url) {
        routeAttempted = false;
        try {
            String base = GatewayURL.targetBase(url);
            clear();
            activity.selectedPage = "chat";
            activity.hideNative();
            activity.header(false);
            activity.renderTabs();
            activity.status.setVisibility(View.VISIBLE);
            origin = base;
            activity.computers.opened(origin, System.currentTimeMillis());
            activity.status.setText(activity.L("正在连接 · ") + origin);
            FrameLayout viewport = new FrameLayout(activity);
            webViewport = viewport;
            activity.body.addView(viewport, new FrameLayout.LayoutParams(-1, -1));
            web = new WebView(activity.ui.context);
            web.setBackgroundColor(activity.pageColor());
            viewport.addView(web, new FrameLayout.LayoutParams(-1, -1));
            ImageButton menu = activity.icon("ic_more", activity.L("电脑与聊天选项"), () -> {});
            chatMenu = menu;
            menu.setOnClickListener(v -> gatewayMenu(v));
            FrameLayout.LayoutParams menuPosition =
                    new FrameLayout.LayoutParams(activity.dp(48), activity.dp(48), Gravity.TOP | Gravity.END);
            menuPosition.topMargin = activity.dp(4);
            menuPosition.setMarginEnd(activity.dp(8));
            viewport.addView(menu, menuPosition);
            final String clipboardToken = UUID.randomUUID().toString();
            final String appearance =
                    MobileWebScripts.appearance(activity.getAssets())
                            .replace("__BRIDGE_CLIPBOARD_TOKEN__", clipboardToken)
                            .replace("'__BRIDGE_CONNECTION_NAME__'",
                                    JSONObject.quote(activity.computerList.computerName(origin)))
                            .replace("'__BRIDGE_NATIVE_LANGUAGE__'",
                                    JSONObject.quote(
                                            activity.prefs.getString("language",
                                                                  java.util.Locale.getDefault().getLanguage())
                                                            .startsWith("en")
                                                    ? "en"
                                                    : "zh"));
            WebSettings s = web.getSettings();
            s.setJavaScriptEnabled(true);
            s.setDomStorageEnabled(true);
            s.setAllowFileAccess(false);
            s.setAllowContentAccess(true);
            s.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
            s.setSupportMultipleWindows(false);
            s.setUserAgentString(s.getUserAgentString() + " BridgeMobile/0.1-Android");
            CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);
            // Keep the WebView glyph size aligned with the terminal's 6px lower bound.
            s.setTextZoom(Math.round(activity.getResources().getConfiguration().fontScale * 100));
            s.setMinimumFontSize(6);
            s.setMinimumLogicalFontSize(6);
            final int connectionGeneration = generation;
            web.setWebViewClient(new WebViewClient() {
                private String mainFrame;
                public void onPageStarted(WebView view, String target, android.graphics.Bitmap favicon) {
                    if (view != web || !GatewayURL.sameOrigin(target, base))
                        return;
                    mainFrame = target;
                    dismissGatewayMenu();
                    shellReady = false;
                    chatDetail = false;
                    webModal = false;
                    gatewayAuthenticated = false;
                    resetConnectionError();
                    if ("chat".equals(activity.selectedPage)) {
                        activity.header(false);
                        activity.renderTabs();
                    }
                }
                public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                    String target = request.getUrl().toString();
                    if (view != web)
                        return true;
                    if (!request.isForMainFrame())
                        return !previewShell(target);
                    if (previewShell(target))
                        return true;
                    if (target.equals("codexbridge://home") && request.hasGesture()
                            && GatewayURL.sameOrigin(view.getUrl(), origin)) {
                        activity.computerList.home();
                        return true;
                    }
                    if (ArtifactDownload.accepts(target, origin) && request.hasGesture()) {
                        activity.downloads.downloadArtifact(target);
                        return true;
                    }
                    if (GatewayURL.sameOrigin(target, origin))
                        return false;
                    if (request.hasGesture()
                            && ("https".equals(request.getUrl().getScheme())
                                    || "http".equals(request.getUrl().getScheme())))
                        new AlertDialog.Builder(activity.ui.context)
                                .setMessage(activity.L("在浏览器打开外部链接？\n") + request.getUrl().getHost())
                                .setPositiveButton(activity.L("打开"),
                                        (d, w)
                                                -> activity.startActivity(
                                                        new Intent(Intent.ACTION_VIEW, request.getUrl())))
                                .setNegativeButton(activity.L("取消"), null)
                                .show();
                    return true;
                }
                public void onPageFinished(WebView view, String target) {
                    if (view == web && !connectionFailed && GatewayURL.isChatPage(target, origin)) {
                        connectionReady = true;
                        cancelConnectionDeadline();
                        cancelRouteRecovery();
                        CookieManager.getInstance().flush();
                        view.evaluateJavascript(
                                "window.BridgeMobileCapabilities = Object.freeze({ diagnosticsExport: true });",
                                null);
                        view.evaluateJavascript(appearance, null);
                        syncWebTheme();
                        activity.status.setVisibility(View.GONE);
                        rememberConnection();
                        if (pendingStorage != null) {
                            String snapshot = pendingStorage;
                            pendingStorage = null;
                            view.evaluateJavascript("(()=>{const data=" + snapshot
                                            + ";if(data&&typeof data==='object'){for(const [k,v] of Object.entries(data))if(typeof v==='string')localStorage.setItem(k,v);location.reload();}})()",
                                    null);
                        }
                    }
                }
                public void onReceivedError(WebView view, WebResourceRequest req, WebResourceError error) {
                    if (req.isForMainFrame())
                        connectionError(url, view, connectionGeneration,
                                ConnectionProblem.fromWebError(error.getErrorCode()));
                }
                public void onReceivedHttpError(
                        WebView view, WebResourceRequest req, WebResourceResponse response) {
                    if (req.isForMainFrame() && GatewayURL.sameOrigin(req.getUrl().toString(), base))
                        connectionError(url, view, connectionGeneration,
                                ConnectionProblem.fromHttpStatus(response.getStatusCode()));
                }
                public void onReceivedSslError(
                        WebView view, SslErrorHandler handler, android.net.http.SslError error) {
                    handler.cancel();
                    String failed = error.getUrl(), requested = mainFrame == null ? url : mainFrame;
                    if (GatewayURL.sameOrigin(failed, base)
                            && requested.split("#", 2)[0].equals(failed.split("#", 2)[0]))
                        connectionError(url, view, connectionGeneration, ConnectionProblem.certificate());
                }
            });
            web.setWebChromeClient(new ChatChromeClient(menu, clipboardToken));

            web.setDownloadListener(
                    (target, agent, disposition, mime, length) -> activity.downloads.downloadArtifact(target));
            showChat();
            watchConnection(url, web, generation);
            loadWithRecovery(base, url, web, generation);
            activity.notifications.monitor();
        } catch (Exception e) {
            activity.message(e.getMessage());
        }
    }

    private boolean trusted(WebView view, String url) {
        return view == web && GatewayURL.sameOrigin(url, origin) && GatewayURL.sameOrigin(view.getUrl(), origin);
    }

    private boolean focusedChat(WebView view) {
        return "chat".equals(activity.selectedPage) && view.hasWindowFocus();
    }

    void onWindowFocused() {
        final WebView current = web;
        final int ticket = generation;
        if (current != null) {
            current.post(() -> {
                if (ticket == generation && current.hasWindowFocus() && trusted(current, current.getUrl()))
                    current.evaluateJavascript("window.dispatchEvent(new Event('focus'));", null);
            });
        }
    }

    void prepareToPause() {
        if (web != null && GatewayURL.sameOrigin(web.getUrl(), origin))
            web.evaluateJavascript("window.LovestShell?.persist()", null);
        dismissGatewayMenu();
        cancelRouteRecovery();
    }

    void configurationChanged(float fontScale) {
        if (web == null)
            return;
        web.getSettings().setTextZoom(Math.round(fontScale * 100));
        web.requestLayout();
        web.invalidate();
    }

    @FunctionalInterface
    private interface PromptHandler {
        String handle(WebView view, String url, String value) throws Exception;
    }

    private final class ChatChromeClient extends WebChromeClient {
        private final ImageButton menu;
        private final Map<String, PromptHandler> handlers = new HashMap<>();

        ChatChromeClient(ImageButton menu, String token) {
            this.menu = menu;
            handlers.put("lovest-shell:" + token, this::shell);
            handlers.put("codexbridge-system-bars:" + token, this::systemBars);
            handlers.put("codexbridge-modal:" + token, this::modal);
            handlers.put("codexbridge-session:" + token, this::session);
            handlers.put("codexbridge-chat-visible:" + token, this::chatVisible);
            handlers.put("codexbridge-auth-lost:" + token, this::authLost);
            handlers.put("codexbridge-logout:" + token, this::logout);
            handlers.put("codexbridge-theme:" + token, this::theme);
            handlers.put("codexbridge-language:" + token, this::language);
            handlers.put("codexbridge-copy:" + token, this::copy);
        }

        @Override
        public boolean onJsAlert(WebView view, String url, String message, JsResult result) {
            if (view != web || previewShell(url)) {
                result.cancel();
                return true;
            }
            return super.onJsAlert(view, url, message, result);
        }

        @Override
        public boolean onJsConfirm(WebView view, String url, String message, JsResult result) {
            if (view != web || previewShell(url)) {
                result.cancel();
                return true;
            }
            return super.onJsConfirm(view, url, message, result);
        }

        @Override
        public boolean onJsPrompt(WebView view, String url, String prompt, String value, JsPromptResult result) {
            if (view != web || previewShell(url)) {
                result.cancel();
                return true;
            }
            PromptHandler handler = handlers.get(prompt);
            if (handler == null) {
                if (!prompt.startsWith("codexbridge-") && !prompt.startsWith("lovest-shell:"))
                    return false;
                result.cancel();
                return true;
            }

            String response = null;
            if (trusted(view, url)) {
                try {
                    response = handler.handle(view, url, value);
                } catch (Exception invalid) {
                    // Invalid or stale native requests cannot fall back to an ordinary prompt.
                }
            }
            if (response == null)
                result.cancel();
            else
                result.confirm(response);
            return true;
        }

        private String shell(WebView view, String url, String value) throws Exception {
            if (!GatewayURL.isChatPage(url, origin) || value == null || value.length() > 4096)
                return null;
            JSONObject data = new JSONObject(value);
            if (data.optInt("version") != 1)
                return null;
            String action = data.optString("action");
            if ("ready".equals(action)) {
                shellReady = true;
                return "ready";
            }
            if (!shellReady)
                return null;
            if ("inbox".equals(action) && focusedChat(view)) {
                view.post(() -> {
                    if (trusted(view, url) && focusedChat(view)) activity.notifications.inbox();
                });
                return "opened";
            }
            if (!"state".equals(action))
                return null;

            chatDetail = data.optBoolean("detail");
            webModal = data.optBoolean("modal");
            gatewayAuthenticated = data.optBoolean("authenticated");
            if (gatewayAuthenticated && chatDetail && !data.isNull("threadId")) {
                String thread = data.getString("threadId");
                String host = data.getString("host");
                GatewayURL.chat(origin, thread, host);
                activity.computers.setLastChat(
                        origin, new JSONObject().put("threadId", thread).put("host", host).toString());
            }
            if ("chat".equals(activity.selectedPage)) {
                activity.header(chatDetail);
                activity.renderTabs();
                menu.setVisibility(chatDetail && !webModal ? View.VISIBLE : View.GONE);
            }
            if (gatewayAuthenticated && pendingComputerAction != null) {
                view.post(() -> {
                    if (view == web)
                        runComputerAction();
                });
            }
            return "saved";
        }

        private String systemBars(WebView view, String url, String value) throws JSONException {
            if (!focusedChat(view) || value == null || value.length() >= 100)
                return null;
            JSONObject colors = new JSONObject(value);
            String top = colors.getString("top");
            String bottom = colors.getString("bottom");
            if (!top.matches("#[0-9a-fA-F]{6}") || !bottom.matches("#[0-9a-fA-F]{6}"))
                return null;
            activity.updateBars(android.graphics.Color.parseColor(top), android.graphics.Color.parseColor(bottom));
            return "saved";
        }

        private String modal(WebView view, String url, String value) {
            if (!view.hasWindowFocus() || !("open".equals(value) || "closed".equals(value)))
                return null;
            boolean open = "open".equals(value);
            menu.setVisibility(open || shellReady && !chatDetail ? View.GONE : View.VISIBLE);
            menu.setEnabled(!open);
            if (open)
                dismissGatewayMenu();
            return "saved";
        }

        private String session(WebView view, String url, String value) {
            authLossAttempted = false;
            CookieManager.getInstance().flush();
            MonitorService.bindingsChanged();
            rememberConnection();
            return "saved";
        }

        private String chatVisible(WebView view, String url, String value) throws Exception {
            if (!activity.resumed || !view.hasWindowFocus() || !activity.computerList.saved().contains(origin)
                    || value == null || value.length() > 4096)
                return null;
            JSONObject chat = new JSONObject(value);
            String thread = chat.getString("threadId");
            String host = chat.getString("host");
            GatewayURL.chat(origin, thread, host);
            TaskNotifications.clearChat(activity, origin, thread, host);
            return "cleared";
        }

        private String authLost(WebView view, String url, String value) {
            if (!focusedChat(view) || authLossAttempted)
                return null;
            authLossAttempted = true;
            loadWithRecovery(origin, origin + "/", view, generation);
            return "recovering";
        }

        private String logout(WebView view, String url, String value) {
            if (!view.hasWindowFocus())
                return null;
            DeviceBinding.clear(activity, origin);
            activity.computers.setLastChat(origin, null);
            EventClient.forget(origin);
            MonitorService.bindingsChanged();
            return "cleared";
        }

        private String theme(WebView view, String url, String value) {
            if (!view.hasWindowFocus()
                    || !("system".equals(value) || "light".equals(value) || "dark".equals(value)))
                return null;
            activity.prefs.edit().putString("settingsTheme", value).apply();
            return "saved";
        }

        private String language(WebView view, String url, String value) {
            if (!("en".equals(value) || "zh".equals(value)))
                return null;
            activity.prefs.edit().putString("language", value).apply();
            menu.setContentDescription(activity.L("电脑与聊天选项"));
            activity.downloads.renderDownload();
            return "saved";
        }

        private String copy(WebView view, String url, String value) {
            if (!view.hasWindowFocus() || value == null || value.length() > 262144)
                return null;
            ClipboardManager clipboard = (ClipboardManager) activity.getSystemService(Context.CLIPBOARD_SERVICE);
            clipboard.setPrimaryClip(ClipData.newPlainText("Lovest Code", value));
            return "copied";
        }

        @Override
        public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
            if (files != null)
                files.onReceiveValue(null);
            files = callback;
            fileView = view;
            fileGeneration = generation;
            Intent picker = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            picker.addCategory(Intent.CATEGORY_OPENABLE);
            picker.setType("*/*");
            picker.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            picker.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, params.getMode() == FileChooserParams.MODE_OPEN_MULTIPLE);
            ArrayList<String> types = new ArrayList<>();
            for (String type : params.getAcceptTypes())
                if (type != null && type.contains("/"))
                    types.add(type);
            if (types.size() == 1)
                picker.setType(types.get(0));
            else if (!types.isEmpty())
                picker.putExtra(Intent.EXTRA_MIME_TYPES, types.toArray(new String[0]));
            try {
                activity.startActivityForResult(picker, 31);
            } catch (Exception e) {
                files.onReceiveValue(null);
                files = null;
                fileView = null;
                activity.message(activity.L("没有可用的文件选择器"));
            }
            return true;
        }
    }

    boolean openIntent(Intent intent) {
        final int request = ++navigationRequest;
        try {
            String base = intent.getStringExtra("origin"), thread = intent.getStringExtra("thread"),
                   host = intent.getStringExtra("host");
            Uri link = intent.getData();
            if (link != null) {
                if (!"codexbridge".equals(link.getScheme()) || !"open".equals(link.getHost()))
                    return false;
                base = link.getQueryParameter("origin");
                thread = link.getQueryParameter("thread");
                host = link.getQueryParameter("host");
            }
            if (base == null)
                return false;
            if (!activity.computerList.saved().contains(base)) {
                activity.message(activity.L("请先扫码保存通知对应的电脑，再打开聊天。"));
                return false;
            }
            String target = thread == null || thread.isEmpty()
                    ? base + "/"
                    : GatewayURL.chat(base, thread, host == null ? "local" : host);
            if (thread == null || thread.isEmpty() || !reuseChat(target, base, request))
                connect(target);
            else
                showChat();
            return true;
        } catch (Exception e) {
            activity.message(activity.L("通知链接无效"));
            return false;
        }
    }

    boolean reuseChat(String target, String base, int request) {
        if (web == null || !base.equals(origin) || !GatewayURL.isChatPage(web.getUrl(), base))
            return false;
        final WebView current = web;
        final int ticket = generation;
        final String binding = EventClient.sessionCookie(CookieManager.getInstance().getCookie(base + "/"), base);
        if (binding.isEmpty())
            return false;
        String hash = "#" + Uri.parse(target).getEncodedFragment();
        current.evaluateJavascript("(()=>typeof window.BridgeOpenChatLink==='function'&&window.BridgeOpenChatLink("
                        + JSONObject.quote(hash) + "))()",
                accepted -> {
                    if (web != current || ticket != generation || request != navigationRequest
                            || !base.equals(origin) || !activity.computerList.saved().contains(base)
                            || !GatewayURL.isChatPage(current.getUrl(), base)
                            || !binding.equals(EventClient.sessionCookie(
                                    CookieManager.getInstance().getCookie(base + "/"), base)))
                        return;
                    if (!"true".equals(accepted))
                        beforeLeave(() -> connectNow(target));
                });
        return true;
    }

    void selectedFiles(int result, Intent data) {
        ValueCallback<Uri[]> callback = files;
        WebView target = fileView;
        int ticket = fileGeneration;
        files = null;
        fileView = null;
        if (callback == null)
            return;
        if (result != Activity.RESULT_OK || target != web || ticket != generation) {
            callback.onReceiveValue(null);
            return;
        }
        LinkedHashSet<Uri> selected = new LinkedHashSet<>();
        if (data != null) {
            ClipData clips = data.getClipData();
            if (clips != null)
                for (int i = 0; i < clips.getItemCount(); i++) {
                    Uri uri = clips.getItemAt(i).getUri();
                    if (uri != null)
                        selected.add(uri);
                }
            if (data.getData() != null)
                selected.add(data.getData());
        }
        if (selected.isEmpty()) {
            callback.onReceiveValue(null);
            activity.message(activity.L("未收到所选文件，请重新选择。"));
            return;
        }
        activity.worker.execute(() -> {
            boolean readable = true;
            try {
                for (Uri uri : selected) {
                    if (!"content".equals(uri.getScheme()))
                        throw new java.io.IOException();
                    android.content.pm.ProviderInfo provider =
                            activity.getPackageManager().resolveContentProvider(uri.getAuthority(), 0);
                    if (provider == null || provider.applicationInfo.uid == android.os.Process.myUid())
                        throw new java.io.IOException();
                    try (android.content.res.AssetFileDescriptor descriptor =
                                    activity.getContentResolver().openAssetFileDescriptor(uri, "r")) {
                        if (descriptor == null)
                            throw new java.io.IOException();
                    }
                }
            } catch (Exception e) {
                readable = false;
            }
            final boolean accepted = readable;
            activity.runOnUiThread(() -> {
                if (target != web || ticket != generation) {
                    callback.onReceiveValue(null);
                    return;
                }
                callback.onReceiveValue(accepted ? selected.toArray(new Uri[0]) : null);
                if (!accepted)
                    activity.message(activity.L("无法读取所选文件，请从系统文件选择器重新选择。"));
            });
        });
    }

    void handleBack() {
        if (gatewayPopup != null) {
            dismissGatewayMenu();
            return;
        }
        if (!"chat".equals(activity.selectedPage)) {
            showChat();
            return;
        }
        if (web != null
                && (!connectionReady || connectionFailed || connectionTask != null || routeDiscovery != null)) {
            activity.computerList.home();
            return;
        }
        if (web == null || !GatewayURL.sameOrigin(web.getUrl(), origin)) {
            activity.moveTaskToBack(true);
            return;
        }
        final int ticket = generation, request = ++navigationRequest;
        web.evaluateJavascript(
                "(()=>{const dialog=[...document.querySelectorAll('dialog[open]')].at(-1);if(dialog){if(dialog.dispatchEvent(new Event('cancel',{cancelable:true})))dialog.close();return true;}return window.BridgeNavigation?.back()||false;})()",
                result -> {
                    if (ticket == generation && request == navigationRequest
                            && "chat".equals(activity.selectedPage) && !"true".equals(result))
                        activity.moveTaskToBack(true);
                });
    }
}
