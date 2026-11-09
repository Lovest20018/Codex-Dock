package io.github.try2love.codexbridge;

import android.content.Context;
import android.content.pm.*;
import android.os.Build;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import org.json.*;

/** Public release metadata is read without gateway credentials or changes to saved computers. */
final class AppUpdates {
 static MobileRelease check(String current)throws Exception {
  HttpURLConnection connection=(HttpURLConnection)new URL(MobileRelease.API).openConnection();
  try{
   connection.setConnectTimeout(15000);connection.setReadTimeout(15000);connection.setInstanceFollowRedirects(false);
   connection.setRequestProperty("Accept","application/vnd.github+json");connection.setRequestProperty("User-Agent","LovestCode-Android/"+current);
   if(connection.getResponseCode()!=200)throw new IOException("检查失败，请检查网络后重试。也可以打开版本页面。");
   ByteArrayOutputStream bytes=new ByteArrayOutputStream();
   try(InputStream input=connection.getInputStream()){byte[] block=new byte[8192];int count;while((count=input.read(block))!=-1){if(Thread.currentThread().isInterrupted())throw new InterruptedIOException();if(bytes.size()+count>4*1024*1024)throw new IOException("更新信息无效");bytes.write(block,0,count);}}
   return select(new JSONArray(bytes.toString("UTF-8")),current);
  }finally{connection.disconnect();}
 }
 static MobileRelease select(JSONArray rows,String current)throws Exception {
  MobileRelease selected=null;
  for(int i=0;i<rows.length();i++){
   JSONObject row=rows.optJSONObject(i);if(row==null)continue;JSONArray assets=row.optJSONArray("assets");if(assets==null)continue;
   for(int j=0;j<assets.length();j++){
    JSONObject asset=assets.optJSONObject(j);if(asset==null||!MobileRelease.eligible(current,row.optString("tag_name"),row.optBoolean("draft",true),row.optBoolean("prerelease",true),asset.optString("name"),asset.optString("browser_download_url")))continue;
    String version=row.getString("tag_name").substring(9),notes=row.optString("body","");
    MobileRelease candidate=new MobileRelease(version,notes.substring(0,Math.min(notes.length(),12000)),asset.optLong("size",-1),asset.optString("digest",null));
    if(candidate.size<=0||candidate.size>MobileRelease.MAX_SIZE||candidate.sha256==null)throw new IOException("更新缺少有效的大小或 SHA-256 校验信息，请打开版本页面下载。");
    if(selected==null||MobileRelease.compare(candidate.version,selected.version)>0)selected=candidate;
   }
  }
  return selected;
 }
 static void verifyPackage(Context context,File file,MobileRelease release)throws Exception {
  PackageManager manager=context.getPackageManager();int flags=Build.VERSION.SDK_INT>=28?PackageManager.GET_SIGNING_CERTIFICATES:PackageManager.GET_SIGNATURES;
  PackageInfo archive=manager.getPackageArchiveInfo(file.getAbsolutePath(),flags),installed=manager.getPackageInfo(context.getPackageName(),flags);
  if(archive==null||!context.getPackageName().equals(archive.packageName)||!release.version.equals(archive.versionName)||code(archive)<=code(installed))throw new IOException("安装包身份或版本不匹配，请重新下载");
  Signature[] signatures=Build.VERSION.SDK_INT>=28&&archive.signingInfo!=null?archive.signingInfo.getApkContentsSigners():archive.signatures;
  if(signatures==null||signatures.length!=1)throw new IOException("安装包签名不匹配，请重新下载");
  byte[] hash=MessageDigest.getInstance("SHA-256").digest(signatures[0].toByteArray());StringBuilder digest=new StringBuilder();for(byte value:hash)digest.append(String.format(java.util.Locale.ROOT,"%02x",value));
  if(!"0c31e8dc7e29b7e835343db7551df5a505498959e87d4462f1b6bd9b5e72098e".equals(digest.toString()))throw new IOException("安装包签名不匹配，请重新下载");
 }
 private static long code(PackageInfo info){return Build.VERSION.SDK_INT>=28?info.getLongVersionCode():info.versionCode;}
}
