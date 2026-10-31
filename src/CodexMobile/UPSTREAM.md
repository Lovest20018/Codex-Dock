# Codex workbench integration

The source snapshot is from `try2love/codex-mobile-bridge` 2.1.7, commit
`0721aa7dafa6177751ba7e568e0ad0efee05655b`, MIT. The complete Codex backend
and Web UI are reused. The upstream Android app (build 40) remains the mobile host.
No Electron or Android executable is rebuilt or bundled by Dock.

`upstream-manifest.json` records original SHA-256 digests. `upstream-overlay.json`
enumerates every intentional modification and omitted file. Run
`scripts/verify-mobile-upstream.py` before building; unexpected edits fail the build.
Upstream tests, architecture notes, native helpers and licenses accompany the source.

Dock owns only startup/configuration, fixed HTTPS ingress, local WPF control,
Codex-only filtering, a loopback enrollment host, and migration. The shared-source
login fix from 0.13.3 is retained: new authentication is restricted after failures;
valid sessions remain usable until explicit policy, expiry or revocation applies.
Legacy uncertain submissions never resend automatically. Legacy browser drafts
are imported only after the user confirms the current account scope.

All original Codex session/workspace/account/notification APIs use the upstream
ownership, host, provider, authentication and CSRF checks. Other client providers
are disabled. The local enrollment host reuses the upstream account and CLI panels;
its one-use 60-second link exchanges for a 30-minute HttpOnly cookie and CSRF token.
The public listener never exposes enrollment credentials or generic desktop RPC.

The public entry remains Dock's single configured HTTPS origin and IPv4 listener.
External reverse proxy, certificates, tunnels and router rules are operator managed.
Upstream alternate network provisioning and other AI clients are outside this build.

Build with Python 3.12 and `scripts/build-mobile.ps1`. The frozen worker includes
certifi trust roots, pinned Microsoft ConPTY, the native Codex quit helper, remote
adapter Python sources, Web vendor libraries and their license files. Keep the entire
`mobile` directory beside CodexDock.exe. Local QR generation uses Nayuki 1.8.0;
no token-bearing URL is sent to an image service.
