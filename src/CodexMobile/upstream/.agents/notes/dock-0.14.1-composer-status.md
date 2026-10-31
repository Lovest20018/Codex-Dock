# Dock 0.14.1: confirmed drafts and passive status reads

The pinned upstream source remains at 0721aa7dafa6177751ba7e568e0ad0efee05655b. Dock overlays are explicitly hashed in the parent upstream-overlay.json; this local decision record is additional integration documentation.

- A draft revision tracks text, quotes, modes and skills. Caret positions still persist but do not change its content revision.
- An in-flight provisional send may record only its first verified account scope, for the same gateway and chat and an unchanged draft. Request IDs stay in their original store. Unknown writes are never replayed automatically.
- Confirmation clears only the recorded revision and normalized content snapshot. A consumed marker prevents a later HTTP acknowledgement from deleting a new draft after an earlier receipt cleared the original.
- Passive client status failures notify once per continuous error and back off to 60 seconds. Explicit refresh bypasses that delay and remains actionable in application management. Stale login/state generations cannot publish errors.

The integration tests under the parent repository's tests/mobile cover real production JavaScript, delayed acknowledgements, new edits, identity changes, unknown delivery, status read aborts and manual retry. Authentication, owner/provider/cwd and proxy policies are unchanged.
