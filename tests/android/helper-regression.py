"""Run unchanged Java download assertions over isolated HTTPS.

This host's HTTP filter rewrites a fixture's oversized Content-Length into
chunked encoding. Only fixture server construction and its local URL scheme
are adapted here; production helpers and all original assertions stay intact.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
UP = ROOT / 'src/CodexMobile/upstream'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--jdk', type=Path, required=True)
args = parser.parse_args()
out = ROOT / 'artifacts/android-helper-regression'
out.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix='tls-', dir=out) as tls_directory:
    key = Path(tls_directory) / 'fixture.p12'


    def run(*command):
        subprocess.run([str(a) for a in command], check=True, cwd=ROOT)


    if not key.exists():
        run(args.jdk / 'bin/keytool.exe', '-genkeypair', '-alias', 'fixture', '-keystore', key,
            '-storetype', 'PKCS12', '-storepass', 'fixture-password', '-keypass', 'fixture-password',
            '-keyalg', 'RSA', '-keysize', '2048', '-validity', '7', '-dname', 'CN=Android isolated fixture', '-ext', 'SAN=ip:127.0.0.1')
    helper = '''
     static HttpServer fixtureServer() throws Exception {
      java.security.KeyStore keys=java.security.KeyStore.getInstance("PKCS12");
      try(java.io.InputStream in=new java.io.FileInputStream(System.getProperty("fixture.store"))){keys.load(in,"fixture-password".toCharArray());}
      javax.net.ssl.KeyManagerFactory managers=javax.net.ssl.KeyManagerFactory.getInstance(javax.net.ssl.KeyManagerFactory.getDefaultAlgorithm());
      managers.init(keys,"fixture-password".toCharArray());
      javax.net.ssl.SSLContext tls=javax.net.ssl.SSLContext.getInstance("TLS");tls.init(managers.getKeyManagers(),null,null);
      com.sun.net.httpserver.HttpsServer server=com.sun.net.httpserver.HttpsServer.create(new InetSocketAddress("127.0.0.1",0),0);
      server.setHttpsConfigurator(new com.sun.net.httpserver.HttpsConfigurator(tls));return server;
     }
    '''
    java = UP / 'mobile/android/src/io/github/try2love/codexbridge'
    inputs = [java / name for name in ('GatewayURL.java', 'MobileRelease.java', 'ArtifactDownload.java')]
    for name in ('ArtifactDownloadTests', 'ArtifactRangeTests'):
        text = (UP / f'mobile/tests/{name}.java').read_text(encoding='utf-8')
        setup = 'HttpServer server=HttpServer.create(new InetSocketAddress("127.0.0.1",0),0);'
        assert text.count(setup) == 1
        text = text.replace(setup, 'HttpServer server=fixtureServer();').replace('http://127.0.0.1:', 'https://127.0.0.1:')
        text = text.replace(f'public class {name} {{', f'public class {name} {{' + helper)
        path = out / (name + '.java')
        path.write_text(text, encoding='utf-8')
        inputs.append(path)
    run(args.jdk / 'bin/javac.exe', '-encoding', 'UTF-8', '-d', out, *inputs)
    for name in ('ArtifactDownloadTests', 'ArtifactRangeTests'):
        run(args.jdk / 'bin/java.exe', '-Djavax.net.ssl.trustStore=' + str(key), '-Djavax.net.ssl.trustStorePassword=fixture-password',
            '-Dfixture.store=' + str(key), '-cp', out, name, out / name)
