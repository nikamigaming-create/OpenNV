# Mods: register, validate, launch

Open Nevada is a profile-layer manager. It does not install a mod into a game
directory, copy files into TTW output, or silently activate an unverified
native plugin.

1. Download a mod from its author-approved source using your own account where
   required.
2. Extract it into an untouched mod directory managed by you.
3. Register the directory in `local/paths.json` with
   `Configure-OpenNV.ps1`, or add the documented module source key yourself.
4. Inspect the plan before enabling it:

   ```powershell
   .\scripts\Manage-OpenNVMods.ps1 -Action Plan -Campaign TTW -Layer quality-of-life
   ```

5. Enable only a ready layer, then launch with `-UseManagedMods`.

   ```powershell
   .\scripts\Manage-OpenNVMods.ps1 -Action Enable -Campaign TTW -Layer quality-of-life
   .\scripts\Start-OpenNV.ps1 -Campaign TTW -UseManagedMods
   ```

## Portable compatibility, not a Windows-only cutoff

No mod is excluded merely because its original extension was a Windows DLL.
Open Nevada treats native plugins as behavioral specifications: it records the
commands, callbacks, persistent data, and UI/engine effects a mod needs, then
implements and validates portable equivalents in the OpenNV extender bridge.

A module becomes **ready** only after all of these are recorded:

- source files and legal asset prerequisites are present;
- every required runtime capability has a compatible implementation;
- the intended launch path is validated with telemetry;
- the module's compatibility record names the tested version and remaining
  limitations, if any.

This protects players from a green checkmark that merely means “the archive was
found.” A gated xNVSE/JIP/Johnny/ShowOff module is not a broken download: its
portable behavior is still being implemented or tested. See the desktop
[launcher architecture](desktop-launcher.md) for the bridge model.
