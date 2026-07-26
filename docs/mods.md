# Mods: download, register, validate, launch

OpenNV is a profile-layer manager. It does not install a mod into a game
directory, copy files into TTW, or silently activate a native plugin.

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

A green/ready module means its required source files, runtime capability, and
launch validation are all recorded. A gated xNVSE/JIP/Johnny/ShowOff module is
not a broken download: it is intentionally held until the compatible behavior
has been implemented and tested in the OpenNV runtime.
