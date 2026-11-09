package io.github.try2love.codexbridge;

import java.net.URI;
import java.util.Locale;
import java.util.regex.Pattern;

/** Android uses its own tags; the repository's latest Release belongs to Windows. */
public final class MobileRelease {
 public static final String REPO="https://github.com/Lovest20018/Codex-Dock";
 public static final String API="https://api.github.com/repos/Lovest20018/Codex-Dock/releases?per_page=100";
 public static final long MAX_SIZE=512L*1024*1024;
 private static final Pattern VERSION=Pattern.compile("(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)");
 public final String version,notes,download,sha256;public final long size;
 public MobileRelease(String version,String notes){this(version,notes,-1,null);}
 public MobileRelease(String version,String notes,long size,String sha256){
  parts(version);this.version=version;this.notes=notes;this.size=size;
  String digest=sha256==null?"":sha256.startsWith("sha256:")?sha256.substring(7):sha256;
  this.sha256=digest.matches("[a-fA-F0-9]{64}")?digest.toLowerCase(Locale.ROOT):null;
  download=REPO+"/releases/download/android-v"+version+"/"+asset(version);
 }
 public static String asset(String version){return "LovestCode-Android-"+version+".apk";}
 public String page(){return REPO+"/releases/tag/android-v"+version;}
 public static String channel(String version){parts(version);return "stable";}
 private static long[] parts(String version){
  if(version==null||!VERSION.matcher(version).matches())throw new IllegalArgumentException("Invalid Android version");
  String[] fields=version.split("\\.");return new long[]{Long.parseLong(fields[0]),Long.parseLong(fields[1]),Long.parseLong(fields[2])};
 }
 public static int compare(String a,String b){long[] x=parts(a),y=parts(b);for(int i=0;i<x.length;i++)if(x[i]!=y[i])return Long.compare(x[i],y[i]);return 0;}
 public static boolean eligible(String current,String tag,boolean draft,boolean prerelease,String name,String url){
  try{if(draft||prerelease||!tag.startsWith("android-v"))return false;String v=tag.substring(9);
   return compare(v,current)>0&&asset(v).equals(name)&&new MobileRelease(v,"").download.equals(url);
  }catch(RuntimeException invalid){return false;}
 }
 public static boolean isDownload(String url){
  try{String prefix=REPO+"/releases/download/android-v";if(!url.startsWith(prefix))return false;
   String version=url.substring(prefix.length(),url.lastIndexOf('/'));return new MobileRelease(version,"").download.equals(url);
  }catch(RuntimeException invalid){return false;}
 }
 public static boolean redirectAllowed(String url){
  try{URI value=new URI(url);String host=value.getHost();return "https".equalsIgnoreCase(value.getScheme())&&value.getRawUserInfo()==null&&value.getRawFragment()==null&&(value.getPort()==-1||value.getPort()==443)&&host!=null&&
   (host.equalsIgnoreCase("github.com")||host.equalsIgnoreCase("objects.githubusercontent.com")||host.equalsIgnoreCase("release-assets.githubusercontent.com")||host.equalsIgnoreCase("github-releases.githubusercontent.com"));
  }catch(Exception invalid){return false;}
 }
}
