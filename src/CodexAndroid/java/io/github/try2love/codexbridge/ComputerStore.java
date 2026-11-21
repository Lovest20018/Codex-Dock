package io.github.try2love.codexbridge;

import android.content.SharedPreferences;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** Owns saved computer preferences while keeping their keys compatible with the pinned notification helpers. */
final class ComputerStore {
    private static final String ORIGINS = "origins", ACTIVE = "active";
    private static final String CONNECTION = "connection:", NAME = "name:", LAST_OPENED = "last-opened:",
        LAST_CHAT = "last-chat:", COOKIE_NAME = "cookieName:", CLEARED = "cleared:",
        MUTED = "notificationsMuted:", SOURCE_REVISION = "notificationSourceRevision:";
    private static final String[] ADDRESS_KEYS = {
        CONNECTION, NAME, LAST_OPENED, LAST_CHAT, COOKIE_NAME, MUTED, SOURCE_REVISION,
        "computer-name:", "notificationCheckpoint:", "cursor:", "stream:", "pushRegistered:"
    };

    private final SharedPreferences prefs;

    ComputerStore(SharedPreferences prefs) {
        this.prefs = prefs;
    }

    Set<String> addresses() {
        return new LinkedHashSet<>(prefs.getStringSet(ORIGINS, Collections.emptySet()));
    }

    List<String> recentAddresses() {
        List<String> entries = new ArrayList<>(addresses());
        entries.sort((a, b) -> {
            int order = Long.compare(prefs.getLong(LAST_OPENED + b, 0), prefs.getLong(LAST_OPENED + a, 0));
            return order != 0 ? order : a.compareTo(b);
        });
        return entries;
    }

    String active() {
        return prefs.getString(ACTIVE, "");
    }

    void opened(String address, long time) {
        Set<String> saved = addresses();
        saved.add(address);
        prefs.edit().putStringSet(ORIGINS, saved).putString(ACTIVE, address)
            .putLong(LAST_OPENED + address, time).apply();
    }

    String name(String address, String fallback) {
        return prefs.getString(NAME + address, fallback);
    }

    void rename(String address, String name) {
        if (!addresses().contains(address)) return;
        prefs.edit().putString(NAME + address, name.isEmpty() ? null : name).apply();
    }

    String connection(String address) {
        return prefs.getString(CONNECTION + address, null);
    }

    String cookieName(String address) {
        return prefs.getString(COOKIE_NAME + address, null);
    }

    void setCookieName(String address, String name) {
        prefs.edit().putString(COOKIE_NAME + address, name).apply();
    }

    void rememberConnection(String address, String connection, String authenticatedCookieName) {
        SharedPreferences.Editor edit = prefs.edit().putString(CONNECTION + address, connection);
        if (authenticatedCookieName != null) edit.putString(COOKIE_NAME + address, authenticatedCookieName);
        edit.apply();
    }

    String lastChat(String address) {
        return prefs.getString(LAST_CHAT + address, "");
    }

    void setLastChat(String address, String route) {
        prefs.edit().putString(LAST_CHAT + address, route).apply();
    }

    long cleared(String address, String stream) {
        return prefs.getLong(clearedPrefix(address) + stream, 0);
    }

    void markCleared(String address, String stream, long through) {
        synchronized (EventClient.class) {
            if (!addresses().contains(address)) return;
            prefs.edit().putLong(clearedPrefix(address) + stream, Math.max(through, cleared(address, stream))).apply();
        }
    }

    void migrate(String from, String to, String connection) {
        // The receiver uses this lock when writing notification checkpoints.
        synchronized (EventClient.class) {
            Set<String> saved = addresses();
            if (from.equals(to) || !saved.remove(from)) return;
            boolean alreadySaved = !saved.add(to);
            Map<String, ?> values = prefs.getAll();
            SharedPreferences.Editor edit = prefs.edit().putStringSet(ORIGINS, saved);
            for (String prefix : ADDRESS_KEYS) {
                String previous = prefix + from;
                boolean notificationChoice = prefix.equals(MUTED) || prefix.equals(SOURCE_REVISION);
                if (!notificationChoice || !alreadySaved) {
                    String next = prefix + to;
                    Object value = values.get(previous);
                    if (value instanceof String) edit.putString(next, (String) value);
                    else if (value instanceof Long) edit.putLong(next, (Long) value);
                    else if (value instanceof Boolean) edit.putBoolean(next, (Boolean) value);
                    else if (value instanceof Integer) edit.putInt(next, (Integer) value);
                    else if (notificationChoice) edit.remove(next);
                }
                edit.remove(previous);
            }
            String prefix = clearedPrefix(from);
            for (Map.Entry<String, ?> entry : values.entrySet()) {
                String key = entry.getKey();
                if (!belongsToClearedStream(key, prefix)) continue;
                if (entry.getValue() instanceof Long) {
                    String stream = key.substring(prefix.length());
                    String next = clearedPrefix(to) + stream;
                    Object existing = values.get(next);
                    long through = (Long) entry.getValue();
                    if (existing instanceof Long) through = Math.max(through, (Long) existing);
                    edit.putLong(next, through);
                }
                edit.remove(key);
            }
            edit.putString(CONNECTION + to, connection);
            if (from.equals(active())) edit.putString(ACTIVE, to);
            edit.apply();
        }
    }

    void remove(String address) {
        synchronized (EventClient.class) {
            Set<String> saved = addresses();
            saved.remove(address);
            SharedPreferences.Editor edit = prefs.edit().putStringSet(ORIGINS, saved);
            for (String prefix : ADDRESS_KEYS) edit.remove(prefix + address);
            String prefix = clearedPrefix(address);
            for (String key : prefs.getAll().keySet()) {
                if (belongsToClearedStream(key, prefix)) edit.remove(key);
            }
            if (address.equals(active())) edit.remove(ACTIVE);
            edit.apply();
        }
    }

    private static String clearedPrefix(String address) {
        return CLEARED + address + ":";
    }

    private static boolean belongsToClearedStream(String key, String prefix) {
        // The final colon separates the gateway's hexadecimal stream ID from the full address.
        return key.startsWith(prefix) && key.lastIndexOf(':') == prefix.length() - 1;
    }
}
