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

final class NotificationInbox {
    private final MainActivity activity;

    NotificationInbox(MainActivity activity) { this.activity = activity; }

    private ServiceConnection monitor;

    void close() {
        if (monitor != null)
            activity.getApplicationContext().unbindService(monitor);
    }

    void clearInbox(String base, JSONObject data, Runnable done) {
        final int ticket = activity.chat.generation;
        final String binding = EventClient.sessionCookie(CookieManager.getInstance().getCookie(base + "/"), base);
        activity.worker.execute(() -> {
            try {
                JSONObject auth = EventClient.request(base, "/api/auth", null, null);
                if (!binding.equals(
                            EventClient.sessionCookie(CookieManager.getInstance().getCookie(base + "/"), base)))
                    return;
                JSONObject snapshot = EventClient.request(base, "/api/mobile/events/read",
                        new JSONObject()
                                .put("provider", "all")
                                .put("streamId", data.getString("streamId"))
                                .put("through", data.getLong("cursor")),
                        auth.getString("csrf"));
                activity.runOnUiThread(() -> {
                    if (ticket != activity.chat.generation || !activity.computerList.saved().contains(base)
                            || !binding.equals(EventClient.sessionCookie(
                                    CookieManager.getInstance().getCookie(base + "/"), base)))
                        return;
                    ComputerNotifications.update(base, binding, snapshot);
                    done.run();
                });
            } catch (Exception error) {
                activity.runOnUiThread(() -> {
                    if (ticket == activity.chat.generation)
                        activity.message(error.getMessage());
                });
            }
        });
    }

    void inbox() {
        if (activity.chat.origin.isEmpty()) {
            if (activity.computerList.saved().isEmpty()) {
                activity.message(activity.L("请先连接并登录电脑"));
                return;
            }
            inbox(activity.computerList.recentComputers().get(0));
        } else
            inbox(activity.chat.origin);
    }

    void inbox(String base) {
        final int ticket = activity.chat.generation;
        activity.status.setVisibility(View.VISIBLE);
        activity.status.setText(activity.L("正在读取通知…"));
        activity.worker.execute(() -> {
            try {
                JSONObject data = EventClient.read(base);
                activity.runOnUiThread(() -> {
                    if (ticket != activity.chat.generation || !activity.computerList.saved().contains(base))
                        return;
                    activity.status.setVisibility(View.GONE);
                    if (!data.optBoolean("enabled")) {
                        activity.message(activity.L("请在 Windows Dock 的手机连接设置中开启通知收件箱。"));
                        return;
                    }
                    LinearLayout content = activity.column();
                    Dialog dialog = activity.panel(
                            activity.L("通知") + " · " + activity.computerList.computerName(base), content);
                    if (activity.computerList.saved().size() > 1)
                        activity.add(activity.button(activity.L("切换电脑"), () -> {
                            dialog.dismiss();
                            List<String> entries = activity.computerList.recentComputers();
                            String[] names = new String[entries.size()];
                            for (int n = 0; n < names.length; n++)
                                names[n] = activity.computerList.computerName(entries.get(n));
                            new AlertDialog.Builder(activity.ui.context)
                                    .setTitle(activity.L("切换电脑"))
                                    .setItems(names, (d, w) -> inbox(entries.get(w)))
                                    .show();
                        }), content, 8);
                    TextView heading = activity.text(activity.L("最近任务提醒"), 22);
                    heading.setTypeface(null, android.graphics.Typeface.BOLD);
                    activity.add(heading, content, 16);
                    String stream = data.optString("streamId");
                    long cleared = activity.computers.cleared(base, stream);
                    JSONArray events = new JSONArray();
                    JSONArray all = data.optJSONArray("events");
                    if (all != null)
                        for (int n = 0; n < all.length(); n++) {
                            JSONObject e = all.optJSONObject(n);
                            if (e != null && e.optLong("sequence") > cleared)
                                events.put(e);
                        }
                    if (events.length() > 0)
                        activity.add(activity.button(activity.L("清空通知"), () -> clearInbox(base, data, () -> {
                            activity.computers.markCleared(base, stream, data.optLong("cursor"));
                            activity.computerList.renderComputerNotifications();
                            android.app.NotificationManager nm =
                                    (android.app.NotificationManager) activity.getSystemService(
                                            Context.NOTIFICATION_SERVICE);
                            Set<String> tags = new HashSet<>();
                            if (all != null)
                                for (int n = 0; n < all.length(); n++) {
                                    JSONObject e = all.optJSONObject(n);
                                    if (e != null && e.optLong("sequence") <= data.optLong("cursor"))
                                        tags.add(e.optString("id"));
                                }
                            for (android.service.notification.StatusBarNotification note :
                                    nm.getActiveNotifications()) {
                                Bundle extra = note.getNotification().extras;
                                String prefix = "bridge|" + base + "|";
                                boolean remote = false;
                                if (note.getTag() != null && note.getTag().startsWith(prefix)) {
                                    try {
                                        remote = Long.parseLong(note.getTag().substring(prefix.length()))
                                                <= data.optLong("cursor");
                                    } catch (NumberFormatException ignored) {
                                    }
                                }
                                if (remote || tags.contains(note.getTag())
                                        || (base.equals(extra.getString("bridgeOrigin"))
                                                && extra.getLong("bridgeSequence", 0) <= data.optLong("cursor")))
                                    nm.cancel(note.getTag(), note.getId());
                            }
                            dialog.dismiss();
                            inbox(base);
                        })), content, 12);
                    if (events.length() == 0) {
                        LinearLayout empty = activity.card();
                        activity.add(activity.text(activity.L("暂无通知"), 18), empty, 8);
                        activity.add(
                                activity.text(
                                        activity.L("在聊天中开启“提醒”，任务完成或需要你处理时，会记录在这里。"),
                                        14),
                                empty, 0);
                        activity.add(empty, content, 12);
                        return;
                    }
                    for (int i = events.length() - 1; i >= 0; i--) {
                        JSONObject event = events.optJSONObject(i);
                        if (event == null)
                            continue;
                        LinearLayout item = activity.card();
                        TextView title = activity.text(event.optString("title", activity.L("任务提醒")), 17);
                        title.setTypeface(null, android.graphics.Typeface.BOLD);
                        activity.add(title, item, 8);
                        if (!event.optString("body").isEmpty())
                            activity.add(activity.text(event.optString("body"), 14), item, 8);
                        if (event.has("createdAt")) {
                            TextView time = activity.text(
                                    java.text.DateFormat
                                            .getDateTimeInstance(
                                                    java.text.DateFormat.SHORT, java.text.DateFormat.SHORT)
                                            .format(new Date((long) (event.optDouble("createdAt") * 1000))),
                                    12);
                            time.setTextColor(activity.mutedColor());
                            activity.add(time, item, 8);
                        }
                        activity.add(activity.button(activity.L("查看聊天  ↗"), () -> {
                            try {
                                String target = GatewayURL.chat(
                                        base, event.getString("threadId"), event.getString("host"));
                                dialog.dismiss();
                                activity.chat.openIntent(new Intent()
                                                                 .putExtra("origin", base)
                                                                 .putExtra("thread", event.getString("threadId"))
                                                                 .putExtra("host", event.getString("host")));
                            } catch (Exception e) {
                                activity.message(activity.L("聊天链接无效"));
                            }
                        }), item, 0);
                        activity.add(item, content, 12);
                    }
                });
            } catch (Exception e) {
                activity.runOnUiThread(() -> {
                    if (ticket == activity.chat.generation) {
                        activity.status.setVisibility(View.GONE);
                        activity.message(e.getMessage());
                    }
                });
            }
        });
    }

    void toggleBackgroundNotifications() {
        if (activity.prefs.getBoolean("backgroundNotifications", false)) {
            activity.prefs.edit().putBoolean("backgroundNotifications", false).apply();
            MonitorService.refresh();
            return;
        }
        if (Build.VERSION.SDK_INT >= 33
                && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            activity.requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, 26);
            return;
        }
        if (!((NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE))
                        .areNotificationsEnabled()) {
            activity.message(activity.L("请在系统设置中允许通知。"));
            return;
        }
        activity.prefs.edit().putBoolean("alerts", true).putBoolean("backgroundNotifications", true).apply();
        monitor();
        if (!MonitorService.startBackground(activity))
            activity.message(activity.L("后台通知启动失败，请重新打开 App 后再试。"));
    }

    void requestStartupNotifications() {
        TaskNotifications.createChannel(activity, "foreground-tasks");
        // A stored false is an explicit choice (including a denied startup request).
        if (activity.prefs.contains("alerts"))
            return;
        activity.prefs.edit().putBoolean("alerts", false).apply();
        if (Build.VERSION.SDK_INT >= 33
                && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            activity.requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, 25);
            return;
        }
        activity.prefs.edit()
                .putBoolean("alerts",
                        ((NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE))
                                .areNotificationsEnabled())
                .apply();
    }

    void toggleNotifications() {
        if (activity.prefs.getBoolean("alerts", false)) {
            activity.prefs.edit().putBoolean("alerts", false).putBoolean("backgroundNotifications", false).apply();
            MonitorService.refresh();
            return;
        }
        if (Build.VERSION.SDK_INT >= 33
                && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            activity.requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, 22);
            return;
        }
        if (!((NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE))
                        .areNotificationsEnabled()) {
            activity.message(activity.L("请在系统设置中允许通知。"));
            return;
        }
        activity.prefs.edit().putBoolean("alerts", true).apply();
        monitor();
    }

    void testNotification() {
        if (Build.VERSION.SDK_INT >= 33
                && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                        != PackageManager.PERMISSION_GRANTED) {
            activity.requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, 23);
            return;
        }
        String base = activity.chat.origin;
        new Handler().postDelayed(() -> {
            try {
                TaskNotifications.show(activity, base,
                        new JSONObject()
                                .put("id", "test")
                                .put("title", activity.L("手机通知测试"))
                                .put("body", activity.L("本机通知已开启")));
            } catch (Exception ignored) {
            }
        }, 10000);
        activity.message(activity.L("10 秒后显示本地测试通知，可以先返回桌面。此测试不代表远程推送已经接通。"));
    }

    void monitor() {
        if (monitor == null) {
            monitor = new ServiceConnection() {
                public void onServiceConnected(ComponentName n, IBinder b) {
                }
                public void onServiceDisconnected(ComponentName n) {
                }
            };
            activity.getApplicationContext().bindService(
                    new Intent(activity, MonitorService.class), monitor, Context.BIND_AUTO_CREATE);
        }
    }
}
