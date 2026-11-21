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

final class UpdateController {
    private final MainActivity activity;

    UpdateController(MainActivity activity) { this.activity = activity; }

    UpdateInstallSession updateSession;

    boolean restoringUpdate;

    boolean autoInstallUpdate;

    private boolean checkingUpdate;

    private AlertDialog updateDialog;

    private final ExecutorService updateChecks = Executors.newSingleThreadExecutor();

    void close() {
        if (updateDialog != null)
            updateDialog.dismiss();
        updateChecks.shutdownNow();
    }

    void openDockReleases() { openUpdateUrl(MobileRelease.REPO + "/releases?q=android-v"); }

    void openUpdateUrl(String url) {
        try {
            activity.startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
        } catch (ActivityNotFoundException e) {
            activity.message(activity.L("没有可用的浏览器，请在电脑上打开 Lovest Code 发布页。"));
        }
    }

    void showAppUpdates() {
        if (updateDialog != null && updateDialog.isShowing())
            return;
        LinearLayout content = activity.column();
        content.setPadding(activity.dp(24), activity.dp(8), activity.dp(24), activity.dp(8));
        TextView detail = activity.text(activity.L("当前版本") + " " + activity.appVersion() + "\n\n"
                        + activity.L("手动检查更新，确认后下载并安装"),
                15);
        activity.add(detail, content, 0);
        AlertDialog dialog = new AlertDialog.Builder(activity.ui.context)
                                     .setTitle(activity.L("应用更新"))
                                     .setView(content)
                                     .setNegativeButton(activity.L("关闭"), null)
                                     .setNeutralButton(activity.L("版本页面"), (d, w) -> openDockReleases())
                                     .setPositiveButton(activity.L("检查更新"), null)
                                     .create();
        updateDialog = dialog;
        dialog.setOnDismissListener(d -> {
            if (updateDialog == dialog)
                updateDialog = null;
        });
        dialog.show();
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> checkUpdate(dialog, detail));
    }

    void checkUpdate(AlertDialog dialog, TextView detail) {
        if (checkingUpdate)
            return;
        checkingUpdate = true;
        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);
        detail.setText(activity.L("正在检查更新…"));
        updateChecks.execute(() -> {
            MobileRelease candidate = null;
            String failure = null;
            try {
                candidate = AppUpdates.check(activity.appVersion());
            } catch (Exception e) {
                failure = e.getMessage() == null ? "检查失败，请检查网络后重试。也可以打开版本页面。"
                                                 : e.getMessage();
            }
            final MobileRelease release = candidate;
            final String error = failure;
            activity.runOnUiThread(() -> {
                checkingUpdate = false;
                if (activity.isFinishing() || activity.isDestroyed() || !dialog.isShowing())
                    return;
                dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);
                if (error != null) {
                    detail.setText(activity.L(error));
                    return;
                }
                if (release == null) {
                    detail.setText(activity.L("当前版本") + " " + activity.appVersion() + "\n\n"
                            + activity.L("当前已是此通道最新版本。"));
                    return;
                }
                detail.setText(activity.L("发现新版本") + " " + release.version);
                new AlertDialog.Builder(activity.ui.context)
                        .setTitle(activity.L("发现新版本") + " " + release.version)
                        .setMessage(activity.L("App 会在本机下载并打开系统安装器，已保存的电脑与登录状态会保留。")
                                + "\n\n" + release.notes)
                        .setNegativeButton(activity.L("稍后"), null)
                        .setNeutralButton(activity.L("更新说明"), (d, w) -> openUpdateUrl(release.page()))
                        .setPositiveButton(activity.L("下载并安装"),
                                (d, w) -> {
                                    if (startUpdateDownload(release))
                                        dialog.dismiss();
                                })
                        .show();
            });
        });
    }

    java.io.File updateDirectory() { return new java.io.File(activity.getFilesDir(), "pending-update"); }

    void updateProblem(String text, MobileRelease release) {
        new AlertDialog.Builder(activity.ui.context)
                .setMessage(activity.L(text))
                .setNegativeButton(activity.L("关闭"), null)
                .setPositiveButton(activity.L("版本页面"), (d, w) -> openUpdateUrl(release.page()))
                .show();
    }

    boolean startUpdateDownload(MobileRelease release) {
        if (restoringUpdate) {
            activity.message(activity.L("正在恢复更新，请稍候"));
            return false;
        }
        if (activity.downloads.downloadTask != null) {
            activity.downloads.showTask();
            activity.downloads.renderDownload();
            activity.message(activity.L("已有下载任务，请先完成或取消"));
            return false;
        }
        try {
            java.io.File directory = updateDirectory();
            if (!directory.isDirectory() && !directory.mkdirs())
                throw new java.io.IOException("无法保存待安装更新，请重新下载");
            activity.downloads.downloadTask = new ArtifactDownload.Task(release.download, MobileRelease.REPO,
                    directory, release.size, release.sha256, MobileRelease.asset(release.version));
            updateSession = new UpdateInstallSession(release, activity.downloads.downloadTask, directory);
            activity.downloads.showTask();
            activity.downloads.renderDownload();
            activity.downloads.runDownload(activity.downloads.downloadTask);
            return true;
        } catch (java.io.IOException e) {
            updateSession = null;
            activity.downloads.downloadTask = null;
            updateProblem(e.getMessage(), release);
            return false;
        }
    }

    void restoreUpdate() {
        restoringUpdate = true;
        final String current = activity.appVersion();
        activity.downloads.executor.execute(() -> {
            UpdateInstallSession saved = null;
            boolean invalid = false;
            try {
                saved = UpdateInstallSession.restore(updateDirectory(), current);
            } catch (java.io.InterruptedIOException e) {
                return;
            } catch (java.io.IOException e) {
                if (Thread.currentThread().isInterrupted())
                    return;
                invalid = true;
                UpdateInstallSession.discard(updateDirectory());
            }
            final UpdateInstallSession recovered = saved;
            final boolean failed = invalid;
            activity.runOnUiThread(() -> {
                if (activity.isFinishing() || activity.isDestroyed())
                    return;
                restoringUpdate = false;
                if (recovered != null) {
                    updateSession = recovered;
                    activity.downloads.downloadTask = recovered.task;
                    activity.downloads.showTask();
                    activity.downloads.renderDownload();
                } else if (failed)
                    activity.message(activity.L("待安装更新已失效，请重新检查更新"));
                resumeUpdateInstall();
            });
        });
    }

    void resumeUpdateInstall() {
        if (!activity.resumed || updateSession == null || activity.downloads.savingDownload
                || activity.downloads.choosingDownload != null)
            return;
        if (updateSession.waitingPermission()) {
            try {
                updateSession.permissionReturned();
            } catch (java.io.IOException e) {
                updateProblem("无法保存待安装更新，请重新下载", updateSession.release);
                return;
            }
            if (!activity.getPackageManager().canRequestPackageInstalls()) {
                updateProblem("请在系统设置中允许此应用安装未知来源应用。", updateSession.release);
                return;
            }
            installDownloadedUpdate();
        } else if (autoInstallUpdate) {
            autoInstallUpdate = false;
            installDownloadedUpdate();
        }
    }

    void installDownloadedUpdate() {
        final ArtifactDownload.Task task = activity.downloads.downloadTask;
        final UpdateInstallSession session = updateSession;
        if (!activity.resumed || task == null || session == null || task.state != ArtifactDownload.State.COMPLETE
                || activity.downloads.openingDownload || activity.downloads.savingDownload
                || activity.downloads.choosingDownload != null || !session.beginInstall())
            return;
        activity.downloads.openingDownload = true;
        activity.downloads.renderDownload();
        activity.downloads.executor.execute(() -> {
            Uri uri = null;
            String failure = null;
            try {
                task.verifyCompletedUpdate();
                AppUpdates.verifyPackage(activity, task.result().file, session.release);
                uri = DownloadProvider.prepare(activity, task.result());
            } catch (Exception e) {
                failure = e.getMessage() == null ? "安装包准备失败，请重新下载" : e.getMessage();
            }
            final Uri target = uri;
            final String error = failure;
            activity.runOnUiThread(() -> {
                activity.downloads.openingDownload = false;
                session.finishInstall();
                if (activity.isFinishing() || activity.isDestroyed() || task != activity.downloads.downloadTask
                        || session != updateSession)
                    return;
                activity.downloads.renderDownload();
                if (error != null) {
                    updateProblem(error, session.release);
                    return;
                }
                if (!activity.resumed)
                    return;
                if (!activity.getPackageManager().canRequestPackageInstalls()) {
                    try {
                        session.awaitPermission();
                        activity.startActivity(
                                new Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                                        Uri.parse("package:" + activity.getPackageName())));
                    } catch (Exception e) {
                        try {
                            session.permissionReturned();
                        } catch (java.io.IOException ignored) {
                        }
                        updateProblem("无法打开安装权限设置，请在系统设置中允许此应用安装未知来源应用。",
                                session.release);
                    }
                    return;
                }
                Intent install = new Intent(Intent.ACTION_VIEW)
                                         .setDataAndType(target, "application/vnd.android.package-archive")
                                         .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                install.setClipData(ClipData.newRawUri("Lovest Code", target));
                try {
                    activity.startActivity(install);
                } catch (ActivityNotFoundException e) {
                    updateProblem("系统没有可用的安装器，请保存 APK 后手动安装", session.release);
                } catch (Exception e) {
                    updateProblem("打开安装器失败，请重试", session.release);
                }
            });
        });
    }
}
