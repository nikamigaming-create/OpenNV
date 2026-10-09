# Windows development playtest

This is an experimental OpenNV Release build. The complete New Vegas campaign,
Fallout 3, TTW, required mods and physical-headset acceptance remain unfinished.
The native launcher reports the selected route's current status. Its artwork
is original decorative art, not a gameplay screenshot.

Bring your own legally owned installation. No retail files, transformed game
assets, retail executables or saves are included. Read `NOTICE.md` and
`THIRD_PARTY.md`. Godot and the .NET runtime are included; playing this package
requires no development SDK.

## Start a test

Keep the executable, PCK, native libraries and runtime directory together.
Run **OpenNV Flat.cmd**, select **New Vegas**, and choose your installation
folder. Select **Desktop** in the launcher. **Play** opens the original game menu;
**New Game** enters its original confirmation and bootstrap. **Continue** is
available only when the selected profile has an OpenNV save, and the game
validates that save against the selected source stack before loading it.

For VR, start your headset's OpenXR runtime, run **OpenNV VR.cmd**, and select
**VR**. Flat and OpenXR use the same authoritative gameplay and profile save.
The launcher does not change the system's OpenXR runtime. Close the game before
switching modes. Physical-headset behavior still requires a user playtest.

Installation folders, mod selections and OpenNV saves remain outside the pinned
package. OpenNV saves live under
`%APPDATA%/Godot/app_userdata/OpenNV/profiles/`. Retail saves are not loaded or
modified. A save from a different source stack can be refused visibly.

## Controls

In flat mode, use WASD and the mouse to move/look, E to activate, Tab for the
Pip-Boy, Escape for the pause menu and F5 for a manual save. **Save / Load**
opens the saved-game browser. Continue resumes the profile's configured save.
Use Q for the weapon wheel and H for consumables; release to confirm or use
Escape/the center to cancel. Scroll or Page Up/Down changes pages.

In OpenXR, use the thumbsticks for movement/turning and the controller
pointer/trigger for menus. The left Menu button or right-stick click opens
the pause menu. A short left-primary press saves; holding it opens the weapon
wheel. Hold the left trigger for consumables, select with the right stick and
release to confirm. The wrist device shares inventory and gameplay state.

## Pinned builds

`release-manifest.json` identifies the exact source commit and every packaged
file's SHA-256. `build-toolchain.json`, when built through the playtest helper,
also records the source tree, exporter and SDK. The launcher displays the build
identity. Each versioned package and ZIP keeps its original path.

For local development, `scripts/Build-OpenNVPlaytest.ps1` creates a checked-source
Release package and a stable named `.cmd` shortcut. Updating that shortcut points
it to a newer package without replacing earlier pinned versions. The sibling
`.json` records the selected version, commit and archive checksum. The helper
requires a clean committed source tree and a successful exported-launcher start.

## Report a failure

Record the build commit, mode, selected game/mod stack, location, action and
visible result. Unsupported scripts, native-plugin behavior, actor routines,
materials, vegetation/LOD, streaming and save/lifecycle owners remain active
implementation work. A successful launcher start or component check does not
establish campaign completion or parity. All complete-game requirements remain
open. Flat and physical-headset playtests are required before a golden release.
