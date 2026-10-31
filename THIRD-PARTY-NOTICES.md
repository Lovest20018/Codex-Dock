# Third-party notices

## Codex Mobile Bridge 2.1.7 (Codex backend, Web UI and source snapshot)

Copyright (c) 2026 try2love. MIT.

Source: https://github.com/try2love/codex-mobile-bridge/tree/0721aa7dafa6177751ba7e568e0ad0efee05655b

The full Codex backend and Web UI are reused in the mobile worker. Source provenance and explicit overlays are documented in `src/CodexMobile/UPSTREAM.md`. The full license is in `third_party/codex-mobile-bridge-LICENSE.txt` and `src/CodexMobile/UPSTREAM-LICENSE`. Upstream Electron and Android executables are not bundled. The separate Dock Android APK compiles pinned Android helper sources with Dock-owned Activity, settings and branding; its notice and included upstream licenses are specified in `src/CodexAndroid/NOTICE.md`. QR scanning uses ZXing Core 3.5.3 (Apache-2.0), with its full license in the APK; menu icons use Lucide 0.468.0 (ISC), with attribution in the upstream mobile notice. Other AI providers are disabled by the Dock adapter; their source remains in the auditable upstream snapshot.

## Project Nayuki QR Code generator 1.8.0

Copyright Project Nayuki. MIT.

Source: https://github.com/nayuki/QR-Code-generator/tree/v1.8.0

The pure-Python encoder is used for local pairing QR generation. Full notice: `third_party/nayuki-qrcodegen-LICENSE.txt`.

## Python 3.12 and PyInstaller 6.22.3

The mobile worker bundles CPython and standard-library modules under the Python Software Foundation license and included third-party notices. PyInstaller packages the worker under GPL with its bootloader exception, which permits distribution of the generated executable under this project's license. Build dependencies are pinned in `scripts/requirements-mobile-build.txt`. Full notices are in `third_party/Python-LICENSE.txt` and `third_party/PyInstaller-COPYING.txt`; copies also accompany the mobile worker.

The browser QA tools (`playwright-core`, `jsqr`, `pngjs`) are development dependencies only and are excluded from binary releases. Their lockfile is included in the source archive.

## Superba (catalog data, translations and design reference)

Source: https://github.com/krill-lab/superba

Reviewed revision: `f78d77a2849aceb8dffc1f27a83869ffceab7e90` (2026-10-27).

Superba is licensed under MIT OR Apache-2.0. This project initially imported configuration metadata, curated grouping and Chinese translations from its Codex 0.164.0 catalog under the MIT option. The data is now independently maintained in `config/config-schema.json`; no upstream checkout or generator is required. Key-preserving editing, backup/rollback and PAT routing were independently implemented with its design as a reference. No Superba Rust crates or brand assets are linked or distributed. The MIT notice remains in `third_party/Superba-LICENSE.txt`.

## Tomlyn 0.19.0

Copyright Alexandre Mutel. BSD-2-Clause.

Source: https://github.com/xoofx/Tomlyn/tree/0.19.0

The full notice is in `third_party/Tomlyn-LICENSE.txt`.

## .NET 8 and System.Security.Cryptography.ProtectedData 8.0.0

Copyright .NET Foundation and contributors / Microsoft. MIT.

Source: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf

The Windows self-contained release includes runtime license and third-party notices under `third_party/`.

## Inno Setup simplified Chinese messages

`third_party/ChineseSimplified.isl` is adapted from the user-contributed Chinese Simplified language file from the Inno Setup repository, fixed at tag `is-6_5_4`:
https://github.com/jrsoftware/issrc/blob/is-6_5_4/Files/Languages/Unofficial/ChineseSimplified.isl

The file retains its translator attribution and source comments. The unmodified upstream file SHA-256 is `fc1dbf303559cfefd47cf59eec7f1ad1c5fc6266ef8fa64569c13bc754adfeb4`. Local changes remove four obsolete message names and translate the additional Inno Setup 6.7 messages.


## Microsoft ConPTY 1.25.260930003

Microsoft. MIT. The locked NuGet package `Microsoft.Windows.Console.ConPTY` provides the app-local Windows terminal DLL and architecture-specific OpenConsole executables. The build checks the upstream SHA-256 and package metadata. License and version metadata accompany `mobile/runtime/conpty`.

## certifi 2026.7.22

Mozilla CA root bundle, MPL-2.0. https://github.com/certifi/python-certifi . Trust roots and the package license accompany `mobile/runtime/bridge/features/auth` and `mobile/runtime/licenses/certifi` respectively.

## Upstream Web vendor assets

The worker includes the pinned upstream Markdown-it, texmath, KaTeX, Prism, Mermaid, PDF.js (including fonts/CMaps), and xterm.js / fit assets. Their original license files accompany the files under `mobile/runtime/web/vendor`, and the complete upstream provenance is in `src/CodexMobile/upstream/web/vendor/README.md`. No npm development installation or Electron binary is distributed.
