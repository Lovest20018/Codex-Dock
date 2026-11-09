package io.github.try2love.codexbridge;
import java.io.*;
import java.nio.file.*;
import java.security.*;

public final class UpdateReleaseTests {
 static int checks;
 static void check(boolean value,String message){if(!value)throw new AssertionError(message);checks++;}
 static String hash(byte[] bytes)throws Exception{StringBuilder out=new StringBuilder();for(byte value:MessageDigest.getInstance("SHA-256").digest(bytes))out.append(String.format("%02x",value));return out.toString();}
 interface Action{void run()throws Exception;}
 static void rejects(Action action,String message)throws Exception{try{action.run();}catch(IOException|IllegalArgumentException expected){checks++;return;}throw new AssertionError(message);}
 public static void main(String[] args)throws Exception{
  String url="https://github.com/Lovest20018/Codex-Dock/releases/download/android-v0.2.5/LovestCode-Android-0.2.5.apk";
  check(MobileRelease.eligible("0.2.4","android-v0.2.5",false,false,"LovestCode-Android-0.2.5.apk",url),"New Android release selected");
  check(!MobileRelease.eligible("0.2.4","v99.0.0",false,false,"CodexDock-99.0.0-Setup-win-x64.exe",url),"Windows release ignored");
  check(!MobileRelease.eligible("0.2.5","android-v0.2.5",false,false,"LovestCode-Android-0.2.5.apk",url),"Current ignored");
  check(!MobileRelease.eligible("0.2.6","android-v0.2.5",false,false,"LovestCode-Android-0.2.5.apk",url),"Downgrade ignored");
  check(!MobileRelease.eligible("0.2.4","android-v0.2.5",true,false,"LovestCode-Android-0.2.5.apk",url),"Draft ignored");
  check(!MobileRelease.eligible("0.2.4","android-v0.2.5",false,true,"LovestCode-Android-0.2.5.apk",url),"Prerelease ignored");
  check(!MobileRelease.eligible("0.2.4","android-v0.2.5-rc.1",false,false,"LovestCode-Android-0.2.5.apk",url),"Preview tag ignored");
  check(MobileRelease.compare("0.2.10","0.2.9")>0&&MobileRelease.compare("1.0.0","0.99.99")>0,"Numeric comparison");
  check(MobileRelease.isDownload(url),"Exact download accepted");
  for(String invalid:new String[]{url+"?x=1",url+"#fragment",url.replace("github.com/","github.com.evil.invalid/"),url.replace("https:","http:"),url.replace("Lovest20018","try2love"),url.replace("android-v","v"),url.replace("0.2.5/","0.02.5/")})check(!MobileRelease.isDownload(invalid),"Untrusted download rejected");
  for(String valid:new String[]{"https://github.com/release","https://release-assets.githubusercontent.com/a?token=value","https://objects.githubusercontent.com/a"})check(MobileRelease.redirectAllowed(valid),"GitHub redirect accepted");
  for(String invalid:new String[]{"http://github.com/a","https://github.com.evil.invalid/a","https://user@github.com/a","https://github.com:444/a","https://github.com/a#fragment"})check(!MobileRelease.redirectAllowed(invalid),"Untrusted redirect rejected");
  byte[] bytes="verified package fixture".getBytes("UTF-8");File directory=Files.createTempDirectory(Paths.get(args[0]),"session-").toFile();File file=new File(directory,"bridge-download-fixture.part");Files.write(file.toPath(),bytes);
  MobileRelease release=new MobileRelease("0.2.5","",bytes.length,hash(bytes));
  ArtifactDownload.Task task=ArtifactDownload.Task.restoreUpdate(release,directory,file.getName());UpdateInstallSession session=new UpdateInstallSession(release,task,directory);
  check(session.completed()&&!session.completed(),"Completion handled once");
  check(session.beginInstall()&&!session.beginInstall(),"Only one installation handoff");session.awaitPermission();
  check(session.waitingPermission(),"Permission persisted");
  UpdateInstallSession restored=UpdateInstallSession.restore(directory,"0.2.4");check(restored!=null&&restored.waitingPermission(),"Pending package survives recreation");
  check(restored.permissionReturned()&&!restored.permissionReturned(),"Permission return consumed once");
  Files.write(file.toPath(),"corrupt package fixture!".getBytes("UTF-8"));rejects(()->UpdateInstallSession.restore(directory,"0.2.4"),"Tampered pending package rejected");
  Files.write(file.toPath(),bytes);check(UpdateInstallSession.restore(directory,"0.2.5")==null&&!file.exists(),"Installed version clears stale package");
  rejects(()->new ArtifactDownload.Task(url,MobileRelease.REPO,directory,bytes.length,null,"test.apk"),"Missing digest refused");
  rejects(()->new ArtifactDownload.Task(url,MobileRelease.REPO,directory,MobileRelease.MAX_SIZE+1,hash(bytes),"test.apk"),"Oversize refused");
  System.out.println(checks+" Android update checks passed.");
 }
}
