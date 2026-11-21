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

final class ComputerList {
    private final MainActivity activity;

    ComputerList(MainActivity activity) { this.activity = activity; }

    private final ExecutorService probes = Executors.newFixedThreadPool(3);

    private final Map<String, TextView> homeStates = new LinkedHashMap<>();

    private final Map<String, ComputerReachability> lastReachability = new HashMap<>();

    private final Map<String, LinearLayout> homeNotifications = new LinkedHashMap<>();

    private final Map<String, android.graphics.Bitmap> clientIcons = new HashMap<>();

    private final List<GatewayProbe> homeProbes = new ArrayList<>();

    private final List<Future<?>> probeTasks = new ArrayList<>();

    private int probeGeneration;

    private final Runnable homeRefresh = () -> checkComputers();

    void clearRows() {
        stopComputerChecks();
        homeStates.clear();
        homeNotifications.clear();
    }

    void close() { probes.shutdownNow(); }

    void forgetReachability(String address) { lastReachability.remove(address); }

    void addComputer() {
        new AlertDialog.Builder(activity.ui.context)
                .setTitle(activity.L("添加电脑"))
                .setItems(new String[] {activity.L("扫码连接电脑"), activity.L("输入网关地址")},
                        (d, w) -> {
                            if (w == 0)
                                scan();
                            else
                                manualAddress();
                        })
                .show();
    }

    void computerDetails(String address) {
        LinearLayout content = activity.column();
        Dialog dialog = activity.panel(computerName(address), content);
        activity.add(activity.text(address, 14), content, 12);
        activity.add(activity.button(activity.L(address.equals(activity.chat.origin) && activity.chat.web != null
                                                     ? "继续聊天"
                                                     : "连接电脑"),
                             () -> {
                                 dialog.dismiss();
                                 activity.chat.connect(resumeUrl(address));
                             }),
                content, 8);
        activity.add(activity.button(activity.L("账号与接入"), () -> {
            dialog.dismiss();
            activity.chat.openComputerAction(address, "accounts");
        }), content, 8);
        activity.add(activity.button(activity.L("连接诊断"), () -> {
            dialog.dismiss();
            activity.chat.openComputerAction(address, "diagnostics");
        }), content, 8);
        activity.add(activity.button(activity.L("重命名"), () -> {
            dialog.dismiss();
            renameComputer(address);
        }), content, 8);
        activity.add(activity.button(activity.L("移除这台电脑"), () -> {
            dialog.dismiss();
            confirmRemove(address);
        }), content, 8);
    }

    String resumeUrl(String address) {
        try {
            String value = activity.computers.lastChat(address);
            if (!value.isEmpty()) {
                JSONObject route = new JSONObject(value);
                return GatewayURL.chat(address, route.getString("threadId"), route.getString("host"));
            }
        } catch (Exception ignored) {
        }
        return address + "/";
    }

    Set<String> saved() { return activity.computers.addresses(); }

    void home() {
        activity.chat.leaveConnectionPage();
        activity.selectedPage = "computers";
        activity.hideNative();
        clearRows();
        if (activity.chat.webViewport != null)
            activity.chat.webViewport.setVisibility(View.GONE);
        activity.nativeHost.setVisibility(View.VISIBLE);
        activity.status.setVisibility(View.GONE);
        activity.header(false);
        ScrollView scroll = new ScrollView(activity.ui.context);
        LinearLayout content = activity.column();
        content.setPadding(activity.dp(16), activity.dp(12), activity.dp(16), activity.dp(24));
        scroll.addView(content);
        activity.nativeHost.addView(scroll, new FrameLayout.LayoutParams(-1, -1));
        if (saved().isEmpty()) {
            activity.add(activity.text(activity.L("连接你的电脑"), 24), content, 8);
            activity.add(activity.text(activity.L("在 Windows Dock 的“手机连接”中启动服务并生成配对码。"), 16),
                    content, 20);
            activity.add(activity.button(activity.L("扫码连接电脑"), this::scan), content, 8);
            activity.add(activity.button(activity.L("输入网关地址"), this::manualAddress), content, 16);
        } else {
            activity.add(activity.button(activity.L("刷新状态"), () -> {
                checkComputers();
                MonitorService.refresh();
            }), content, 12);
            for (String entry : recentComputers()) {
                LinearLayout computer = activity.card(), heading = new LinearLayout(activity.ui.context);
                heading.setGravity(Gravity.CENTER_VERTICAL);
                TextView reachability = activity.text("", 13);
                reachability.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
                homeStates.put(entry, reachability);
                renderReachability(entry, reachability);
                TextView name = activity.text(computerName(entry)
                                + (entry.equals(activity.chat.origin) && activity.chat.web != null
                                                ? " · " + activity.L("当前连接")
                                                : ""),
                        17);
                name.setTypeface(null, android.graphics.Typeface.BOLD);
                heading.addView(name, new LinearLayout.LayoutParams(0, -2, 1));
                heading.addView(activity.icon("ic_more", activity.L("电脑详情"), () -> computerDetails(entry)),
                        new LinearLayout.LayoutParams(activity.dp(48), activity.dp(48)));
                activity.add(heading, computer, 0);
                activity.add(reachability, computer, 0);
                TextView address = activity.text(entry, 13);
                address.setTextColor(activity.mutedColor());
                activity.add(address, computer, 0);
                LinearLayout apps = new LinearLayout(activity.ui.context);
                homeNotifications.put(entry, apps);
                activity.add(apps, computer, 0);
                computer.setOnClickListener(v -> activity.chat.connect(resumeUrl(entry)));
                computer.setContentDescription(activity.L("连接 ") + computerName(entry));
                activity.add(computer, content, 12);
            }
        }
        activity.renderTabs();
        activity.updateBars(activity.pageColor(), activity.surfaceColor());
        renderComputerNotifications();
        checkComputers();
    }

    List<String> recentComputers() { return activity.computers.recentAddresses(); }

    String computerName(String address) { return activity.computers.name(address, Uri.parse(address).getHost()); }

    void renameComputer(String address) {
        EditText field = new EditText(activity.ui.context);
        field.setSingleLine();
        field.setFilters(new android.text.InputFilter[] {new android.text.InputFilter.LengthFilter(80)});
        field.setText(computerName(address));
        field.selectAll();
        field.setPadding(activity.dp(24), activity.dp(16), activity.dp(24), activity.dp(16));
        new AlertDialog.Builder(activity.ui.context)
                .setTitle(activity.L("重命名电脑"))
                .setMessage(address + "\n" + activity.L("留空恢复默认名称"))
                .setView(field)
                .setNegativeButton(activity.L("取消"), null)
                .setPositiveButton(activity.L("保存"),
                        (d, w) -> {
                            activity.computers.rename(address, field.getText().toString().trim());
                            home();
                        })
                .show();
    }

    void renderComputerNotifications() {
        if (!activity.resumed || !"computers".equals(activity.selectedPage))
            return;
        for (Map.Entry<String, LinearLayout> entry : homeNotifications.entrySet()) {
            LinearLayout apps = entry.getValue();
            apps.removeAllViews();
            JSONArray rows = ComputerNotifications.clients(activity, entry.getKey());
            for (int i = 0; i < rows.length(); i++) {
                JSONObject row = rows.optJSONObject(i);
                if (row == null || !row.optBoolean("enabled"))
                    continue;
                String provider = row.optString("id");
                if (!"codex".equals(provider))
                    continue;
                String name = "Codex";
                long count = Math.max(0, row.optLong("unread"));
                FrameLayout slot = new FrameLayout(activity);
                ImageButton app = new ImageButton(activity.ui.context);
                app.setBackgroundColor(android.graphics.Color.TRANSPARENT);
                app.setScaleType(ImageView.ScaleType.FIT_CENTER);
                app.setPadding(activity.dp(11), activity.dp(11), activity.dp(11), activity.dp(11));
                if (!clientIcons.containsKey(provider))
                    try (java.io.InputStream input = activity.getAssets().open(provider + ".png")) {
                        clientIcons.put(provider, android.graphics.BitmapFactory.decodeStream(input));
                    } catch (java.io.IOException ignored) {
                    }
                app.setImageBitmap(clientIcons.get(provider));
                app.setContentDescription(name
                        + (row.has("connected") && !row.isNull("connected") && !row.optBoolean("connected")
                                        ? " · " + activity.L("尚未连接")
                                        : "")
                        + " · " + activity.L("未读通知") + " " + count);
                app.setOnClickListener(v -> activity.chat.connect(entry.getKey() + "/?client=" + provider));
                slot.addView(app, new FrameLayout.LayoutParams(activity.dp(48), activity.dp(48)));
                if (count > 0) {
                    TextView badge = activity.text(count > 99 ? "99+" : String.valueOf(count), 10);
                    badge.setPadding(activity.dp(4), 0, activity.dp(4), 0);
                    badge.setMinWidth(activity.dp(18));
                    badge.setGravity(Gravity.CENTER);
                    badge.setTextColor(0xffffffff);
                    badge.setBackground(activity.background(0xffcf3939, 10));
                    badge.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
                    slot.addView(
                            badge, new FrameLayout.LayoutParams(-2, activity.dp(18), Gravity.TOP | Gravity.START));
                }
                slot.setAlpha(ComputerNotifications.fresh(entry.getKey())
                                        && (!row.has("connected") || row.isNull("connected")
                                                || row.optBoolean("connected"))
                                ? 1f
                                : .5f);
                apps.addView(slot, new LinearLayout.LayoutParams(activity.dp(48), activity.dp(48)));
            }
        }
    }

    void stopComputerChecks() {
        probeGeneration++;
        activity.homeHandler.removeCallbacks(homeRefresh);
        for (GatewayProbe probe : homeProbes)
            probe.cancel();
        homeProbes.clear();
        for (Future<?> task : probeTasks)
            task.cancel(true);
        probeTasks.clear();
    }

    private static final class ComputerReachability {
        final boolean online;
        final long checkedAt;
        ComputerReachability(boolean online, long checkedAt) {
            this.online = online;
            this.checkedAt = checkedAt;
        }
    }

    private String timeSinceCheck(long checkedAt) {
        long minutes = Math.max(0, (SystemClock.elapsedRealtime() - checkedAt) / 60000);
        if (minutes == 0)
            return activity.L("刚刚");
        if (minutes == 1)
            return activity.L("1 分钟前");
        if (minutes < 60)
            return String.format(Locale.ROOT, activity.L("%d 分钟前"), minutes);
        long hours = minutes / 60;
        if (hours == 1)
            return activity.L("1 小时前");
        if (hours < 24)
            return String.format(Locale.ROOT, activity.L("%d 小时前"), hours);
        long days = hours / 24;
        return days == 1 ? activity.L("1 天前") : String.format(Locale.ROOT, activity.L("%d 天前"), days);
    }

    private void renderReachability(String address, TextView label) {
        ComputerReachability result = lastReachability.get(address);
        String value = result == null
                ? activity.L("检查中…")
                : (result.online ? "● " : "○ ") + activity.L(result.online ? "在线" : "暂不可达") + " · "
                        + timeSinceCheck(result.checkedAt);
        if (!value.contentEquals(label.getText()))
            label.setText(value);
        label.setTextColor(result != null && result.online ? activity.ui.accent : activity.mutedColor());
    }

    void checkComputers() {
        stopComputerChecks();
        if (!activity.resumed || !"computers".equals(activity.selectedPage) || homeStates.isEmpty())
            return;
        final int ticket = probeGeneration;
        for (Map.Entry<String, TextView> entry : homeStates.entrySet()) {
            String address = entry.getKey();
            renderReachability(address, entry.getValue());
            GatewayProbe probe = new GatewayProbe();
            homeProbes.add(probe);
            probeTasks.add(probes.submit(() -> {
                boolean online = probe.check(address);
                activity.runOnUiThread(() -> {
                    if (ticket != probeGeneration || !activity.resumed
                            || !"computers".equals(activity.selectedPage) || !saved().contains(address))
                        return;
                    lastReachability.put(address, new ComputerReachability(online, SystemClock.elapsedRealtime()));
                    renderReachability(address, entry.getValue());
                });
            }));
        }
        activity.homeHandler.postDelayed(homeRefresh, 30000);
    }

    void manualAddress() {
        activity.resetExitGesture();
        EditText field = new EditText(activity.ui.context);
        field.setSingleLine();
        field.setInputType(17);
        field.setText("https://");
        field.setSelection(field.length());
        field.setPadding(activity.dp(24), activity.dp(16), activity.dp(24), activity.dp(16));
        new AlertDialog.Builder(activity.ui.context)
                .setTitle(activity.L("连接电脑"))
                .setMessage(activity.L("输入 Windows Dock 手机连接页中的网关地址（HTTPS 或局域网 HTTP）"))
                .setView(field)
                .setPositiveButton(activity.L("继续"), (d, w) -> choose(field.getText().toString()))
                .setNegativeButton(activity.L("取消"), null)
                .show();
    }

    void choose(String value) {
        activity.resetExitGesture();
        try {
            String target = GatewayURL.connection(value);
            String base = GatewayURL.origin(value);
            new AlertDialog.Builder(activity.ui.context)
                    .setTitle(activity.L("连接到这台电脑？"))
                    .setMessage(base + activity.L("\n请确认这是你自己的网关。"))
                    .setPositiveButton(activity.L("连接"), (d, w) -> activity.chat.connect(target))
                    .setNegativeButton(activity.L("取消"), null)
                    .show();
        } catch (Exception e) {
            activity.message(activity.L("请输入 Codex Dock 的完整网关地址，或从 Windows Dock 手机连接页扫码。"));
        }
    }

    void confirmRemove() { confirmRemove(activity.chat.origin); }

    void confirmRemove(String target) {
        new AlertDialog.Builder(activity.ui.context)
                .setTitle(activity.L("移除这台电脑？"))
                .setMessage(target + "\n\n" + activity.L("清除本机保存的连接和登录状态，电脑上的聊天不受影响。"))
                .setNegativeButton(activity.L("取消"), null)
                .setPositiveButton(activity.L("移除"),
                        (d, w) -> {
                            if (activity.downloads.downloadTask != null
                                    && activity.downloads.downloadTask.origin.equals(target))
                                activity.downloads.cancelDownload();
                            final int ticket = activity.chat.generation;
                            activity.worker.execute(() -> {
                                try {
                                    JSONObject auth = EventClient.request(target, "/api/auth", null, null);
                                    if (auth.optBoolean("authenticated"))
                                        EventClient.request(
                                                target, "/api/logout", new JSONObject(), auth.getString("csrf"));
                                } catch (Exception ignored) { /* Offline removal is local; server session expiry
                                                                 still applies. */
                                }
                                DeviceBinding.clear(activity, target);
                                activity.runOnUiThread(() -> {
                                    activity.computers.remove(target);
                                    lastReachability.remove(target);
                                    EventClient.forget(target);
                                    MonitorService.bindingsChanged();
                                    if (target.equals(activity.chat.origin)) {
                                        activity.chat.clear();
                                        activity.chat.origin = "";
                                    }
                                    if (ticket == activity.chat.generation || target.equals(activity.chat.origin)
                                            || activity.chat.web == null)
                                        home();
                                });
                            });
                        })
                .show();
    }

    void scan() {
        if (activity.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            activity.requestPermissions(new String[] {Manifest.permission.CAMERA}, 21);
            return;
        }
        activity.startActivityForResult(new Intent(activity, ScanActivity.class), 20);
    }
}
