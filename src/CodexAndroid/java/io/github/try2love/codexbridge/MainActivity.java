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

/** Dock-owned Android shell. */
public final class MainActivity extends Activity implements SharedPreferences.OnSharedPreferenceChangeListener {
    final ComputerList computerList = new ComputerList(this);

    final ChatHost chat = new ChatHost(this);

    final NotificationInbox notifications = new NotificationInbox(this);

    final DownloadController downloads = new DownloadController(this);

    final UpdateController updates = new UpdateController(this);

    private LinearLayout root;

    private LinearLayout bar;

    TextView status;

    private SystemBars systemBars;

    SharedPreferences prefs;

    ComputerStore computers;

    final ExecutorService worker = Executors.newSingleThreadExecutor();

    final Handler homeHandler = new Handler(Looper.getMainLooper());

    boolean resumed;

    private long homeBackAt;

    private Toast exitToast;

    private FrameLayout screen;

    private SettingsPage settingsPage;

    SettingsUi ui;

    FrameLayout body;

    FrameLayout nativeHost;

    private LinearLayout tabs;

    private SettingsPage embeddedSettings;

    String selectedPage = "chat";

    private int barTop = 0xfff5f5f3;

    private int barBottom = 0xfff5f5f3;

    void updateBars(int top, int bottom) {
        barTop = top;
        barBottom = bottom;
        systemBars.update(top, bottom);
    }

    int pageColor() { return ui.dark ? ui.page : 0xfff5f5f3; }

    private int textColor() { return ui.dark ? ui.text : 0xff202124; }

    int mutedColor() { return ui.dark ? ui.muted : 0xff777b80; }

    int surfaceColor() { return ui.dark ? ui.surface : 0xffffffff; }

    private int buttonColor() { return ui.dark ? ui.pressed : 0xfff4f5f7; }

    private void applyTheme() {
        chat.dismissGatewayMenu();
        ui = new SettingsUi(SettingsUi.themed(this));
        root.setBackgroundColor(pageColor());
        status.setTextColor(mutedColor());
        updateBars(pageColor(), pageColor());
        if (chat.web != null) {
            chat.web.setBackgroundColor(pageColor());
            if (chat.chatMenu != null)
                chat.chatMenu.setColorFilter(textColor());
            chat.syncWebTheme();
        }
        if ("computers".equals(selectedPage))
            computerList.home();
        else if ("me".equals(selectedPage))
            showMe();
        else
            chat.showChat();
    }

    @Override
    public void onSharedPreferenceChanged(SharedPreferences preferences, String key) {
        if ("settingsTheme".equals(key))
            applyTheme();
    }

    String L(String value) { return MobileStrings.text(this, value); }

    int dp(int n) { return (int) (getResources().getDisplayMetrics().density * n); }

    TextView text(String value, int size) {
        TextView t = new TextView(ui.context);
        t.setText(value);
        t.setTextSize(size);
        t.setTextColor(textColor());
        t.setPadding(0, dp(5), 0, dp(5));
        return t;
    }

    android.graphics.drawable.GradientDrawable background(int color, int radius) {
        android.graphics.drawable.GradientDrawable d = new android.graphics.drawable.GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radius));
        return d;
    }

    Button button(String value, Runnable action) {
        Button b = new Button(ui.context);
        b.setText(value);
        b.setTextSize(16);
        b.setTextColor(textColor());
        b.setAllCaps(false);
        b.setMinHeight(dp(54));
        b.setStateListAnimator(null);
        b.setElevation(0);
        b.setTranslationZ(0);
        b.setPadding(dp(18), dp(10), dp(18), dp(10));
        android.graphics.drawable.GradientDrawable face = background(buttonColor(), 12);
        b.setBackground(new android.graphics.drawable.RippleDrawable(
                android.content.res.ColorStateList.valueOf(ui.dark ? ui.pressed : 0x18202124), face, null));
        b.setOnClickListener(v -> action.run());
        return b;
    }

    LinearLayout column() {
        LinearLayout c = new LinearLayout(ui.context);
        c.setOrientation(1);
        return c;
    }

    void add(View v, LinearLayout parent, int bottom) {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, -2);
        p.bottomMargin = dp(bottom);
        parent.addView(v, p);
    }

    LinearLayout card() {
        LinearLayout c = column();
        c.setPadding(dp(16), dp(10), dp(12), dp(10));
        c.setBackground(background(surfaceColor(), 12));
        return c;
    }

    ImageButton icon(String name, String description, Runnable action) {
        ImageButton b = new ImageButton(ui.context);
        b.setImageResource(getResources().getIdentifier(name, "drawable", getPackageName()));
        b.setColorFilter(textColor());
        b.setContentDescription(description);
        b.setPadding(dp(12), dp(12), dp(12), dp(12));
        android.util.TypedValue ripple = new android.util.TypedValue();
        ui.context.getTheme().resolveAttribute(android.R.attr.selectableItemBackgroundBorderless, ripple, true);
        b.setBackgroundResource(ripple.resourceId);
        b.setOnClickListener(v -> action.run());
        return b;
    }

    void header(boolean chatDetails) {
        bar.setVisibility(chatDetails ? View.GONE : View.VISIBLE);
        if (chatDetails)
            return;
        bar.removeAllViews();
        bar.setBackgroundColor(surfaceColor());
        ImageView logo = new ImageView(ui.context);
        logo.setImageResource(getResources().getIdentifier("ic_bridge", "drawable", getPackageName()));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(28), dp(28));
        lp.setMarginEnd(dp(10));
        bar.addView(logo, lp);
        String title = "computers".equals(selectedPage) ? L("电脑")
                : "me".equals(selectedPage)             ? L("我的")
                : chat.web != null                      ? computerList.computerName(chat.origin)
                                                        : "Lovest Code";
        LinearLayout identity = column();
        TextView name = text(title, 18);
        name.setTypeface(null, android.graphics.Typeface.BOLD);
        name.setSingleLine();
        name.setEllipsize(android.text.TextUtils.TruncateAt.END);
        identity.addView(name);
        if ("chat".equals(selectedPage) && chat.web != null && !chat.connectionFailed) {
            TextView state = text(L(chat.gatewayAuthenticated ? "网关已登录" : "等待连接或登录"), 12);
            state.setTextColor(mutedColor());
            state.setPadding(0, 0, 0, 0);
            identity.addView(state);
        }
        bar.addView(identity, new LinearLayout.LayoutParams(0, -2, 1));
        if ("computers".equals(selectedPage))
            bar.addView(icon("ic_add", L("添加电脑"), computerList::addComputer),
                    new LinearLayout.LayoutParams(dp(48), dp(48)));
        else if ("chat".equals(selectedPage) && chat.web != null)
            bar.addView(icon("ic_more", L("电脑与聊天选项"), () -> chat.gatewayMenu(bar)),
                    new LinearLayout.LayoutParams(dp(48), dp(48)));
    }

    void renderTabs() {
        if (tabs == null)
            return;
        tabs.removeAllViews();
        tabs.setBackgroundColor(surfaceColor());
        String[] ids = {"chat", "computers", "me"}, labels = {"聊天", "电脑", "我的"},
                 icons = {"ic_chat", "ic_computer", "ic_menu_user_round"};
        for (int i = 0; i < ids.length; i++) {
            final String page = ids[i];
            Button tab = button(L(labels[i]), () -> selectPage(page));
            boolean selected = page.equals(selectedPage);
            int color = selected ? ui.accent : mutedColor();
            tab.setSelected(selected);
            tab.setTextColor(color);
            tab.setTextSize(11);
            tab.setSingleLine(true);
            tab.setBackgroundColor(android.graphics.Color.TRANSPARENT);
            tab.setPadding(dp(4), dp(7), dp(4), dp(7));
            tab.setMinHeight(dp(62));
            android.graphics.drawable.Drawable glyph =
                    getDrawable(getResources().getIdentifier(icons[i], "drawable", getPackageName())).mutate();
            glyph.setTint(color);
            glyph.setBounds(0, 0, dp(21), dp(21));
            tab.setCompoundDrawables(null, glyph, null, null);
            tab.setCompoundDrawablePadding(dp(4));
            tabs.addView(tab, new LinearLayout.LayoutParams(0, -2, 1));
        }
        tabs.setVisibility(
                "chat".equals(selectedPage) && (chat.chatDetail || chat.webModal) ? View.GONE : View.VISIBLE);
    }

    void hideNative() {
        if (embeddedSettings != null) {
            embeddedSettings.stopEmbedded();
            embeddedSettings = null;
        }
        nativeHost.removeAllViews();
        nativeHost.setVisibility(View.GONE);
    }

    void selectPage(String page) {
        chat.dismissGatewayMenu();
        if (chat.web != null && GatewayURL.sameOrigin(chat.web.getUrl(), chat.origin))
            chat.web.evaluateJavascript("window.LovestShell?.persist()", null);
        if ("computers".equals(page))
            computerList.home();
        else if ("me".equals(page))
            showMe();
        else
            chat.showChat();
    }

    void showMe() {
        chat.leaveConnectionPage();
        selectedPage = "me";
        hideNative();
        computerList.stopComputerChecks();
        if (chat.webViewport != null)
            chat.webViewport.setVisibility(View.GONE);
        nativeHost.setVisibility(View.VISIBLE);
        status.setVisibility(View.GONE);
        embeddedSettings = new SettingsPage(this, 0, false);
        nativeHost.addView(embeddedSettings.embeddedView(), new FrameLayout.LayoutParams(-1, -1));
        embeddedSettings.startEmbedded();
        header(false);
        renderTabs();
        updateBars(pageColor(), surfaceColor());
    }

    void languagePicker() {
        new AlertDialog.Builder(ui.context)
                .setTitle(L("语言"))
                .setSingleChoiceItems(new String[] {"简体中文", "English"},
                        prefs.getString("language", java.util.Locale.getDefault().getLanguage()).startsWith("en")
                                ? 1
                                : 0,
                        (d, w) -> {
                            prefs.edit().putString("language", w == 0 ? "zh" : "en").apply();
                            if (chat.web != null)
                                chat.web.evaluateJavascript("window.LovestShell?.language("
                                                + JSONObject.quote(w == 0 ? "zh" : "en") + ")",
                                        null);
                            d.dismiss();
                            showMe();
                        })
                .setNegativeButton(L("取消"), null)
                .show();
    }

    void message(String value) {
        resetExitGesture();
        new AlertDialog.Builder(ui.context).setMessage(L(value)).setPositiveButton(L("好"), null).show();
    }

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = getSharedPreferences("bridge", 0);
        computers = new ComputerStore(prefs);
        ui = new SettingsUi(SettingsUi.themed(this));
        prefs.registerOnSharedPreferenceChangeListener(this);
        for (String address : computerList.saved())
            EventClient.setName(address, computers.cookieName(address));
        root = new LinearLayout(ui.context);
        root.setOrientation(1);
        root.setBackgroundColor(pageColor());
        screen = new FrameLayout(this);
        screen.addView(root, new FrameLayout.LayoutParams(-1, -1));
        setContentView(screen);
        systemBars = new SystemBars(this, screen, downloads::requestPlacement);
        if (Build.VERSION.SDK_INT >= 33)
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                    android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT, chat::handleBack);
        bar = new LinearLayout(ui.context);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setPadding(dp(20), dp(4), dp(12), dp(4));
        root.addView(bar);
        header(false);
        downloads.attach(screen);
        updates.restoreUpdate();
        status = text("", 12);
        status.setTextColor(mutedColor());
        status.setGravity(Gravity.CENTER);
        root.addView(status);
        body = new FrameLayout(this);
        root.addView(body, new LinearLayout.LayoutParams(-1, 0, 1));
        nativeHost = new FrameLayout(this);
        body.addView(nativeHost, new FrameLayout.LayoutParams(-1, -1));
        tabs = new LinearLayout(ui.context);
        root.addView(tabs);
        if (!chat.openIntent(getIntent())) {
            String active = computers.active();
            if (computerList.saved().contains(active))
                chat.connect(computerList.resumeUrl(active));
            else
                chat.showChat();
        }
        notifications.monitor();
        root.post(notifications::requestStartupNotifications);
        if (state != null && state.getBoolean("settingsOpen", false)) {
            int offset = state.getInt("settingsScroll", 0);
            root.post(() -> {
                settingsPage = new SettingsPage(this, offset, false);
                settingsPage.show();
            });
        }
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        chat.openIntent(intent);
    }

    Dialog panel(String title, LinearLayout content) {
        resetExitGesture();
        Dialog dialog = new Dialog(ui.context);
        dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);
        LinearLayout body = column();
        body.setBackground(background(pageColor(), 12));
        body.setPadding(dp(18), dp(12), dp(18), dp(12));
        LinearLayout heading = new LinearLayout(ui.context);
        heading.setGravity(Gravity.CENTER_VERTICAL);
        TextView name = text(title, 23);
        name.setTypeface(null, android.graphics.Typeface.BOLD);
        heading.addView(name, new LinearLayout.LayoutParams(0, -2, 1));
        Button done = button(L("完成"), dialog::dismiss);
        heading.addView(done);
        body.addView(heading);
        ScrollView scroll = new ScrollView(ui.context);
        content.setPadding(0, dp(20), 0, dp(16));
        scroll.addView(content);
        body.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        dialog.setContentView(body);
        dialog.show();
        dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        dialog.getWindow().setLayout(getResources().getDisplayMetrics().widthPixels - dp(24),
                getResources().getDisplayMetrics().heightPixels - dp(100));
        return dialog;
    }

    void settings() {
        if (settingsPage == null || !settingsPage.isShowing()) {
            settingsPage = new SettingsPage(this, 0);
            settingsPage.show();
        }
    }

    void reopenSettings(SettingsPage previous) {
        if (previous != settingsPage || !previous.isShowing() || previous.isClosing())
            return;
        SettingsPage replacement = previous.replacement();
        previous.dismiss();
        settingsPage = replacement;
        settingsPage.show();
    }

    String currentComputerName() {
        return chat.origin.isEmpty() ? L("尚未连接") : computerList.computerName(chat.origin);
    }

    String appVersion() { return getString(R.string.dock_version_name); }

    @Override
    public void onRequestPermissionsResult(int code, String[] names, int[] results) {
        super.onRequestPermissionsResult(code, names, results);
        boolean allowed = results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED;
        if (code == 25) {
            prefs.edit().putBoolean("alerts", allowed).apply();
            if (allowed)
                notifications.monitor();
            return;
        }
        if (!allowed) {
            message(L(code == 21 ? "未获得权限，可继续粘贴地址使用。" : "请在系统设置中允许通知。"));
            return;
        }
        if (code == 26)
            notifications.toggleBackgroundNotifications();
        if (code == 21)
            computerList.scan();
        if (code == 23)
            notifications.testNotification();
        if (code == 22) {
            prefs.edit().putBoolean("alerts", true).apply();
            notifications.monitor();
        }
    }

    @Override
    protected void onActivityResult(int code, int result, Intent data) {
        super.onActivityResult(code, result, data);
        if (code == 32) {
            downloads.saveDownload(result, data);
            return;
        }
        if (code == 20 && result == RESULT_OK && data != null)
            computerList.choose(data.getStringExtra("code"));
        if (code == 31)
            chat.selectedFiles(result, data);
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        if (settingsPage != null && settingsPage.isShowing() && !settingsPage.isClosing()) {
            state.putBoolean("settingsOpen", true);
            state.putInt("settingsScroll", settingsPage.scrollPosition());
        }
        super.onSaveInstanceState(state);
    }

    @Override
    protected void onResume() {
        super.onResume();
        resumed = true;
        if (ui.dark != new SettingsUi(SettingsUi.themed(this)).dark)
            applyTheme();
        ComputerNotifications.onChange = () -> runOnUiThread(computerList::renderComputerNotifications);
        computerList.checkComputers();
        MonitorService.setVisible(true);
        if (!MonitorService.startBackground(this))
            message(L("后台通知启动失败，请重新打开 App 后再试。"));
        if (chat.web != null)
            chat.web.onResume();
        if (settingsPage != null && settingsPage.isShowing())
            settingsPage.refresh();
        updates.resumeUpdateInstall();
    }

    // Keep the live WebView and its drafts when the window rotates or resizes.
    @Override
    public void onConfigurationChanged(android.content.res.Configuration config) {
        super.onConfigurationChanged(config);
        chat.dismissGatewayMenu();
        if ("system".equals(SettingsUi.theme(this)) && ui.dark != new SettingsUi(SettingsUi.themed(this)).dark)
            applyTheme();
        screen.requestApplyInsets();
        downloads.requestPlacement();
        chat.configurationChanged(config.fontScale);
        if (settingsPage != null && "system".equals(SettingsUi.theme(this)))
            reopenSettings(settingsPage);
    }

    @Override
    public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        if (focused && systemBars != null) {
            systemBars.update(barTop, barBottom);
            chat.onWindowFocused();
        }
    }

    @Override
    protected void onPause() {
        chat.prepareToPause();
        ComputerNotifications.onChange = null;
        resumed = false;
        computerList.stopComputerChecks();
        resetExitGesture();
        MonitorService.setVisible(false);
        if (chat.web != null)
            chat.web.onPause();
        CookieManager.getInstance().flush();
        super.onPause();
    }

    void resetExitGesture() {
        homeBackAt = 0;
        if (exitToast != null) {
            exitToast.cancel();
            exitToast = null;
        }
    }

    @Override
    public void onBackPressed() {
        chat.handleBack();
    }

    @Override
    public void onDestroy() {
        prefs.unregisterOnSharedPreferenceChangeListener(this);
        if (embeddedSettings != null)
            embeddedSettings.stopEmbedded();
        if (settingsPage != null)
            settingsPage.dismiss();
        updates.close();
        downloads.close();
        chat.clear();
        computerList.close();
        worker.shutdownNow();
        notifications.close();
        super.onDestroy();
    }

    String computerName(String address) { return computerList.computerName(address); }

    Set<String> saved() { return computerList.saved(); }

    void toggleNotifications() { notifications.toggleNotifications(); }

    void toggleBackgroundNotifications() { notifications.toggleBackgroundNotifications(); }

    void testNotification() { notifications.testNotification(); }

    void showAppUpdates() { updates.showAppUpdates(); }

    void webSettings(String page) { chat.webSettings(page); }
}
