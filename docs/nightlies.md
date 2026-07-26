# Nightly and stable release policy

`nightly` is a rolling prerelease built from the source revisions named in
`BUILD-INFO.json` inside the archive. It is for testing real licensed assets
and feeding telemetry back into the compatibility matrix.

Stable tags (`v*`) are built from a tagged source revision. A stable build is
eligible only when its package smoke test, campaign preflight, and required
telemetry scenarios pass.

Each release is deliberately asset-free. Users provide their legal Fallout 3,
Fallout: New Vegas, DLC, TTW output, and manually authorized mod downloads.
That keeps releases small, lawful, updateable, and safe to extract without
overwriting a game or a mod manager.

The release workflow creates a reproducible archive with:

- the OpenNV-derived runtime and resources;
- the headless campaign/mod-manager scripts;
- tracked default settings and documentation;
- `BUILD-INFO.json`, which names product, launcher, and engine revisions.

The workflow never uploads user game data, saves, profiles, or downloaded mod
archives.
