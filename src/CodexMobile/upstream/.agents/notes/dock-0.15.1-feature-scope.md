# Dock feature scope in the shared web host

Dock uses a fixed externally managed HTTPS origin and Windows lifecycle controls. Its presentation module advertises `supportsNetworkAdaptation: false`. ClientNavigation omits both constructing and reading upstream dynamic network deployment settings under that explicit optional flag; without it the upstream behavior remains unchanged.

Dock's HTTP adapter separately denies upstream mobile and Codex Desktop update helpers and network adaptation, retaining authentication and CSRF enforcement. Hiding a control is not treated as authorization. Account enrollment, session ownership, uncertain writes, files, terminals, Git and approvals keep their existing protocol boundaries. The distributed upstream Android APK is unchanged; a new Dock native shell is a separate signed artifact and has not been built.

Validation: production Dock browser regression plus adapter HTTP tests; existing upstream Node regression checks the default navigation behavior.
