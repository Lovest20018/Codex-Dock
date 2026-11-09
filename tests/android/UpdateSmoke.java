package io.github.lovest20018.codexdock.test;

import android.app.*;
import android.content.*;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.lang.reflect.*;
import java.net.*;
import java.security.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.*;
import org.json.*;

/** Network fixtures and install interception exist only in this separate test APK. */
public final class UpdateSmoke extends Instrumentation {
 private Activity activity;private final JSONArray checks=new JSONArray();private byte[] apk;private String digest;private volatile String mode="new";private volatile int requests;
 private final String current="0.2.4",next="0.2.5";private Class<?> releaseType,updatesType;
 private static final String PACKAGE="io.github.lovest20018.codexdock";
 @Override public void onCreate(Bundle args){start();}
 private void ui(Runnable action){runOnMainSync(action);}
 private void check(boolean value,String message){if(!value)throw new AssertionError(message);checks.put(message);android.util.Log.i("DockUpdateQA",message);}
 private void until(BooleanSupplier condition,String message)throws Exception{long end=SystemClock.elapsedRealtime()+25000;while(SystemClock.elapsedRealtime()<end){if(condition.getAsBoolean())return;Thread.sleep(100);}throw new AssertionError(message);}
 private Object field(String name){try{Field f=activity.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(activity);}catch(Exception e){throw new RuntimeException(e);}}
 private Object call(String name,Class<?>[] types,Object...args){try{Method m=activity.getClass().getDeclaredMethod(name,types);m.setAccessible(true);return m.invoke(activity,args);}catch(Exception e){throw new RuntimeException(e);}}
 private void call(String name){call(name,new Class<?>[0]);}
 private Object release()throws Exception{return releaseType.getConstructor(String.class,String.class,long.class,String.class).newInstance(next,"Fixture Android release",(long)apk.length,digest);}
 private JSONArray rows(String version)throws Exception{
  return new JSONArray().put(new JSONObject().put("tag_name","v99.0.0").put("draft",false).put("prerelease",false).put("assets",new JSONArray().put(new JSONObject().put("name","CodexDock-99.0.0-Setup-win-x64.exe"))))
   .put(new JSONObject().put("tag_name","android-v"+version).put("draft",false).put("prerelease",false).put("body","Fixture Android release").put("assets",new JSONArray().put(new JSONObject().put("name","LovestCode-Android-"+version+".apk").put("browser_download_url","https://github.com/Lovest20018/Codex-Dock/releases/download/android-v"+version+"/LovestCode-Android-"+version+".apk").put("size",apk.length).put("digest","sha256:"+digest))));
 }
 private HttpURLConnection response(URL url)throws IOException{
  requests++;if("network".equals(mode))throw new IOException("Fixture offline");
  if(url.getHost().equals("api.github.com")){
   try{byte[] body=rows("latest".equals(mode)?current:next).toString().getBytes("UTF-8");return new MemoryResponse(url,"limited".equals(mode)?403:200,body,null);}catch(Exception e){throw new IOException(e);}
  }
  if(!url.getHost().equals("github.com")||!url.getPath().endsWith("LovestCode-Android-"+next+".apk"))throw new IOException("Unexpected test URL");
  if("redirect".equals(mode))return new MemoryResponse(url,302,new byte[0],"https://untrusted.invalid/update.apk");
  byte[] body=apk.clone();if("corrupt".equals(mode))body[body.length/2]^=1;return new MemoryResponse(url,200,body,null);
 }
 private static final class MemoryResponse extends HttpURLConnection {
  private final int status;private final byte[] bytes;private final String location;
  MemoryResponse(URL url,int status,byte[] bytes,String location){super(url);this.status=status;this.bytes=bytes;this.location=location;}
  public void connect(){}public void disconnect(){}public boolean usingProxy(){return false;}public int getResponseCode(){return status;}
  public long getContentLengthLong(){return bytes.length;}public String getHeaderField(String name){return name.equalsIgnoreCase("Location")?location:name.equalsIgnoreCase("Content-Length")?Integer.toString(bytes.length):null;}
  public InputStream getInputStream(){if(getRequestProperty("Cookie")!=null)throw new AssertionError("Gateway cookie leaked to release download");return new ByteArrayInputStream(bytes);}
 }
 private void showCheck()throws Exception{ui(()->call("showAppUpdates"));until(()->field("updateDialog")!=null,"Update dialog opens");}
 private AlertDialog dialog(){return (AlertDialog)field("updateDialog");}
 private void clickCheck(){ui(()->dialog().getButton(AlertDialog.BUTTON_POSITIVE).performClick());}
 private String dialogText(){String[] value={""};ui(()->value[0]=text(dialog().getWindow().getDecorView()));return value[0];}
 private String text(View view){StringBuilder out=new StringBuilder();if(view instanceof TextView)out.append(((TextView)view).getText()).append('\n');if(view instanceof ViewGroup)for(int i=0;i<((ViewGroup)view).getChildCount();i++)out.append(text(((ViewGroup)view).getChildAt(i)));return out.toString();}
 private String state(){Object task=field("downloadTask");if(task==null)return "NONE";try{return task.getClass().getField("state").get(task).toString();}catch(Exception e){throw new RuntimeException(e);}}
 private void cancel(){ui(()->call("cancelDownload"));}
 @Override public void onStart(){Bundle result=new Bundle();try{
  Context target=getTargetContext();releaseType=Class.forName("io.github.try2love.codexbridge.MobileRelease",true,target.getClassLoader());updatesType=Class.forName("io.github.try2love.codexbridge.AppUpdates",true,target.getClassLoader());
  try(InputStream in=getContext().getAssets().open("update-fixture.apk");ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] block=new byte[8192];int count;while((count=in.read(block))!=-1)out.write(block,0,count);apk=out.toByteArray();}
  StringBuilder hash=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(apk))hash.append(String.format("%02x",b));digest=hash.toString();
  URL.setURLStreamHandlerFactory(protocol->"https".equals(protocol)?new URLStreamHandler(){protected URLConnection openConnection(URL url)throws IOException{return response(url);}}:null);
  Method select=updatesType.getDeclaredMethod("select",JSONArray.class,String.class);select.setAccessible(true);
  Object selected=select.invoke(null,rows(next),current);check(selected!=null&&next.equals(releaseType.getField("version").get(selected)),"Android release selected while newer Windows tag is ignored");
  check(select.invoke(null,rows(current),current)==null,"Current Android release produces no update");
  JSONArray bad=rows(next);bad.getJSONObject(1).getJSONArray("assets").getJSONObject(0).remove("digest");try{select.invoke(null,bad,current);throw new AssertionError("Missing digest accepted");}catch(InvocationTargetException expected){check(expected.getCause() instanceof IOException,"Missing release digest fails visibly");}
  Method verify=updatesType.getDeclaredMethod("verifyPackage",Context.class,File.class,releaseType);verify.setAccessible(true);
  File fixture=new File(target.getCacheDir(),"fixture-update.apk");try(FileOutputStream out=new FileOutputStream(fixture)){out.write(apk);}verify.invoke(null,target,fixture,release());check(true,"Newer APK has the expected package, version and existing signing certificate");
  try{verify.invoke(null,target,new File(target.getApplicationInfo().sourceDir),release());throw new AssertionError("Current APK accepted");}catch(InvocationTargetException expected){check(expected.getCause() instanceof IOException,"Wrong APK version is rejected before installation");}
  File wrong=new File(target.getCacheDir(),"wrong-signer.apk");try(InputStream in=getContext().getAssets().open("bad-update-fixture.apk");FileOutputStream out=new FileOutputStream(wrong)){byte[] block=new byte[8192];int count;while((count=in.read(block))!=-1)out.write(block,0,count);}
  try{verify.invoke(null,target,wrong,release());throw new AssertionError("Wrong signing certificate accepted");}catch(InvocationTargetException expected){check(expected.getCause() instanceof IOException&&expected.getCause().getMessage().contains("签名"),"Different signing certificate is rejected before installation");}
  File pending=new File(target.getFilesDir(),"pending-update");File[] leftovers=pending.listFiles();if(leftovers!=null)for(File file:leftovers)file.delete();
  target.getSharedPreferences("bridge",0).edit().clear().putString("language","zh").commit();
  activity=startActivitySync(new Intent(Intent.ACTION_MAIN).setClassName(PACKAGE,"io.github.try2love.codexbridge.MainActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TASK));
  until(()->Boolean.FALSE.equals(field("restoringUpdate")),"Pending state restored");if(field("downloadTask")!=null)cancel();showCheck();check(dialogText().contains("当前版本")&&dialogText().contains(current),"Native update dialog shows installed version");
  mode="latest";clickCheck();until(()->dialogText().contains("当前已是"),"Latest state shown");check(dialog().getButton(AlertDialog.BUTTON_POSITIVE).isEnabled(),"Latest version leaves check available");
  mode="limited";clickCheck();until(()->dialogText().contains("检查失败"),"Rate-limit error shown");check(dialog().getButton(AlertDialog.BUTTON_POSITIVE).isEnabled(),"Check failure allows retry without opening a browser");
  mode="new";IntentFilter browserFilter=new IntentFilter(Intent.ACTION_VIEW);browserFilter.addDataScheme("https");ActivityMonitor browser=addMonitor(browserFilter,null,true);clickCheck();until(()->!Boolean.TRUE.equals(field("checkingUpdate")),"New release checked");check(browser.getHits()==0,"Checking newer Android release stays inside the app");
  until(()->{android.view.accessibility.AccessibilityNodeInfo root=getUiAutomation().getRootInActiveWindow();if(root==null)return false;java.util.List<android.view.accessibility.AccessibilityNodeInfo> nodes=root.findAccessibilityNodeInfosByText("稍后");return !nodes.isEmpty()&&nodes.get(0).performAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK);},"New release can be postponed");
  ui(()->dialog().dismiss());until(()->Boolean.TRUE.equals(field("resumed"))&&activity.hasWindowFocus(),"App remains in foreground");
  removeMonitor(browser);
  Object candidate=release();IntentFilter installFilter=new IntentFilter(Intent.ACTION_VIEW);installFilter.addDataScheme("content");installFilter.addDataType("application/vnd.android.package-archive");ActivityMonitor install=addMonitor(installFilter,new ActivityResult(Activity.RESULT_CANCELED,null),true);
  IntentFilter permissionFilter=new IntentFilter(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);permissionFilter.addDataScheme("package");ActivityMonitor permission=addMonitor(permissionFilter,new ActivityResult(Activity.RESULT_CANCELED,null),true);
  mode="corrupt";ui(()->call("startUpdateDownload",new Class<?>[]{releaseType},candidate));until(()->state().equals("FAILED"),"Corrupt APK fails download");check(install.getHits()==0&&permission.getHits()==0,"Corrupt download never opens permission or installer");cancel();
  mode="redirect";ui(()->call("startUpdateDownload",new Class<?>[]{releaseType},candidate));until(()->state().equals("FAILED"),"Untrusted redirect fails");check(install.getHits()==0,"Untrusted redirect never opens installer");cancel();
  mode="new";ui(()->call("startUpdateDownload",new Class<?>[]{releaseType},candidate));until(()->state().equals("COMPLETE"),"APK downloaded and verified");until(()->permission.getHits()>0||install.getHits()>0,"System handoff begins automatically");check(new File(target.getFilesDir(),"pending-update/pending.properties").isFile(),"Verified package persists before install permission");
  check(requests>=6,"Checks and download execute the production HTTP path");
  // Restore the pending file through production verification, then grant install permission externally.
  ui(()->call("resumeUpdateInstall"));check(field("updateSession")!=null,"Permission denial preserves the pending package for retry");
  ui(()->{try{for(String name:new String[]{"downloadTask","updateSession"}){Field f=activity.getClass().getDeclaredField(name);f.setAccessible(true);f.set(activity,null);}call("restoreUpdate");}catch(Exception e){throw new RuntimeException(e);}});
  until(()->Boolean.FALSE.equals(field("restoringUpdate"))&&state().equals("COMPLETE"),"Verified pending package restores");check(true,"Pending package restores from private storage and is reverified");
  try(android.os.ParcelFileDescriptor command=getUiAutomation().executeShellCommand("appops set "+PACKAGE+" REQUEST_INSTALL_PACKAGES allow");InputStream input=new FileInputStream(command.getFileDescriptor())){byte[] block=new byte[256];while(input.read(block)!=-1){}}
  ui(()->call("installDownloadedUpdate"));until(()->install.getHits()>0,"Granted permission opens installer");check(true,"Verified APK reaches the system installer after permission is granted");
  removeMonitor(install);removeMonitor(permission);
  ui(()->call("installDownloadedUpdate"));until(()->{android.view.accessibility.AccessibilityNodeInfo root=getUiAutomation().getRootInActiveWindow();return root!=null&&root.getPackageName()!=null&&root.getPackageName().toString().contains("packageinstaller");},"Actual system installer is visible");
  check(true,"Verified APK opens the real Android system installation confirmation");sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
  result.putString("report",new JSONObject().put("status","PASS").put("checks",checks).toString());finish(Activity.RESULT_OK,result);
 }catch(Throwable e){try{JSONObject report=new JSONObject().put("status","FAIL").put("error",e.toString()).put("checks",checks);if(activity!=null)report.put("resumed",field("resumed")).put("downloadState",state()).put("opening",field("openingDownload")).put("autoInstall",field("autoInstallUpdate"));result.putString("report",report.toString());}catch(Exception ignored){result.putString("report",e.toString());}finish(Activity.RESULT_CANCELED,result);}}
}
