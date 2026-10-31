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
 private String text(View view){if(view.getVisibility()!=View.VISIBLE)return "";StringBuilder result=new StringBuilder();if(view instanceof TextView)result.append(((TextView)view).getText()).append('\n');if(view instanceof ViewGroup)for(int n=0;n<((ViewGroup)view).getChildCount();n++)result.append(text(((ViewGroup)view).getChildAt(n)));return result.toString();}
 private void shot(String name)throws Exception{waitForIdleSync();Thread.sleep(600);waitForIdleSync();Bitmap b=getUiAutomation().takeScreenshot();File dir=getTargetContext().getExternalFilesDir(null);if(dir==null)throw new IOException("Missing target external files directory");try(FileOutputStream out=new FileOutputStream(new File(dir,name+".png"))){b.compress(Bitmap.CompressFormat.PNG,100,out);}b.recycle();}
 @Override public void onStart(){Bundle result=new Bundle();try{
  base=options.getString("base");Context target=getTargetContext();
  if("seed".equals(options.getString("mode"))){target.getSharedPreferences("bridge",0).edit().putString("upgrade-marker","keep").putString("settingsTheme","dark").putStringSet("origins",new java.util.HashSet<>(java.util.Arrays.asList("https://upgrade.invalid"))).commit();result.putString("report","SEEDED");finish(Activity.RESULT_OK,result);return;}
  if("upgrade".equals(options.getString("mode"))){android.content.SharedPreferences before=target.getSharedPreferences("bridge",0);check("keep".equals(before.getString("upgrade-marker",""))&&"dark".equals(before.getString("settingsTheme",""))&&before.getStringSet("origins",java.util.Collections.emptySet()).contains("https://upgrade.invalid"),"Previous version upgrade preserves preferences and computers");result.putString("report","UPGRADE_PASS");finish(Activity.RESULT_OK,result);return;}
target.getSharedPreferences("bridge",0).edit().clear().putBoolean("alerts",true).putString("language","zh").commit();
  Intent intent=new Intent(Intent.ACTION_MAIN).setClassName(target.getPackageName(),"io.github.try2love.codexbridge.MainActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK);
  activity=startActivitySync(intent);waitForIdleSync();
  String[] visible={""};ui(()->visible[0]=text(activity.getWindow().getDecorView()));
  check(visible[0].contains("Lovest Code")&&visible[0].contains("继续你的聊天")&&!visible[0].contains("DSH"),"Lovest Code home and Codex-only connection entry");shot("home-zh");
  ui(()->{target.getSharedPreferences("bridge",0).edit().putString("language","en").apply();call("home");visible[0]=text(activity.getWindow().getDecorView());});check(visible[0].contains("Connect your computer")&&!visible[0].matches("(?s).*[\\u4e00-\\u9fff].*"),"English home has complete translated Dock copy");shot("home-en");ui(()->{target.getSharedPreferences("bridge",0).edit().putString("language","zh").apply();call("home");});
  ui(()->call("connect",new Class<?>[]{String.class},base+"/"));
  until(()->Boolean.TRUE.equals(js("!!window.DockUI&&!!document.getElementById('login-button')&&!document.getElementById('login').hidden&&document.getElementById('bootstrap-status').hidden")),"Production login initialized");
  js("document.getElementById('username').value='fixture';document.getElementById('password').value='isolated-android-fixture';document.getElementById('login-button').click();true");
  until(()->Boolean.TRUE.equals(js("!!document.getElementById('app')&&!document.getElementById('app').hidden&&!!window.DockUI")),"Authenticated production Dock page");
  check(Boolean.TRUE.equals(js("document.getElementById('app').getBoundingClientRect().width>0")),"Real Dock assets and password login in Android WebView");
  check(Boolean.TRUE.equals(js("prompt('codexbridge-copy:wrong-token','rejected')===null&&prompt('codexbridge-theme:wrong-token','light')===null")),"Forged native bridge tokens are rejected");
  final boolean[] blocked={false};ui(()->{WebView v=(WebView)field("web");blocked[0]=v.getWebViewClient().shouldOverrideUrlLoading(v,new WebResourceRequest(){public Uri getUrl(){return Uri.parse("https://untrusted.invalid/");}public boolean isForMainFrame(){return true;}public boolean isRedirect(){return false;}public boolean hasGesture(){return false;}public String getMethod(){return "GET";}public java.util.Map<String,String> getRequestHeaders(){return java.util.Collections.emptyMap();}});});check(blocked[0],"External navigation without a user gesture is blocked");
  until(()->Boolean.TRUE.equals(js("window.LovestShell?.version===1&&document.documentElement.classList.contains('lovest-shell')")),"Versioned owned adapter acknowledged");
  js("localStorage.setItem('android-fixture-draft','retained');true");WebView original=(WebView)field("web");
  ui(()->call("settings"));waitForIdleSync();Dialog settings=(Dialog)field("settingsPage");
  ui(()->visible[0]=text(settings.getWindow().getDecorView()));
  check(visible[0].contains("当前版本")&&visible[0].contains("版本与更新")&&!visible[0].contains("网关更新")&&!visible[0].contains("DSH"),"Owned settings show Dock versions and notifications");shot("settings-light");
  ui(()->target.getSharedPreferences("bridge",0).edit().putString("settingsTheme","dark").apply());waitForIdleSync();Thread.sleep(350);shot("settings-dark");
  ui(()->((Dialog)field("settingsPage")).dismiss());waitForIdleSync();
  check(field("web")==original&&"retained".equals(js("localStorage.getItem('android-fixture-draft')")),"Settings and theme preserve WebView and draft storage");
  ui(()->activity.onConfigurationChanged(new android.content.res.Configuration(activity.getResources().getConfiguration())));waitForIdleSync();
  check(field("web")==original,"Configuration changes preserve the live WebView");shot("workbench");
  ui(()->call("showChat"));ui(()->call("gatewayMenu",new Class<?>[]{View.class},field("bar")));Thread.sleep(400);shot("chat-menu");
  check(field("gatewayPopup")!=null,"Native chat menu opens over production workbench");ui(()->call("handleBack"));check(field("web")==original&&field("gatewayPopup")==null,"Back closes native menu before navigating chat");
  final boolean[] callback={false};ui(()->original.getWebChromeClient().onShowFileChooser(original,values->callback[0]=values==null,new WebChromeClient.FileChooserParams(){public int getMode(){return MODE_OPEN;}public String[] getAcceptTypes(){return new String[]{"text/plain"};}public boolean isCaptureEnabled(){return false;}public CharSequence getTitle(){return null;}public String getFilenameHint(){return null;}public Intent createIntent(){return new Intent(Intent.ACTION_OPEN_DOCUMENT);}}));
  Thread.sleep(800);shot("file-picker");sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);until(()->callback[0],"File picker cancel callback");check(field("web")==original,"System file picker cancellation preserves chat");
  final boolean[] stale={false};ui(()->{field("files",(ValueCallback<Uri[]>)(values->stale[0]=values==null));field("fileView",original);field("fileGeneration",Integer.valueOf(((Integer)field("generation"))-1));call("selectedFiles",new Class<?>[]{int.class,Intent.class},Activity.RESULT_OK,new Intent().setData(Uri.parse("content://invalid/file")));});check(stale[0],"Stale file callbacks cannot attach to a new generation");
  ui(()->call("downloadArtifact",new Class<?>[]{String.class},base+"/api/sessions/diagnostics"));
  until(()->{try{Object task=field("downloadTask");return task!=null&&task.getClass().getField("state").get(task).toString().equals("COMPLETE");}catch(Exception e){throw new RuntimeException(e);}},"Authenticated diagnostics download completes");
  check(field("downloadTask")!=null,"Native download uses authenticated production endpoint");shot("download");
  ui(()->call("home"));check(field("downloadTask")!=null&&field("web")==original,"Download and live WebView survive return to computer list");ui(()->call("cancelDownload"));
  ui(()->call("connect",new Class<?>[]{String.class},base+"/"));until(()->Boolean.TRUE.equals(js("!!document.getElementById('app')&&!document.getElementById('app').hidden")),"Cookie-restored app");
  check(Boolean.TRUE.equals(js("localStorage.getItem('android-fixture-draft')==='retained'")),"Saved login and storage survive reconnect");

  ui(()->call("showMe"));waitForIdleSync();check(field("web")==original&&"me".equals(field("selectedPage")),"Me tab preserves live chat");shot("me-embedded");
  ui(()->call("home"));waitForIdleSync();check(field("web")==original&&"computers".equals(field("selectedPage")),"Computer tab preserves live chat");shot("computers");
  ui(()->call("handleBack"));check("chat".equals(field("selectedPage"))&&field("web")==original,"Back from another tab returns to chat");
  check(Boolean.TRUE.equals(js("getComputedStyle(document.querySelector('.sidebar-foot')).display==='none'")),"Owned navigation replaces duplicate Web footer");
  // Exercise the production input handler and scoped storage, without a native model runtime.
  js("window.testOriginal={id:currentId,scope:codexDraftScope,gateway:draftGateway};currentId='fixture-thread';draftGateway='fixture-gateway';codexDraftScope={gateway:'fixture-gateway',provider:'codex',account:'fixture-account',host:'local',session:'fixture-thread'};document.getElementById('message').value='real input draft';document.getElementById('message').oninput();true");
  check(Boolean.TRUE.equals(js("composerDrafts.load(codexDraftScope)?.text==='real input draft'&&!window.LovestShell.prepareLeave().draftUnsafe")),"Production input saves a recoverable scoped draft");
  ui(()->call("home"));ui(()->call("showMe"));ui(()->call("showChat"));
  check(Boolean.TRUE.equals(js("document.getElementById('message').value==='real input draft'&&composerDrafts.load(codexDraftScope).text==='real input draft'")),"Actual composition survives all three tabs");
  check(Boolean.TRUE.equals(js("composerDrafts.load({...codexDraftScope,account:'another-account'})===null")),"Drafts remain isolated between accounts");
  check(Boolean.TRUE.equals(js("attachments.drafts.set('fixture-attachment',[{id:'pending-upload',status:'uploading'}]);window.LovestShell.prepareLeave().attachmentCount===1")),"Unsent attachments are detected before destructive navigation");
  js("attachments.drafts.delete('fixture-attachment');true");
  check(Boolean.TRUE.equals(js("window.testScope=codexDraftScope;codexDraftScope=null;window.LovestShell.prepareLeave().draftUnsafe")),"Unverified account draft blocks destructive navigation");
  js("codexDraftScope=testScope;codexProvisionalDrafts.clear();currentId='12345678-1234-1234-1234-123456789abc';codexDraftScope={...codexDraftScope,session:currentId};document.getElementById('app').classList.add('chat-open');document.getElementById('chat').hidden=false;document.getElementById('welcome').hidden=true;document.getElementById('chat-title').textContent='Fixture navigation';document.getElementById('messages').replaceChildren();for(let i=0;i<30;i++){const p=document.createElement('p');p.className='message';p.textContent='Isolated navigation fixture '+i;document.getElementById('messages').append(p);}true");
  until(()->Boolean.TRUE.equals(field("chatDetail")),"Native shell observes detail navigation");
  ui(()->visible[0]=text(((LinearLayout)field("tabs"))));check(visible[0].isEmpty(),"Chat detail hides primary navigation");shot("chat-detail");
  try(InputStream input=getContext().getAssets().open("presentation.js");ByteArrayOutputStream source=new ByteArrayOutputStream()){byte[] buffer=new byte[4096];int count;while((count=input.read(buffer))!=-1)source.write(buffer,0,count);js(source.toString("UTF-8"));}
  ui(()->target.getSharedPreferences("bridge",0).edit().putString("settingsTheme","light").apply());
  js("LovestPresentationQA.list();true");shot("ui-list");
  check(Boolean.TRUE.equals(js("document.getElementById('search').getBoundingClientRect().height<=48&&document.querySelectorAll('#sessions>.session').length===5&&document.documentElement.scrollWidth<=innerWidth+2")),"Populated list keeps compact search and fits viewport");
  js("LovestPresentationQA.chat();true");shot("ui-chat");
  check(Boolean.TRUE.equals(js("document.getElementById('dock-chat-context').getBoundingClientRect().top>=document.getElementById('chat-title').getBoundingClientRect().bottom-1&&document.getElementById('composer').getBoundingClientRect().height<210")),"Chat title and path use separate lines with compact composer");
  js("document.getElementById('dock-composer-more').open=true;true");shot("ui-options");
  check(Boolean.TRUE.equals(js("document.getElementById('send').getBoundingClientRect().bottom<=innerHeight+1&&document.getElementById('composer').getBoundingClientRect().height<innerHeight*.65")),"Expanded options leave send and message reachable");
  js("document.getElementById('dock-composer-more').open=false;LovestPresentationQA.models();true");shot("ui-models");
  check(Boolean.TRUE.equals(js("(()=>{const d=document.getElementById('model-dialog').getBoundingClientRect(),b=document.getElementById('model-save').getBoundingClientRect();return d.top>24&&Math.abs(d.bottom-innerHeight)<2&&b.bottom<=innerHeight+1&&d.width<=innerWidth+1})()")),"Model settings appear as a fitted bottom sheet with visible apply action");
  ui(()->target.getSharedPreferences("bridge",0).edit().putString("settingsTheme","dark").apply());shot("ui-models-dark");
  js("document.getElementById('model-dialog').close();true");shot("ui-chat-dark");
  ui(()->{WebView v=(WebView)field("web");v.requestFocus();});js("document.getElementById('message').focus();true");
  ui(()->((android.view.inputmethod.InputMethodManager)target.getSystemService(Context.INPUT_METHOD_SERVICE)).showSoftInput((WebView)field("web"),android.view.inputmethod.InputMethodManager.SHOW_IMPLICIT));Thread.sleep(700);shot("ui-keyboard");
  check(Boolean.TRUE.equals(js("document.getElementById('send').getBoundingClientRect().bottom<=innerHeight+1&&document.getElementById('message').getBoundingClientRect().bottom<=innerHeight+1")),"Focused composer keeps input and send inside resized viewport");
  ui(()->((android.view.inputmethod.InputMethodManager)target.getSystemService(Context.INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(((WebView)field("web")).getWindowToken(),0));js("document.getElementById('message').blur();true");Thread.sleep(400);
  ui(()->{WebView v=(WebView)field("web");v.setLayoutParams(new FrameLayout.LayoutParams(Math.round(320*activity.getResources().getDisplayMetrics().density),-1));v.getSettings().setTextZoom(130);});Thread.sleep(400);
  js("document.getElementById('dock-composer-more').open=true;true");shot("ui-options-narrow");
  check(Boolean.TRUE.equals(js("document.documentElement.scrollWidth<=innerWidth+2&&document.getElementById('send').getBoundingClientRect().bottom<=innerHeight+1")),"Narrow enlarged-text composer remains on screen");
  js("document.getElementById('dock-composer-more').open=false;LovestPresentationQA.models();true");shot("ui-models-narrow");
  check(Boolean.TRUE.equals(js("document.getElementById('model-dialog').scrollWidth<=document.getElementById('model-dialog').clientWidth+2")),"Enlarged-text model settings avoid horizontal overflow");
  js("document.getElementById('model-dialog').close();LovestPresentationQA.restore();document.getElementById('messages').replaceChildren();for(let i=0;i<30;i++){const p=document.createElement('p');p.className='message';p.textContent='Isolated navigation fixture '+i;document.getElementById('messages').append(p);}true");
  ui(()->{WebView v=(WebView)field("web");v.setLayoutParams(new FrameLayout.LayoutParams(-1,-1));v.getSettings().setTextZoom(100);});Thread.sleep(400);
  js("document.getElementById('timeline').scrollTop=150;window.LovestShell.persist();true");
  check(Boolean.TRUE.equals(js("Math.abs(Number(localStorage.getItem(composerDrafts.key(codexDraftScope,'reading')))-150)<2")),"Reading position persists with verified account and chat scope");
  final String[] resume={null};ui(()->resume[0]=(String)call("resumeUrl",new Class<?>[]{String.class},base));check(resume[0].contains("12345678-1234-1234-1234-123456789abc"),"Startup locator restores last verified chat");
  ui(()->call("handleBack"));until(()->!Boolean.TRUE.equals(field("chatDetail")),"Back from detail restores list and navigation");
  js("document.getElementById('message').value='';composerDrafts.clear(codexDraftScope);currentId=testOriginal.id;codexDraftScope=testOriginal.scope;draftGateway=testOriginal.gateway;true");
  ui(()->target.getSharedPreferences("bridge",0).edit().putString("upgrade-marker","0.2.0-retained").commit());
  check((target.getPackageManager().getApplicationInfo(target.getPackageName(),0).flags&android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE)==0,"Production package is not debuggable");
  ui(()->call("toggleBackgroundNotifications"));Thread.sleep(700);
  NotificationManager nm=(NotificationManager)target.getSystemService(Context.NOTIFICATION_SERVICE);check(nm.getActiveNotifications().length>0,"User-enabled background notification service starts");
  ui(()->call("testNotification"));Thread.sleep(10500);
  boolean test=false;for(android.service.notification.StatusBarNotification n:nm.getActiveNotifications())if("手机通知测试".equals(n.getNotification().extras.getString(Notification.EXTRA_TITLE)))test=true;
  check(test,"Native local test notification is delivered");
  ui(()->call("toggleBackgroundNotifications"));
  ui(()->activity.finish());activity=startActivitySync(intent);waitForIdleSync();
  until(()->Boolean.TRUE.equals(js("window.LovestShell?.version===1")),"Adapter initializes after Activity restart");
  final String[] restarted={null};ui(()->restarted[0]=((WebView)field("web")).getUrl());check(restarted[0].contains("12345678-1234-1234-1234-123456789abc"),"Activity restart reopens saved chat locator");
  check(Boolean.TRUE.equals(js("composerDrafts.load({gateway:'fixture-gateway',provider:'codex',account:'fixture-account',host:'local',session:'fixture-thread'})?.text==='real input draft'")),"Persisted draft survives a fresh WebView");
  ui(()->activity.setRequestedOrientation(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE));
  until(()->activity.getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE,"Landscape configuration applied");Thread.sleep(400);
  check(Boolean.TRUE.equals(js("document.documentElement.scrollWidth<=innerWidth+2")),"Landscape chat layout fits the WebView");shot("landscape");
  ui(()->activity.setRequestedOrientation(android.content.pm.ActivityInfo.SCREEN_ORIENTATION_PORTRAIT));
  until(()->activity.getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_PORTRAIT,"Portrait configuration restored");Thread.sleep(400);
  ui(()->{WebView v=(WebView)field("web");v.setLayoutParams(new FrameLayout.LayoutParams(Math.round(320*activity.getResources().getDisplayMetrics().density),-1));v.getSettings().setTextZoom(130);});
  until(()->Boolean.TRUE.equals(js("innerWidth<=322")),"Narrow viewport layout applied after rotation");
  check(Boolean.TRUE.equals(js("document.documentElement.scrollWidth<=innerWidth+2")),"320dp viewport fits with enlarged WebView text "+js("JSON.stringify({width:innerWidth,scroll:document.documentElement.scrollWidth,visual:visualViewport.width,scale:visualViewport.scale})"));shot("narrow-large-text");
  ui(()->{WebView v=(WebView)field("web");v.setLayoutParams(new FrameLayout.LayoutParams(-1,-1));v.getSettings().setTextZoom(100);});
  ui(()->((WebView)field("web")).loadDataWithBaseURL(base+"/","<html><body>Offline fixture</body></html>","text/html","UTF-8",null));Thread.sleep(500);
  final boolean[] left={false};ui(()->call("beforeLeave",new Class<?>[]{Runnable.class},(Runnable)()->left[0]=true));until(()->left[0],"An unavailable page without a draft can switch computers");check(left[0],"Offline fallback does not trap the user on an unavailable page");
  result.putString("report",new JSONObject().put("status","PASS").put("checks",checks).toString());finish(Activity.RESULT_OK,result);
 }catch(Throwable failure){try{shot("failure");}catch(Exception ignored){}result.putString("report","FAIL "+failure);result.putString("stack",android.util.Log.getStackTraceString(failure));finish(Activity.RESULT_CANCELED,result);}}
}
