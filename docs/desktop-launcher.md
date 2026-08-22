# Open Nevada desktop launcher

The Open Nevada launcher is a first-party Electron application, designed as a
native desktop shell for Windows, macOS, and Linux. It deliberately does not
reuse another project's product language: its visual system is the Open Nevada
atlas, and legal provenance is kept in [NOTICE.md](../NOTICE.md), not in the
player-facing identity.

## Product contract

The launcher has three immutable character paths:

| ID | Path | Save boundary |
| --- | --- | --- |
| `newvegas` | Standalone New Vegas | Mojave-only character |
| `fallout3` | Standalone Fallout 3 | Capital Wasteland-only character |
| `ttw` | Combined TTW | One Capital Wasteland-to-Mojave character |

JAM is a profile layer for `newvegas` and `ttw`, rather than a fourth character
path. It can be added later, but the launcher warns that a save using it must
continue to use it.

The portable contract is implemented in
[`desktop/src/contract.mjs`](../desktop/src/contract.mjs). It is deliberately
separate from the current Windows PowerShell bridge, so platform-specific
runtime launches cannot redefine product or save semantics.

## Runtime manifest and portability

The Electron shell reads `runtime-manifest.json` from a selected OpenNV runtime
folder on every platform. The manifest declares its Godot version,
capabilities, campaign readiness, launch eligibility, and platform executable.
The launcher never infers playability from a folder name or a bridge script.

The checked-in static-geometry slice deliberately declares `canLaunch: false`.
Once a campaign passes, the launcher spawns the declared Godot executable with
an explicit campaign request. No Windows-only PowerShell engine bridge defines
the product contract.

Each runtime port must pass the same gates before a release calls it playable:

1. isolated profile generation without source-folder writes;
2. new-character telemetry for each supported campaign;
3. extender capability tests for every promoted mod;
4. package launch and dependency checks on the release platform.

The launcher never claims a Windows native DLL is portable simply because its
archive was detected. The compatibility bridge replaces required behavior with
tested runtime contracts.

## Develop the shell

Install a current Node.js LTS release, then:

```powershell
cd desktop
npm install
npm test
npm run dev
```

The test suite validates the campaign boundary, JAM rule, and safe merging of
a platform runtime state into the product contract. `npm run package` builds a
desktop shell for the host platform.
