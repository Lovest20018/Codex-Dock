# Dock 0.15.0 UI integration

The approved Dock UI plan covers web/mobile and the loopback-only enrollment manager. Keep the pinned upstream snapshot and explicit overlays. Dock-owned presentation assets live outside the snapshot in `../ui`, appended to the existing app/style responses and copied into frozen runtime/ui. Do not relax resource routing, auth, CSP or host checks to load them.

Optional globalThis.DockUI hooks expose list, state, delivery, queue and client presentation. Upstream standalone execution remains valid without DockUI. Existing controls and feature handlers own all writes. Pending/receipt/draft identity and revisions remain the source of truth. A repeated identical uncertain payload opens message management instead of posting again; it never generates a replacement request ID or replays a write. An explicit send_not_submitted response retains the draft and permits manual correction.

Dock settings tabs move existing controls without rebuilding them. Scope text distinguishes browser preferences from gateway-wide settings. The loopback manager reuses AccountsPanel/CliAccessPanel/WslAccountsPanel and preserves the API editor while changing tabs; it does not expose new remote write methods.

Production-gateway browser regressions use synthetic credentials, data and intercepted sends. They assert IME/rejection/queue/uncertain states, draft retention, settings scope, manager edit retention, dialog focus and real viewport changes with mounted synthetic file/terminal tabs. Performance uses exact suffix removal as a presentation baseline and local render/layout samples only. Native WebView, real PTY, external network and model execution require their own acceptance; do not infer them from Chromium or synthetic panels.
