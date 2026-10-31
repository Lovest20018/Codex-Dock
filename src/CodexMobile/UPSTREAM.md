# Codex-only mobile worker

Selected modules from `try2love/codex-mobile-bridge`, commit
`0721aa7dafa6177751ba7e568e0ad0efee05655b` (MIT, version 2.1.7):

- `bridge/clients/codex/ipc.py`, `transport.py` and the Windows/POSIX byte-stream transports.
- `bridge/features/auth/auth.py`, `pairing.py`.
- `bridge/features/sessions/store.py`, `model.py`, `tool_images.py`.

The account-namespace helper `draft_identity` and image-signature helper `image_type`
are isolated into `auth/identity.py` and `sessions/image_signatures.py`; two imports
were changed accordingly. Other selected upstream modules are initially unchanged.
Upstream copyright and license: `UPSTREAM-LICENSE`.

`main.py`, `gateway.py`, `service.py` and `web/` are Dock's narrow integration.
They expose existing local Desktop sessions only, with a fixed HTTPS reverse-proxy
entry, an optional exact IPv4 proxy peer (including a public NAT egress), a separate loopback control listener, and no generic RPC, CLI launcher,
SSH, tunnels, terminal, account management or filesystem endpoints.
An explicit Desktop snapshot must match the saved session ID, cwd and provider.
Mutations use the original owner. Unknown operations are recorded before dispatch
and never replayed automatically. Only saved native message IDs reconcile a lost
send/steer response; prepared live turns do not establish acceptance.

Android compatibility is maintained through the `/api/auth`, `/api/login`,
`/api/pair`, `/api/logout` and cookie contracts, `#app` visibility and the
`bridge-authenticated`, `bridge-logged-out`, `bridge-auth-lost`,
`bridge-chat-visible` events. Phone UI is text-focused. External forms, plan
implementation and unsupported approval types require Desktop in this version.

QR encoding uses Project Nayuki's MIT `qrcodegen.py`, v1.8.0:
https://github.com/nayuki/QR-Code-generator/blob/v1.8.0/python/qrcodegen.py
SHA-256: `b089855caf16185c61421ea4927c1b213cf9468940d71fa8ab11ef83662dcc84`.
PNG rendering is local and uses the standard library. No token-bearing URL goes
to an external image service. See `third_party/nayuki-qrcodegen-LICENSE.txt`.

Build with Python 3.12 on Windows using `scripts/build-mobile.ps1`.
The runtime requires no pip packages. PyInstaller and its pinned dependencies
are build tools only. Keep the whole `mobile` directory beside CodexDock.exe.
