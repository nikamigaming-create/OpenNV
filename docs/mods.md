# Mod compatibility policy

Mod compatibility is a post-gameplay promotion track. New Vegas has bounded
opening and Goodsprings routes, while Fallout 3 reaches persistent stage 90 in
its bounded owned Vault 101 birth room without a freely playable wider Vault
runtime. The current Godot runtime does not claim any complete campaign, TTW,
JAM, or native extender-plugin compatibility.

When the base campaign route passes, a mod is promoted one behavior contract at
a time:

1. record its exact version, authored records, assets, commands, callbacks,
   persistent data, UI effects, and native prerequisites;
2. distinguish portable content from Windows-only executable behavior;
3. implement the smallest first-party runtime capability used by a real mod;
4. validate it with synthetic, authored natural, persistence, and retail
   differential evidence;
5. publish remaining unsupported behavior instead of showing a false ready
   state.

OpenNV never writes into a game installation, bundles third-party archives, or
loads an arbitrary native plugin merely because it was detected.

## Unified read-only source stacks

The launcher owns a versioned `opennv-mod-stack/v2` profile. **Choose New Vegas
Data** validates the legal install's `FalloutNV.esm` header and makes that
read-only Data directory layer zero. It imports the canonical local
`plugins.txt` plus `loadorder.txt` when present; an unlisted plugin is disabled,
and a non-official plugin without an explicit order fails registration. The
load-order files are size, time, and SHA-256 bound so a later manager edit
requires re-import. **Install local mod ZIP** safely installs an already-owned
ZIP into the launcher's private per-mod directory and registers that directory
as the next source layer. It supports stored and deflated ZIP members, including
a single outer `Data` directory; it fails closed on path escape, links, special
files, encryption, unsupported compression, corruption, or overwrite. This is
not a downloader, 7z extractor, or scripted FOMOD executor. **Add mod folder**
appends ordinary Vortex/deployed folders
in explicit low-to-high order. Selecting an MO2 profile directory instead
(a directory containing `modlist.txt` and `plugins.txt`) imports enabled mod
folders in MO2 priority order and active plugins in profile order. Portable
Wabbajack installations use that same MO2 profile contract. The profile indexes effective
top-level ESM, ESP, and BSA names plus byte length and last-write time without
extracting or hashing multi-gigabyte archives. It recursively inventories every
ordinary loose file by root, canonical relative path, byte length, and
last-write time; links, junctions, special files, and case-colliding paths fail
registration. Loose resources use a sealed in-memory case-insensitive
`low-to-high-last-wins` table while retaining winning and overridden identities.

Every v2 stack also binds an edition (`fallout-new-vegas`, `fallout-3`, or
`ttw`), engine/content build, supported campaigns, and a stack-scoped save
compatibility ID. TTW records the required xNVSE/JIP/ShowOff semantics as
clean-room capabilities; OpenNV never loads those extender DLLs.

The same panel lists every non-owned source layer in exact low-to-high priority
order. Enable/disable and priority changes rebuild the sealed stack from the
unchanged owned layer zero; each resulting `stackId` continues to own a separate
save path. **Uninstall** is available only for ZIP content created inside Gate
Vortex's private install root. MO2, Wabbajack, deployed Vortex, Nexus Mods App,
Thunderstore, TTW, JAM, and manual folders remain externally owned and are never
deleted by the launcher.

Standalone Fallout 3 uses a separate `profiles/fallout3/layers.json`, private
install root, `opennv-mod-stack/v2` identity, and stack-keyed save namespace.
Its ordinary deployed folders and static ZIP layers use the same controls without
sharing New Vegas roots or catalog state. Fallout 3 MO2/Wabbajack profile parsing
is not implemented; users may add its already-deployed mod folders individually.
Fallout 1 and Fallout 2 remain deliberately blocked in this manager: Fallout 1's
current direct profile admits exactly install `Data` over `critter.dat` and
`master.dat`, while Fallout 2 admits only `patch000.dat`, `critter.dat`, and
`master.dat`. Neither runtime has an ordered external loose-root contract, so
Gate Vortex refuses loose layers, DAT replacement, executables, and script
extenders instead of implying that those mods work.

The source contract accepts `manual`, `gate-vortex`, `mo2`, `wabbajack`, `vortex`,
`nexus-mods-app`, `thunderstore`, and `ttw-installer` provenance. These labels
all feed the same winner rules; a provider label alone does not grant behavioral
compatibility. On every launch the launcher rechecks declared roots, load-order
sources, top-level plugin/archive metadata, and sealed loose-file metadata,
hashes the stack manifest, and passes `--source-stack`,
`--source-stack-sha256`, `--stack-id`, and `--campaign` to Godot. Saves are
isolated by that stack ID. A changed declared plugin or archive fails closed and
must be registered again. The launcher does not guess a plugin order from
alphabetical filenames. Nexus/Thunderstore downloads and APIs, 7z, scripted
FOMOD choice graphs, and native extender DLL loading are not implemented. A ZIP
containing `fomod/ModuleConfig.xml` fails with instructions to deploy it through
a manager that implements those choices, then add that deployed folder/profile.

## Direct runtime media and localization

The native source stack can pass a winning loose file or BSA member directly
from memory to Godot. DDS textures use Godot 4.7.2's DDS decoder; WAV, MP3, and
Ogg Vorbis audio use the corresponding buffer decoders. No temporary converted
file or prepared media cache is written. Each loader first checks the container
signature and bounded header, then fails if Godot rejects the payload. A mod's
higher-priority loose or archive member therefore replaces the lower resource
through the same `low-to-high-last-wins` namespace used by meshes.

`STRINGS`, `DLSTRINGS`, and `ILSTRINGS` tables also have strict in-memory
readers. They validate the complete directory/data size, offsets, duplicate
IDs, terminators, and UTF-8 text before exposing an ID. Missing IDs and malformed
tables fail instead of producing blank UI text.

Effective `SOUN` records now resolve through the master-aware plugin winner map.
The runtime decodes exact `FNAM`, `RNAM`, `SNDX`/`SNDD`, attenuation-curve,
reverb, priority, flags, and loop-sample fields. Exact-file 2D/menu sounds with
bounded volume and loop behavior can become Godot audio players backed by the
winning in-memory resource. A strict 3D subset uses `AudioStreamPlayer3D` for
spatial panning while applying the source five-point gain curve, static
attenuation, loop points, and an explicit submerged-state mute input. Random
scheduling, folder variant sets, frequency randomization, envelopes, LFE/radius
behavior, and unbound environmental reverb fail closed at playback. Reverb
attenuation `0` is admitted only with a caller-provided `Area3D` mask for the
current acoustic preset; `100` is a dry send. Intermediate per-source wet-send
amounts remain unsupported. The registered official-plus-TTW audit has 1,925
exact-file 3D winners, admits 482 under that exact descriptor contract, and
resolves 479 resources. Fixed frequency adjustment is applied as an authored
pitch percentage; random frequency variance remains fail-closed. `SNDR` does not occur
in the owned official New Vegas corpus and is not admitted as an FNV record
layout. Dialogue response/language selection and joining localized IDs from
every record type remain separate capabilities. WAV codecs or DDS encodings
that Godot 4.7.2 cannot decode remain unsupported and fail closed; OpenNV does
not silently transcode them or fall back to a cache.

For explicit 3D playback requests, `RNAM` is a percentage gate driven by
serializable gameplay-owned sound RNG state. A failed roll occurs before any
resource bytes are loaded, and records without `RNAM` do not advance the RNG.
The registered stack has 74 exact-file 3D `RNAM` records, resolves 73 of their
resources, and admits nine complete descriptors under the current strict contract.
`Play At Random` interval scheduling, random location, and random stream start
remain fail-closed rather than borrowing wall-clock randomness.

## TTW profile

TTW is not downloaded or generated by OpenNV. The player uses the current TTW
installer against legally owned Fallout 3 and New Vegas installations, then
registers the resulting mod-manager layers as read-only OpenNV inputs. The
current inspector records their precedence order, the active plugin order and
hashes, exact master closure, discovered effective BSA names, declared TTW
version, plugin-stack ID, and a distinct save-compatibility ID. There is no TTW
save-loading route yet; the separate identity prevents future TTW support from
silently adopting a standalone Fallout 3 or New Vegas save.

Playable TTW runtime support is absent. The native source lane can register the
effective TTW roots, plugin order, active BSA order, and save-isolated stack ID;
it can index those plugins and resolve loose/BSA members in place. That is
source transport, not a TTW world. The bounded opening compiler separately
resolves only the effective CG00→CG01-stage-5 record, command, and owned-movie
closure described below. OpenNV does not execute those commands, present or
transition the Vault 101 world, load/save TTW gameplay, or execute xNVSE/JAM
plugins. No TTW output or derived cache enters Git or an OpenNV release.

The first concrete registration step is available now. Give the inspector the
effective MO2 data layers in low-to-high precedence order and the profile's
active load order:

```powershell
python content/tools/ttw_profile.py `
  --data-root "D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data" `
  --data-root "D:\Modding\MO2\mods\Tale of Two Wastelands" `
  --data-root "D:\Modding\MO2\mods\YUPTTW" `
  --load-order "D:\Modding\MO2\profiles\TTW\loadorder.txt" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\ttw-profile.json" `
  --ttw-version "installed version"
```

Each `--data-root` may be an install root containing `Data` or an MO2 mod
folder whose contents are already the data root. Supply layers from low to high
precedence; the inspector never modifies them. It requires the generated TTW
marker plugins, hashes every active plugin, validates exact master/load-order
closure, inventories effective BSA names, and emits the distinct save identity.
Archive members, loose files, records, scripts, and world transitions remain
uncompiled, so the manifest deliberately reports
`runtimeCompatibility.ready=false` and cannot be selected as a playable route.
The desktop launcher auto-detects this default output path or accepts it through
**Set up TTW**. Selecting it performs the expensive plugin hash proof once,
then emits the ordinary native mod-stack snapshot from the same read-only roots.
Subsequent launcher refreshes and launches verify the small provenance files plus
plugin/BSA size and modification time rather than re-hashing every ESM. A changed
source fails closed and requires registration again. Registration is shown
separately from playable TTW readiness.

For a flattened installer output whose top-level plugin modification times are
strictly increasing, the inspector can derive the all-active order without an
MO2 profile. Keep the legal New Vegas Data folder as the lower fallback layer
and the generated TTW output as the explicit highest-precedence layer:

```powershell
python content/tools/ttw_profile.py `
  --data-root "D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data" `
  --flattened-installer-output "D:\TTW\Installed" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\ttw-profile.json" `
  --ttw-version "3.4"
```

This mode rejects duplicate plugin timestamps, absent TTW markers, and any
master that does not precede its dependent. It writes
`ttw-profile.loadorder.txt` beside the profile and binds that immutable snapshot
as the launcher's load-order source. It does not derive an order from the lower
vanilla layer.

The next bounded source inspection revalidates every registered plugin and
records only effective top-level winners:

```powershell
python content/tools/ttw_source_namespace.py `
  --profile "$env:LOCALAPPDATA\OpenNV\profiles\ttw-profile.json" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\ttw-effective-source.json"
```

That neutral contract validates BSA v104 headers and zero-byte `.override`
markers without interpreting archive-member precedence, nested loose files, or
override-member semantics. It remains non-playable and reports
`runtimeCompatibility.ready=false`.

The next bounded compiler consumes that exact profile/namespace pair and emits
the source-bound Fallout 3 CG00→CG01-stage-5 command/movie contract beside them:

```powershell
python content/tools/ttw_fo3_opening.py `
  --ttw-profile "$env:LOCALAPPDATA\OpenNV\profiles\ttw-profile.json" `
  --source-namespace "$env:LOCALAPPDATA\OpenNV\profiles\ttw-effective-source.json" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\ttw-fo3-opening-profile.json"
```

The launcher revalidates the exact plugin stack, effective-source namespace,
owned movies, save identity, and dedicated cache identity. It prepares an
isolated future runtime handoff but remains disabled because the profile
truthfully reports `runtimeCompatibility.ready=false`.

## JAM and the xNVSE semantic layer

JAM is dependency- and portable-semantic-gated. Two bounded desktop runtime
semantics are transported below, but complete JAM runtime and launcher support
are not promoted.

The player obtains JAM and its declared prerequisites from their authors. There
is now a local registrar for an existing JAM/MO2 profile. It resolves effective
Data layers in their declared precedence, inventories and hashes
`JustAssortedMods.esp`, the four required DLC masters, xNVSE's three runtime
root files, and the installed JIP LN, JohnnyGuitar, kNVSE, lStewieAl, and UIO
files. Missing packages and plugin masters are emitted explicitly instead of
being mistaken for a complete profile. It writes only a small manifest outside
those read-only inputs; it does not copy mod assets.

```powershell
python content/tools/jam_profile.py `
  --game-root "D:\SteamLibrary\steamapps\common\Fallout New Vegas" `
  --data-root "D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data" `
  --data-root "D:\Modding\MO2\mods\JIP LN NVSE Plugin" `
  --data-root "D:\Modding\MO2\mods\JohnnyGuitar NVSE" `
  --data-root "D:\Modding\MO2\mods\kNVSE" `
  --data-root "D:\Modding\MO2\mods\lStewieAl's Tweaks" `
  --data-root "D:\Modding\MO2\mods\UIO" `
  --data-root "D:\Modding\MO2\mods\JAM - Just Assorted Mods" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\jam-profile.json" `
  --jam-version "installed version"
```

Supply `--data-root` layers from low to high precedence. Each can be a Data
folder, a game root containing Data, or one MO2 mod folder whose contents are
already a Data layer.

Native DLLs will not be loaded into Godot. Required commands, events, callbacks,
persistent values, animation requests, and UI mutations need portable first-party
implementations. Until the complete required-capability set exists for a
selected New Vegas or TTW profile, recognizing `JustAssortedMods.esp` is not
playable support and the launcher must keep JAM disabled. The emitted manifest
therefore has `runtimeCompatibility.ready=false`, `nativeDllLoading=false`, and
an explicit `unsupportedSemantics` list covering xNVSE plugin lifecycle,
command/event dispatch, cosaves, JIP/JohnnyGuitar extensions, kNVSE animation
overrides, Stewie engine/INI behavior, UIO HUD/XML mutation, and JAM execution.
The desktop launcher auto-detects the default manifest or accepts it through
**Set up JAM**, verifies the recorded file sizes and hashes, and keeps the JAM
toggle disabled while any dependency or portable semantic capability is absent.

The current bounded transports are `jvs-forward-sprint-speed-v1` and
`jbt-bullet-time-dilation-v1`. The registrar reads the installed `SCPT`/`SCTX`
and `GLOB` records, inventories their xNVSE and JIP command/event surfaces, and
admits each capability only when its exact authored scripts, settings, commands,
and dispatched-event declaration are present. The recipe hash and canonical
portable-capability hash are part
of the profile/save identity; the launcher and runtime reject edited or stale
capability metadata even when the source plugin hash itself is unchanged. For
the locally owned JAM 4.6 content those settings map DirectInput key 42 to physical Shift
and `JVSSpeedMult=75` to a 1.75 speed multiplier. Godot applies that exact value
only while Shift is held and movement has a forward component. AP and hardcore
drain, eligibility and crippled-limb checks, sounds, animations, forced
holstering, controller button 64, and `JVSStateChange` dispatch remain
unsupported. This is a real partial JVS behavior used by the desktop player,
not a complete JAM claim.

The same owned plugin maps DirectInput key 45 to physical X, enables JBT toggle
mode, and authors `JBTSlowMult=0.5` with `JBTSlowMultStanding=1`. The bounded
desktop transport therefore toggles Godot world time between 1.0 and the exact
effective 0.5 multiplier. It deliberately does not claim JBT's AP eligibility
or drain, weapon AP costs, perks, image-space effects, body-part highlighting,
sounds, FOV/sensitivity changes, controller path, or `JBTStateChange` dispatch.
The launcher remains disabled until those missing semantics and dependencies
are complete.

An incomplete local registration is still useful evidence. Its status is
`incomplete-local-dependency-profile`, its `missingDependencies` and
`missingPluginMasters` arrays name what is absent, and any independently
transported bounded capability remains separately visible. A fully present
dependency set receives `validated-local-dependency-profile`, but still has
`runtimeCompatibility.ready=false` until the complete JAM runtime exists.

Internet access is used only to open the authors' download/instruction pages or
an explicitly authorized mod-manager flow. OpenNV does not scrape credentials,
silently download third-party archives, or imply that recognizing
`JustAssortedMods.esp` makes JAM playable.

Current upstream entry points:

- [Tale of Two Wastelands installation guide](https://thebestoftimes.moddinglinked.com/index.html)
- [Tale of Two Wastelands FAQ](https://thebestoftimes.moddinglinked.com/faq.html)
- [JAM — Just Assorted Mods](https://www.nexusmods.com/newvegas/mods/66666)

Verified on 2026-08-28: the current TTW guide requires English Fallout 3 and
New Vegas copies with all DLC, Windows 10 or later, roughly 40 GB free, and
Nexus Mods plus MOD:PUB accounts. The current JAM page still identifies 4.6
and lists xNVSE, JIP LN, JohnnyGuitar, kNVSE, lStewieAl's Tweaks, UIO, and the
four main DLCs. Those account-backed author installers are not suitable for a
silent OpenNV download. The pages remain the authority for current downloads,
prerequisites, versions, and permissions; OpenNV records the integration
contract but does not freeze, scrape, or republish them.
