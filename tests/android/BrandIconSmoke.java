package io.github.lovest20018.codexdock.test;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.drawable.AdaptiveIconDrawable;
import android.graphics.drawable.Drawable;
import android.os.Build;
import android.os.Bundle;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;

/** Loads the compiled app resources in a separate disposable package, without production credentials. */
public final class BrandIconSmoke extends Instrumentation {
 private final JSONArray checks=new JSONArray();
 @Override public void onCreate(Bundle arguments){start();}
 private void check(boolean condition,String message){if(!condition)throw new AssertionError(message);checks.put(message);}
 private Bitmap render(Drawable drawable,int size){Bitmap bitmap=Bitmap.createBitmap(size,size,Bitmap.Config.ARGB_8888);drawable.setBounds(0,0,size,size);drawable.draw(new Canvas(bitmap));return bitmap;}
 private void save(Context context,Bitmap bitmap,String name)throws Exception{try(FileOutputStream output=new FileOutputStream(new File(context.getExternalFilesDir(null),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,output);}}
 private Drawable resource(Context context,String name){int id=context.getResources().getIdentifier(name,"drawable",context.getPackageName());check(id!=0,"Packaged resource exists: "+name);return context.getDrawable(id);}
 private int colorCount(Bitmap bitmap,int expected){int count=0;for(int y=0;y<bitmap.getHeight();y++)for(int x=0;x<bitmap.getWidth();x++){int actual=bitmap.getPixel(x,y);if(Color.alpha(actual)>250&&Math.abs(Color.red(actual)-Color.red(expected))<3&&Math.abs(Color.green(actual)-Color.green(expected))<3&&Math.abs(Color.blue(actual)-Color.blue(expected))<3)count++;}return count;}
 @Override public void onStart(){Bundle result=new Bundle();try{
  Context target=getTargetContext();Drawable loaded=target.getApplicationInfo().loadIcon(target.getPackageManager());
  check(loaded instanceof AdaptiveIconDrawable,"Application resolves a compiled adaptive launcher icon");
  AdaptiveIconDrawable icon=(AdaptiveIconDrawable)loaded;
  Bitmap launcher=render(icon,512);save(target,launcher,"android-native-adaptive.png");
  check(!icon.getIconMask().isEmpty(),"System launcher supplies a nonempty native icon mask");
  Bitmap foreground=render(icon.getForeground(),432);save(target,foreground,"android-native-foreground.png");
  int minX=432,minY=432,maxX=-1,maxY=-1;double radius=0;
  for(int y=0;y<432;y++)for(int x=0;x<432;x++)if(Color.alpha(foreground.getPixel(x,y))>16){minX=Math.min(minX,x);minY=Math.min(minY,y);maxX=Math.max(maxX,x);maxY=Math.max(maxY,y);double dx=x+0.5-216,dy=y+0.5-216;radius=Math.max(radius,Math.sqrt(dx*dx+dy*dy));}
  check(maxX>=minX&&maxY>=minY,"Adaptive foreground contains visible logo artwork");
  check(radius<=132,"Foreground stays inside the central 66dp safe circle on the 108dp layer");
  check(maxX-minX+1>=192&&maxY-minY+1>=192,"Foreground retains at least 48dp of width and height");
  check(colorCount(foreground,Color.rgb(35,69,60))>3000&&colorCount(foreground,Color.rgb(73,142,112))>1500,"Adaptive foreground contains the selected dark green and soft green palette");
  Bitmap background=render(icon.getBackground(),108);check(colorCount(background,Color.rgb(238,242,237))>11000,"Launcher background uses the selected light sage color");
  Bitmap inApp=render(resource(target,"ic_bridge"),512);save(target,inApp,"android-native-in-app.png");
  check(colorCount(inApp,Color.rgb(238,242,237))>50000&&colorCount(inApp,Color.rgb(35,69,60))>10000&&colorCount(inApp,Color.rgb(73,142,112))>3000,"In-app header mark loads the same selected palette");
  Bitmap notification=render(resource(target,"ic_notification"),96);save(target,notification,"android-native-notification.png");
  int visible=0,white=0;for(int y=0;y<96;y++)for(int x=0;x<96;x++){int value=notification.getPixel(x,y);if(Color.alpha(value)>250){visible++;if(Color.red(value)==255&&Color.green(value)==255&&Color.blue(value)==255)white++;}}
  check(visible>500&&visible==white&&Color.alpha(notification.getPixel(0,0))==0,"Notification icon is visible white artwork on a transparent background");
  if(Build.VERSION.SDK_INT>=33){check(icon.getMonochrome()!=null,"Android 13 and later resolves the packaged monochrome layer");Bitmap mono=render(icon.getMonochrome(),432);save(target,mono,"android-native-monochrome.png");check(colorCount(mono,Color.WHITE)>5000,"Themed icon has a usable single-color alpha mask");}
  JSONObject report=new JSONObject().put("status","PASS").put("sdk",Build.VERSION.SDK_INT).put("checks",checks).put("maximumForegroundRadiusDp",radius/4).put("scope","Compiled production icon overlays in a separate resource-only probe; not a signed production APK or phone acceptance");
  try(FileOutputStream file=new FileOutputStream(new File(target.getExternalFilesDir(null),"icon-report.json"))){file.write(report.toString(2).getBytes("UTF-8"));}
  result.putString("report",report.toString());finish(Activity.RESULT_OK,result);
 }catch(Throwable error){result.putString("failure",android.util.Log.getStackTraceString(error));finish(Activity.RESULT_CANCELED,result);}}
}
