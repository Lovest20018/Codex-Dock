"""Compile the owned icon resources into a disposable probe package, never a production signer."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--jdk',type=Path,required=True)
parser.add_argument('--sdk',type=Path,required=True)
parser.add_argument('--device',default='emulator-5562')
parser.add_argument('--output-dir',type=Path,required=True)
args=parser.parse_args()
out=args.output_dir.resolve()
if not args.device.startswith('emulator-') or not out.is_relative_to((ROOT/'artifacts').resolve()) or out.exists():
    parser.error('Use an isolated emulator and a fresh artifacts output directory.')
out.mkdir(parents=True)
env={**os.environ,'JAVA_HOME':str(args.jdk),'PATH':str(args.jdk/'bin')+os.pathsep+os.environ['PATH']}
tools=args.sdk/'build-tools/35.0.0'
android=args.sdk/'platforms/android-35/android.jar'
package='io.github.lovest20018.codexdock.iconprobe'
def run(*command,capture=False):
    value=subprocess.run([str(item) for item in command],cwd=ROOT,env=env,check=True,text=True,encoding='utf-8',errors='replace',stdout=subprocess.PIPE if capture else None,stderr=subprocess.STDOUT)
    return value.stdout
def adb(*command,capture=False): return run(args.sdk/'platform-tools/adb.exe','-s',args.device,*command,capture=capture)
assert adb('shell','getprop','ro.kernel.qemu',capture=True).strip()=='1'
installed=False
try:
    with tempfile.TemporaryDirectory(prefix='icon-resource-probe-',dir=out) as temporary:
        work=Path(temporary)
        for folder in ('gen','classes','dex'): (work/folder).mkdir()
        shutil.copytree(ROOT/'src/CodexAndroid/res',work/'res')
        (work/'AndroidManifest.xml').write_text(f'''<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="{package}"><uses-sdk android:minSdkVersion="26" android:targetSdkVersion="35"/><application android:label="Isolated icon resource probe" android:icon="@mipmap/ic_launcher" android:allowBackup="false"/><instrumentation android:name="io.github.lovest20018.codexdock.test.BrandIconSmoke" android:targetPackage="{package}"/></manifest>''',encoding='utf-8')
        run(tools/'aapt2.exe','compile','--dir',work/'res','-o',work/'resources.zip')
        run(tools/'aapt2.exe','link','-I',android,'--manifest',work/'AndroidManifest.xml','--java',work/'gen','-o',work/'base.apk',work/'resources.zip')
        run(args.jdk/'bin/javac.exe','-encoding','UTF-8','--release','8','-classpath',android,'-d',work/'classes',ROOT/'tests/android/BrandIconSmoke.java')
        with zipfile.ZipFile(work/'classes.jar','w') as archive:
            for path in (work/'classes').rglob('*.class'): archive.write(path,path.relative_to(work/'classes'))
        run(tools/'d8.bat','--lib',android,'--min-api','26','--output',work/'dex',work/'classes.jar')
        shutil.copy2(work/'base.apk',work/'unsigned.apk')
        with zipfile.ZipFile(work/'unsigned.apk','a') as archive: archive.write(work/'dex/classes.dex','classes.dex')
        run(tools/'zipalign.exe','-f','4',work/'unsigned.apk',work/'aligned.apk')
        run(args.jdk/'bin/keytool.exe','-genkeypair','-alias','icon-probe','-keystore',work/'ephemeral-test.p12','-storetype','PKCS12','-storepass','ephemeral-icon-probe','-keypass','ephemeral-icon-probe','-keyalg','RSA','-keysize','2048','-validity','1','-dname','CN=Offline icon resource probe')
        run(tools/'apksigner.bat','sign','--ks',work/'ephemeral-test.p12','--ks-key-alias','icon-probe','--ks-pass','pass:ephemeral-icon-probe','--out',out/'resource-probe.apk',work/'aligned.apk')
    adb('install','-r',out/'resource-probe.apk')
    installed=True
    result=adb('shell','am','instrument','-w','-r',package+'/io.github.lovest20018.codexdock.test.BrandIconSmoke',capture=True)
    (out/'instrumentation.txt').write_text(result,encoding='utf-8')
    adb('pull','/sdcard/Android/data/'+package+'/files',out/'renders')
    report=json.loads((out/'renders/icon-report.json').read_text(encoding='utf-8'))
    assert report['status']=='PASS'
    (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
finally:
    if installed: adb('uninstall',package)
