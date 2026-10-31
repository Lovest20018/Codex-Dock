package io.github.lovest20018.codexdock.test;

import android.app.*;
import android.content.*;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.webkit.*;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.lang.reflect.*;
import java.util.concurrent.*;
import java.util.function.*;

/** Separate signed instrumentation APK: no test hooks or debugging in production. */
public final class AndroidSmoke extends Instrumentation {
 private Bundle options;private Activity activity;private String base;private final JSONArray checks=new JSONArray();
 @Override public void onCreate(Bundle args){options=args;start();}
 private void ui(Runnable action){runOnMainSync(action);}
 private Object field(String name){try{Field f=activity.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(activity);}catch(Exception e){throw new RuntimeException(e);}}
 private void field(String name,Object value){try{Field f=activity.getClass().getDeclaredField(name);f.setAccessible(true);f.set(activity,value);}catch(Exception e){throw new RuntimeException(e);}}
 private Object call(String name,Class<?>[] types,Object... values){try{Method m=activity.getClass().getDeclaredMethod(name,types);m.setAccessible(true);return m.invoke(activity,values);}catch(Exception e){throw new RuntimeException(e);}}
 private void call(String name){call(name,new Class<?>[]{});}
 private void check(boolean value,String message){if(!value)throw new AssertionError(message);checks.put(message);}
 private void until(BooleanSupplier predicate,String message)throws Exception{long end=SystemClock.elapsedRealtime()+20000;while(SystemClock.elapsedRealtime()<end){if(predicate.getAsBoolean())return;Thread.sleep(150);}throw new AssertionError(message);}
 private Object js(String script){try{CountDownLatch done=new CountDownLatch(1);String[] result={null};ui(()->((WebView)field("web")).evaluateJavascript(script,value->{result[0]=value;done.countDown();}));if(!done.await(12,TimeUnit.SECONDS))throw new AssertionError("JS timeout");return new JSONTokener(result[0]).nextValue();}catch(Exception e){throw new RuntimeException(e);}}
 private String text(View view){StringBuilder result=new StringBuilder();if(view instanceof TextView)result.append(((TextView)view).getText()).append('\n');if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++)result.append(text(((ViewGroup)view).getChildAt(n)));return result.toString();}
 private void shot(String name)throws Exception{waitForIdleSync();Thread.sleep(600);waitForIdleSync();Bitmap b=getUiAutomation().takeScreenshot();File dir=getTargetContext().getExternalFilesDir(null);if(dir==null)throw new IOException("Missing target external files directory");try(FileOutputStream out=new FileOutputStream(new File(dir,name+".png"))){b.compress(Bitmap.CompressFormat.PNG,100,out);}b.recycle();}
 @Override public void onStart(){Bundle result=new Bundle();try{
  base=options.getString("base");Context target=getTargetContext();target.getSharedPreferences("bridge",0).edit().clear().putBoolean("alerts",true).putString("language","zh").commit();
  Intent intent=new Intent(Intent.ACTION_MAIN).setClassName(target.getPackageName(),"io.github.try2love.codexbridge.MainActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK);
  activity=startActivitySync(intent);waitForIdleSync();
  String[] visible={""};ui(()->visible[0]=text(activity.getWindow().getDecorView()));
  check(visible[0].contains("Codex Dock")&&visible[0].contains("输入网关地址")&&!visible[0].contains("DSH"),"Dock home and Codex-only connection entry");shot("home-zh");
  ui(()->{target.getSharedPreferences("bridge",0).edit().putString("language","en").apply();call("home");visible[0]=text(activity.getWindow().getDecorView());});check(visible[0].contains("Your Codex")&&!visible[0].matches("(?s).*[\\u4e00-\\u9fff].*"),"English home has complete translated Dock copy");shot("home-en");ui(()->{target.getSharedPreferences("bridge",0).edit().putString("language","zh").apply();call("home");});
  ui(()->call("connect",new Class<?>[]{String.class},base+"/"));
  until(()->Boolean.TRUE.equals(js("!!window.DockUI&&!!document.getElementById('login-button')&&!document.getElementById('login').hidden&&document.getElementById('bootstrap-status').hidden")),"Production login initialized");
  js("document.getElementById('username').value='fixture';document.getElementById('password').value='isolated-android-fixture';document.getElementById('login-button').click();true");
  until(()->Boolean.TRUE.equals(js("!!document.getElementById('app')&&!document.getElementById('app').hidden&&!!window.DockUI")),"Authenticated production Dock page");
  check(Boolean.TRUE.equals(js("document.getElementById('app').getBoundingClientRect().width>0")),"Real Dock assets and password login in Android WebView");
  check(Boolean.TRUE.equals(js("prompt('codexbridge-copy:wrong-token','rejected')===null&&prompt('codexbridge-theme:wrong-token','light')===null")),"Forged native bridge tokens are rejected");
  final boolean[] blocked={false};ui(()->{WebView v=(WebView)field("web");blocked[0]=v.getWebViewClient().shouldOverrideUrlLoading(v,new WebResourceRequest(){public Uri getUrl(){return Uri.parse("https://untrusted.invalid/");}public boolean isForMainFrame(){return true;}public boolean isRedirect(){return false;}public boolean hasGesture(){return false;}public String getMethod(){return "GET";}public java.util.Map<String,String> getRequestHeaders(){return java.util.Collections.emptyMap();}});});check(blocked[0],"External navigation without a user gesture is blocked");
  js("localStorage.setItem('android-fixture-draft','retained');true");WebView original=(WebView)field("web");
  ui(()->call("settings"));waitForIdleSync();Dialog settings=(Dialog)field("settingsPage");
  ui(()->visible[0]=text(settings.getWindow().getDecorView()));
  check(visible[0].contains("当前版本")&&visible[0].contains("版本与更新")&&!visible[0].contains("网关更新")&&!visible[0].contains("DSH"),"Owned settings show Dock versions and notifications");shot("settings-light");
  ui(()->target.getSharedPreferences("bridge",0).edit().putString("settingsTheme","dark").apply());waitForIdleSync();Thread.sleep(350);shot("settings-dark");
  ui(()->((Dialog)field("settingsPage")).dismiss());waitForIdleSync();
  check(field("web")==original&&"retained".equals(js("localStorage.getItem('android-fixture-draft')")),"Settings and theme preserve WebView and draft storage");
  ui(()->activity.onConfigurationChanged(new android.content.res.Configuration(activity.getResources().getConfiguration())));waitForIdleSync();
  check(field("web")==original,"Configuration changes preserve the live WebView");shot("workbench");
  ui(()->call("gatewayMenu",new Class<?>[]{View.class},field("chatMenu")));Thread.sleep(400);shot("chat-menu");
  check(field("gatewayPopup")!=null,"Native chat menu opens over production workbench");ui(()->call("handleBack"));check(field("web")==original&&field("gatewayPopup")==null,"Back closes native menu before navigating chat");
  final boolean[] callback={false};ui(()->original.getWebChromeClient().onShowFileChooser(original,values->callback[0]=values==null,new WebChromeClient.FileChooserParams(){public int getMode(){return MODE_OPEN;}public String[] getAcceptTypes(){return new String[]{"text/plain"};}public boolean isCaptureEnabled(){return false;}public CharSequence getTitle(){return null;}public String getFilenameHint(){return null;}public Intent createIntent(){return new Intent(Intent.ACTION_OPEN_DOCUMENT);}}));
  Thread.sleep(800);shot("file-picker");sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);until(()->callback[0],"File picker cancel callback");check(field("web")==original,"System file picker cancellation preserves chat");
  final boolean[] stale={false};ui(()->{field("files",(ValueCallback<Uri[]>)(values->stale[0]=values==null));field("fileView",original);field("fileGeneration",Integer.valueOf(((Integer)field("generation"))-1));call("selectedFiles",new Class<?>[]{int.class,Intent.class},Activity.RESULT_OK,new Intent().setData(Uri.parse("content://invalid/file")));});check(stale[0],"Stale file callbacks cannot attach to a new generation");
  ui(()->call("downloadArtifact",new Class<?>[]{String.class},base+"/api/sessions/diagnostics"));
  until(()->{try{Object task=field("downloadTask");return task!=null&&task.getClass().getField("state").get(task).toString().equals("COMPLETE");}catch(Exception e){throw new RuntimeException(e);}},"Authenticated diagnostics download completes");
  check(field("downloadTask")!=null,"Native download uses authenticated production endpoint");shot("download");
  ui(()->call("home"));check(field("downloadTask")!=null&&field("web")==null,"Download survives return to computer list");ui(()->call("cancelDownload"));
  ui(()->call("connect",new Class<?>[]{String.class},base+"/"));until(()->Boolean.TRUE.equals(js("!!document.getElementById('app')&&!document.getElementById('app').hidden")),"Cookie-restored app");
  check(Boolean.TRUE.equals(js("localStorage.getItem('android-fixture-draft')==='retained'")),"Saved login and storage survive reconnect");
  check((target.getPackageManager().getApplicationInfo(target.getPackageName(),0).flags&android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE)==0,"Production package is not debuggable");
  ui(()->call("toggleBackgroundNotifications"));Thread.sleep(700);
  NotificationManager nm=(NotificationManager)target.getSystemService(Context.NOTIFICATION_SERVICE);check(nm.getActiveNotifications().length>0,"User-enabled background notification service starts");
  ui(()->call("testNotification"));Thread.sleep(10500);
  boolean test=false;for(android.service.notification.StatusBarNotification n:nm.getActiveNotifications())if("手机通知测试".equals(n.getNotification().extras.getString(Notification.EXTRA_TITLE)))test=true;
  check(test,"Native local test notification is delivered");
  ui(()->call("toggleBackgroundNotifications"));
  result.putString("report",new JSONObject().put("status","PASS").put("checks",checks).toString());finish(Activity.RESULT_OK,result);
 }catch(Throwable failure){try{shot("failure");}catch(Exception ignored){}result.putString("report","FAIL "+failure);result.putString("stack",android.util.Log.getStackTraceString(failure));finish(Activity.RESULT_CANCELED,result);}}
}
