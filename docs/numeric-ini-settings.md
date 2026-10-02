# Numeric INI settings and loaded-plugin queries

The shared script hosts execute `GetNumericINISetting` against the installation
captured by their complete source graph. The owned executable supplies canonical
declarations; selected INI files and explicit profile rows supply overrides.
File rows alone do not declare settings. The existing `GetINIFloat` profile
storage API is a separate owner.

## Source and value ownership

`FalloutExecutableStringTable` reads compiler-emitted associations without
executing the owned executable. It connects a name, payload and writable
descriptor to a typed constructor, collection factory and virtual registration.
Inline descriptors also retain packed component values assembled in a stack
local. Their byte masks, shifts, local identity and receiving descriptor must
agree. Registration accepts the evidenced EAX and EDX singleton transport;
factory caching includes the singleton identity. Unknown required relationships,
duplicate declarations and invalid numeric payloads refuse admission.

`FalloutNumericIniSettings` retains Main, Prefs and Renderer as distinct
collections. Full-name lookup is case insensitive, while the canonical source
prefix determines Boolean, signed integer, unsigned integer or Float32 storage.
Float32 values widen to the script number. Lookup searches Prefs before Main;
a found nonnumeric setting returns -1 without falling through. Renderer-only
and missing settings also return -1. These rules follow the
[xNVSE 6.4.9 setting API](https://github.com/xNVSE/NVSE/blob/6.4.9/nvse/nvse/GameSettings.cpp).

Main overlays are the installation default, user Fallout.ini and
FalloutCustom.ini. Prefs and Renderer receive FalloutPrefs.ini independently.
Explicit profile rows then override matching declarations. Source names and
origins remain visible, and the executable identity is hashed. The selected
owned inputs are never written. The typed owner is lazy and independent for
each source graph; an owned graph cannot replace its captured installation
with an unrelated settings owner. Fallout 3 executable INI declaration layouts
remain unadmitted.

`IsModLoaded` reads the actual loaded plugin name index of the calling source
graph. It returns 1 for a loaded name and 0 otherwise. It neither scans files
nor infers runtime support from installed packages. Filename case comparison
uses the same case-insensitive namespace as the loaded graph. Its typed string
argument and the INI getter's source-string argument use the existing lazy
expression machinery in reference and fallback quest execution. The contract
comes from the [xNVSE command](https://github.com/xNVSE/NVSE/blob/6.4.9/nvse/nvse/Commands_Game.cpp)
and [loaded-name lookup](https://github.com/xNVSE/NVSE/blob/6.4.9/nvse/nvse/GameData.cpp).

## Verification and remaining boundaries

`NumericIniSettingProbe` checks relocated constructor and inline associations,
packed components, wrong singleton/payload/register/local refusal, signed and
unsigned values, Float32 widening, file/profile precedence, case handling,
nonnumeric shadowing, separate Renderer ownership, duplicate rejection and
non-finite values. The source-string probe executes both commands through both
script hosts, including declared string handles, inactive branches, missing and
present-but-unloaded files, independent graphs and cold retained failures.

The selected owned JDC audit retains the eleven independent game-setting
expectations and verifies the compiled numeric INI literal at local slot 15.
Its observed world FOV is 75, independently resolved from the admitted source
payload and winning file/profile rows. The initializer then executes its
loaded-plugin branch and suffix: 360 audit invocations retain no initializer
error. The registered main-loop callback still faults before completing its
equipment behavior. Owned executable and INI hashes remain unchanged.

Run the focused probes with `--test-numeric-ini` and `--test-source-string` on
`FalloutPluginRuntimeProbe`; the selected source audit remains
`--audit-jdc-game-settings`. The ordinary full repository gate also runs both
synthetic groups. Private source reports belong under ignored local paths.

This block does not implement `SetNumericINISetting`, bind camera consumers,
complete equipment/extra-reference behavior, replay already faulted saved
scripts, or establish a working JAM module. MCM, all nine modules together,
ordinary flat/OpenXR use, cold gameplay acceptance and matched retail evidence
remain separate requirements. Installed package admission and successful source
initialization are distinct from module support.
