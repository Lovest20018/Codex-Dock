package io.github.try2love.codexbridge;

/** Shared helpers load in their original order, followed by the owned shell adapter. */
final class MobileWebScripts {
 static String appearance(android.content.res.AssetManager assets){
  try(java.io.ByteArrayOutputStream out=new java.io.ByteArrayOutputStream()){
   for(String asset:new String[]{"mobile-ui.js","mobile-clipboard.js","mobile-system-bars.js","mobile-session.js","lovest-shell.js"}){
    try(java.io.InputStream in=assets.open(asset)){byte[] data=new byte[4096];int size;while((size=in.read(data))!=-1)out.write(data,0,size);out.write('\n');}
   }
   return out.toString("UTF-8");
  }catch(java.io.IOException e){throw new IllegalStateException("Missing mobile layout",e);}
 }
}
