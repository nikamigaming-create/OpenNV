# Windows experimental Fallout: New Vegas playtest

This experimental Windows build reads a legally owned Fallout: New Vegas
installation directly. The current desktop development build has exercised
recorded Doc dialogue, Reflectron, Vigor allocation, ordinary movement and F5
saving. Selected combat, inventory, crafting and quest paths also have existing
runtime evidence and headless checks; the complete campaign is unfinished.
Package-specific acceptance and exact build identity belong to the release notes.

Expect bugs and explicit unsupported behavior. The compiled SCDA bytecode VM is
not implemented; parsed-source execution is not complete scripting support.
Some DLC startup paths stop at missing essential-state or leveled-list mutation
owners. Arbitrary dynamic references, full actor procedures, all weapons, source
presentation parity and performance remain incomplete. Physical OpenXR/Air Link
acceptance is open. Launcher route availability is not a gameplay certification.
FO3, TTW and custom-plugin acceptance remain incomplete.

Bring your own legally owned installation. No retail files, transformed game
assets, retail executables or saves are included. Read `NOTICE.md` and
`THIRD_PARTY.md`. Godot and the .NET runtime are included; playing the package
requires no development SDK. Keep the executable, PCK, native libraries and
runtime directory together.

## Start a test

Run **OpenNV Flat.cmd** and choose your Fallout: New Vegas installation folder.
It selects New Vegas and Desktop. **Play** opens the original game menu;
**New Game** enters its original confirmation and bootstrap. **Continue** needs
an OpenNV save in this profile and checks compatibility with the selected files.
No completed checkpoint is supplied or implied by this package.

For Quest 3, connect to the PC through Air Link and start **OpenNV VR.cmd** with
the Oculus OpenXR runtime active. It selects VR in the desktop launcher. Click
**Play** there to initialize the game's XR presentation, then use the original
game menu in VR for **New Game** or **Continue**. Close the game before switching
modes. Flat and OpenXR share authoritative gameplay and the profile save.
The launcher does not change the system's OpenXR runtime. Simulator checks do
not establish physical Air Link tracking, latency or headset acceptance.

Both package launchers select official base-game/DLC plugins and disable native
extensions. Loose files already present in the owned installation still win
resource lookup; this selection does not verify or repair a modified installation.

The package launchers request this profile root:
`%APPDATA%/Godot/app_userdata/OpenNV/vanilla/`. Installation registrations live in
`launcher/profiles-v1.json`; FNV saves live in `profiles/newvegas/` beneath that
root. On first use, the current launcher can reuse existing registrations,
including their stored save paths. Confirm the selected installation/save path;
choosing the game folder again registers a save path beneath the requested root.
Retail saves are not loaded or modified. Copy a compatible preserved OpenNV save
into the chosen profile if needed; retain the original. A different source stack
can be refused visibly.
For another independent profile, use the executable directly, for example:
`OpenNV.exe --xr-mode on -- --launcher --launcher-game newvegas --presentation openxr --native-plugins none --content-selection vanilla --profile-root <absolute-directory>`.

For a configured isolated profile, developers can run
`scripts/Set-OpenNVPlayShortcuts.ps1 -ReleaseDirectory <package> -ProfileRoot <profiles>`.
It defaults to New Vegas and creates only modes offered by the package manifest.
The current Fallout 3 route offers Desktop; it has no enabled VR route.

## Controls

In flat mode, use WASD and the mouse to move/look, E to activate, Tab for the
Pip-Boy, Escape for the pause menu and F5 for a manual save. **Save / Load**
opens the saved-game browser. Continue resumes the profile's configured save.
Use Q for the weapon wheel and H for consumables; release to confirm or use
Escape/the center to cancel. Scroll or Page Up/Down changes pages.

In OpenXR, use the thumbsticks for movement/turning and the controller
pointer/trigger for menus. The left Menu button or right-stick click opens
the pause menu. A short left X press toggles enlarged Pip-Boy mode; holding it opens the weapon
wheel. Hold the left trigger for consumables, select with the right stick and
release to confirm. In Pip-Boy mode, point and click with the right trigger;
tap X again to return to gameplay. Use the pause menu for manual saves. Left
squeeze closes the free hand and holds the weapon support grip when nearby. The right
trigger fires, A activates, and B reloads. The wrist device shares inventory
and gameplay state.

## Build identity and feedback

`release-manifest.json` identifies the source commit, source modification state
and packaged file hashes. `build-toolchain.json`, when built through the
playtest helper, also records the source tree, exporter and SDK. The launcher
displays build identity. `scripts/Build-OpenNVPlaytest.ps1` requires clean committed
source and checks the exported launcher; it does not certify gameplay.

Report the release version/commit, mode, owned source selection, location,
ordinary input and visible result, including the first error and whether saving
or Continue still works. Describe expected behavior separately from observation.
Do not upload retail assets or saves to public issues; share only a concise error
excerpt and non-proprietary build/system information.

A useful bounded path is Doc's opening, Goodsprings/Sunny dialogue, shooting,
combat/loot, campfire crafting and Save/Quit/cold Continue. The requested proof
recording follows real elapsed-time gameplay from this package; encoded video
frame rate is not measured game FPS. A blocked or untested part stays incomplete.
Native 4K60, full campaign/parity and physical XR144 are not claimed by this
experimental playtest.
