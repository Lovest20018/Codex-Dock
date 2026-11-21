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

final class DownloadController {
    private final MainActivity activity;

    DownloadController(MainActivity activity) { this.activity = activity; }

    final ExecutorService executor = Executors.newSingleThreadExecutor();

    private DownloadCard downloadCard;

    ArtifactDownload.Task downloadTask;

    ArtifactDownload.Task choosingDownload;

    boolean savingDownload;

    boolean openingDownload;

    void attach(FrameLayout screen) {
        downloadCard =
                new DownloadCard(activity, screen, this::downloadAction, this::openDownload, this::cancelDownload);
    }

    void requestPlacement() {
        if (downloadCard != null)
            downloadCard.post(downloadCard::place);
    }

    void showTask() { downloadCard.showTask(); }

    void close() {
        if (downloadTask != null
                && (activity.updates.updateSession == null
                        || downloadTask.state != ArtifactDownload.State.COMPLETE))
            downloadTask.cancel();
        executor.shutdownNow();
    }

    void renderDownload() {
        downloadCard.render(downloadTask, savingDownload || choosingDownload != null, openingDownload,
                activity.updates.updateSession != null);
    }

    void downloadArtifact(String target) {
        if (downloadTask != null) {
            downloadCard.showTask();
            renderDownload();
            activity.message(activity.L("已有下载任务，请先完成或取消"));
            return;
        }
        if (activity.chat.web == null || !GatewayURL.sameOrigin(activity.chat.web.getUrl(), activity.chat.origin)
                || !GatewayURL.sameOrigin(target, activity.chat.origin)
                || !ArtifactDownload.accepts(target, activity.chat.origin)) {
            activity.message(activity.L("此下载不是当前电脑的附件，请在浏览器中打开"));
            return;
        }
        startDownload(target, activity.chat.origin);
    }

    void startDownload(String target, String base) {
        if (activity.updates.restoringUpdate) {
            activity.message(activity.L("正在恢复更新，请稍候"));
            return;
        }
        try {
            downloadTask = new ArtifactDownload.Task(target, base, activity.getCacheDir());
            downloadCard.showTask();
            renderDownload();
            runDownload(downloadTask);
        } catch (java.io.IOException e) {
            activity.message(e.getMessage());
        }
    }

    void runDownload(ArtifactDownload.Task task) {
        final UpdateInstallSession session = activity.updates.updateSession;
        task.state = ArtifactDownload.State.READY;
        renderDownload();
        executor.execute(() -> task.run(() -> CookieManager.getInstance().getCookie(task.url), value -> {
            boolean complete = value.state == ArtifactDownload.State.COMPLETE;
            if (complete && session != null)
                try {
                    session.save();
                } catch (java.io.IOException e) {
                    complete = false;
                    if (value.state != ArtifactDownload.State.CANCELLED) {
                        value.state = ArtifactDownload.State.FAILED;
                        value.restartRequired = true;
                        value.error = "无法保存待安装更新，请重新下载";
                    }
                }
            final boolean finished = complete;
            activity.runOnUiThread(() -> {
                if (downloadTask != value || activity.isFinishing() || activity.isDestroyed())
                    return;
                renderDownload();
                if (finished && session != null && session == activity.updates.updateSession
                        && session.completed()) {
                    activity.updates.autoInstallUpdate = true;
                    activity.updates.resumeUpdateInstall();
                }
            });
        }));
    }

    void downloadAction() {
        ArtifactDownload.Task task = downloadTask;
        if (task == null || savingDownload || choosingDownload != null || openingDownload)
            return;
        if (task.state == ArtifactDownload.State.RUNNING) {
            task.pause();
            renderDownload();
        } else if (task.state == ArtifactDownload.State.PAUSED || task.state == ArtifactDownload.State.FAILED) {
            if (task.resumable()) {
                runDownload(task);
            } else {
                MobileRelease release =
                        activity.updates.updateSession == null ? null : activity.updates.updateSession.release;
                String target = task.url, base = task.origin;
                cancelDownload();
                if (release != null)
                    activity.updates.startUpdateDownload(release);
                else
                    startDownload(target, base);
            }
        } else if (task.state == ArtifactDownload.State.COMPLETE) {
            Intent save = new Intent(Intent.ACTION_CREATE_DOCUMENT)
                                  .addCategory(Intent.CATEGORY_OPENABLE)
                                  .setType("application/octet-stream")
                                  .putExtra(Intent.EXTRA_TITLE, task.name);
            try {
                choosingDownload = task;
                renderDownload();
                activity.startActivityForResult(save, 32);
            } catch (Exception e) {
                choosingDownload = null;
                renderDownload();
                activity.message(activity.L("没有可用的文件保存器"));
            }
        }
    }

    void openDownload() {
        if (activity.updates.updateSession != null) {
            if (downloadTask != null && downloadTask.state == ArtifactDownload.State.FAILED)
                activity.updates.openDockReleases();
            else
                activity.updates.installDownloadedUpdate();
            return;
        }
        ArtifactDownload.Task task = downloadTask;
        if (task == null || task.state != ArtifactDownload.State.COMPLETE || savingDownload
                || choosingDownload != null || openingDownload)
            return;
        openingDownload = true;
        renderDownload();
        executor.execute(() -> {
            Uri uri = null;
            try {
                uri = DownloadProvider.prepare(activity, task.result());
            } catch (java.io.IOException ignored) {
            }
            final Uri target = uri;
            activity.runOnUiThread(() -> {
                openingDownload = false;
                if (activity.isFinishing() || activity.isDestroyed())
                    return;
                renderDownload();
                if (task != downloadTask)
                    return;
                if (target == null) {
                    activity.message(activity.L("打开失败，请重试或保存文件"));
                    return;
                }
                Intent view = new Intent(Intent.ACTION_VIEW)
                                      .setDataAndType(target, activity.getContentResolver().getType(target))
                                      .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                view.setClipData(ClipData.newRawUri("", target));
                try {
                    activity.startActivity(view);
                } catch (ActivityNotFoundException e) {
                    activity.message(activity.L("暂无可打开此文件的应用"));
                } catch (Exception e) {
                    activity.message(activity.L("打开失败，请重试或保存文件"));
                }
            });
        });
    }

    void cancelDownload() {
        activity.updates.autoInstallUpdate = false;
        if (activity.updates.updateSession != null)
            activity.updates.updateSession.cancel();
        else if (downloadTask != null)
            downloadTask.cancel();
        activity.updates.updateSession = null;
        downloadTask = null;
        renderDownload();
    }

    void saveDownload(int result, Intent data) {
        ArtifactDownload.Task task = choosingDownload;
        choosingDownload = null;
        if (result != Activity.RESULT_OK || data == null || data.getData() == null) {
            renderDownload();
            return;
        }
        if (task == null) {
            renderDownload();
            activity.message(activity.L("保存失败，请重试"));
            return;
        }
        Uri target = data.getData();
        savingDownload = true;
        renderDownload();
        executor.execute(() -> {
            boolean ok = false;
            try {
                if (task == null || task.state != ArtifactDownload.State.COMPLETE)
                    throw new java.io.IOException();
                ArtifactDownload.Result download = task.result();
                try (java.io.InputStream in = new java.io.FileInputStream(download.file);
                        java.io.OutputStream out = activity.getContentResolver().openOutputStream(target, "w")) {
                    if (out == null)
                        throw new java.io.IOException();
                    byte[] buffer = new byte[16384];
                    int count;
                    while ((count = in.read(buffer)) != -1) {
                        if (task.state != ArtifactDownload.State.COMPLETE)
                            throw new java.io.IOException();
                        out.write(buffer, 0, count);
                    }
                }
                if (task.state != ArtifactDownload.State.COMPLETE)
                    throw new java.io.IOException();
                ok = true;
            } catch (Exception e) {
                try {
                    android.provider.DocumentsContract.deleteDocument(activity.getContentResolver(), target);
                } catch (Exception ignored) {
                }
            }
            final boolean saved = ok;
            activity.runOnUiThread(() -> {
                savingDownload = false;
                if (activity.isFinishing() || activity.isDestroyed())
                    return;
                if (task == downloadTask) {
                    if (saved)
                        cancelDownload();
                    else
                        renderDownload();
                    Toast.makeText(activity, activity.L(saved ? "文件已保存" : "保存失败，请重试"),
                                 Toast.LENGTH_LONG)
                            .show();
                } else
                    renderDownload();
            });
        });
    }
}
