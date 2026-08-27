# Fallout: New Vegas retail material/shader contract

This is a clean-room observation contract for OpenNV's Godot material path. It
records behavior from a legally owned retail install; no retail shader bytecode,
texture, mesh, or executable data belongs in Git or in an OpenNV release.

## Source and boundary

- Observed package: `Data/Shaders/shaderpackage019.sdp`
- SHA-256: `31f7f53020692172f837207023f0f471a5097b453bf3ee35070081c774ada0c8`
- Package header: package `100`, 1,007 named Direct3D 9 shader programs
- Verification: package records were bounds-checked to the exact source length;
  selected owned pixel/vertex programs were disassembled locally with the
  Microsoft Direct3D shader disassembler.

The derived equations and register roles below are compatibility facts. The
compiled programs remain private retail evidence.

## Recovered behavior used by OpenNV

The SLS environment pass reconstructs a tangent-space normal, reflects the view
direction, samples the authored cubemap, chooses the custom environment mask or
normal-map alpha, multiplies by the authored environment scale, applies vertex
color when enabled, and fades with the CELL depth-fog curve. It is a separate
material pass. Environment texture slots are inactive unless the NIF shader flag
enables environment mapping. Materials requiring the separate retail
`envmap-light-fade` term stay off that pass until that term is recovered.

The ordinary lighting family samples base and normal maps, applies radial light
attenuation and CELL ambient, then uses the material self-illum input. A white
`NiMaterialProperty.emissive_color` alone does not establish active self-illum:
the observed lamp and jukebox surfaces carry a
`NiMaterialColorController` targeting `SELF_ILLUM`, while ordinary safe geometry
with the same white material default does not. A controlled no-glow-map surface
replaces its lit result with the controller color instead of treating the base
texture as an emission map.

### Road diffuse and directional-light contract

The accepted unattended road probe is
`D:\Builds\OpenNV-actor-retail-sunny-observation-20260825-r25-road-lighting-constants`.
Its canonical run passed without Windows app control, foreground activation, or
foreground input; the retained JSONL SHA-256 is
`895e2d4f4b7d35d285b6430ae28de14963e4b6e0e85b23951fb6ccf738994eca`.
The probe matched the top mip of the owned
`textures\landscape\roads\roadwasteland01.dds` resource and retained 29 live
road draws from the frame that produces source frame 70.

The ordinary road draws select `SLS2001/2002.pso` (FNV-1a `d2b33434`) or
`SLS2017.pso` (FNV-1a `79ed2742`). Their shared diffuse core decodes the normal
map as `normalize((sample.rgb - 0.5) * 2)`, transforms the directional vector
through the authored tangent basis, and evaluates:

```text
ndotl = saturate(dot(surfaceNormal, surfaceToLight))
lighting = max(AmbientColor.rgb + PSLightColor[0].rgb * ndotl, 0)
litDiffuse = baseMap.rgb * vertexColor.rgb * lighting
```

The matched live constants are
`AmbientColor=(0.38697809, 0.469016731, 0.602245271, 1)` and
`PSLightColor[0]=(1.21000004, 1.07713735, 0.806666732, 0)`. The latter is the
owned weather sunlight color multiplied by the composed image-space sunlight
dimmer. `SLS2017` adds its authored alpha-test/specular/toggle terms; it does not
replace this diffuse core with a PBR response. The two `SLS2156.pso` draws are
the separate shadow-map filtering pass and remain a distinct renderer contract.

`SLS2001.vso` and `SLS2012.vso` consume the object-space light vector from
vertex constant `c25`. Repeated identity-basis road draws—including the
near-identity `wastelandroadcurvelong04r` and identity SCOL straight-road
placements—report
`(0.561507463, 0.585110784, 0.585110784)`. Applying OpenNV's proven
Gamebryo-to-Godot basis conversion yields the world-space surface-to-light
vector `(0.561507463, 0.585110784, -0.585110784)`. Godot's Y-X-Z Euler form for
that positive-Z light axis is
`(-35.81081905, 136.17927628, 0)` degrees. This replaces the former provisional
`(-48, -32, 0)` adapter; it is not a hand-tuned lighting value.

### FaceGen head pass

The retail FaceGen pixel program used by Sunny Smiles is identified by FNV-1a
32-bit hash `555808e8` (`1431832808` decimal). Clean-room disassembly and bound
resource observation recover this encoded-color operation:

```text
base = sample(s0)
detail = sample(s2) - 0.5
tone = sample(s3)
encodedRgb = 4 * (base.rgb + 2 * detail.rgb) * tone.rgb
```

The observed sampler roles are `s0 = BaseMap`, `s1 = NormalMap`,
`s2 = FaceGenMap0`, and `s3 = FaceGenMap1`. In the Sunny capture, the runtime
resource bound to `s0` decodes exactly to the female RACE head diffuse despite
an inherited male-path label; `s1` is the female RACE head normal, `s2` is
Sunny's NPC FaceGen detail, and `s3` is a repeated 1x1 RGBA value
`[62, 65, 62, 64]`. These are independent runtime inputs. OpenNV must not bake
them into a guessed diffuse texture during owned-data compilation.

Every observed FaceGen draw in all retained views reports
`D3DSAMP_SRGBTEXTURE = 0` for samplers `s0` through `s5` and
`D3DRS_SRGBWRITEENABLE = 0` for the target. Godot's albedo boundary therefore
performs one explicit, configuration-owned encoded-sRGB to linear conversion
after the retail encoded-color arithmetic. The conversion constants and their
provenance live in `runtime/config/open-nv-runtime-v1.json`; they are not actor
special cases.

The FaceGen surface is opaque and depth-writing. Assigning the sampled base
alpha to Godot `ALPHA` moved the face into the transparent pass and exposed the
otherwise correct mouth, teeth, and tongue geometry through the skin, creating
a false grin. `RetailFaceGenMaterial.cs` consequently rejects any FaceGen
sidecar whose `alphaMode` is not `OPAQUE` and deliberately never writes shader
`ALPHA`.

The canonical color-space observation is
`D:\Builds\OpenNV-actor-retail-sunny-observation-20260825-r20-color-space`.
Its run summary passes the no-application-control policy, and the retained
JSONL SHA-256 is
`8df4e72583c5a04351473d973e7d1f66a0f88785bac0686a5f04010fdb88c891`.
The first opaque Godot recapture is
`D:\Builds\OpenNV-actor-godot-sunny-capture-20260825-r13-opaque-face`;
its engine report remains `captured-provisional-light-direction`, so this
material result is not a pixel-parity pass.

### Atmosphere and cloud dome

The live atmosphere draw is the player's owned `SKY.vso` plus `SKY.pso`
pair. Their bytecode hashes are respectively `9e65c2ca` (384 bytes) and
`15f812a0` (196 bytes). The vertex shader does not derive a color ramp from
height. It consumes the authored `atmosphere.nif` D3DCOLOR and evaluates:

```text
sky.rgb = vertexColor.r * horizon.rgb
        + vertexColor.g * skyLower.rgb
        + vertexColor.b * skyUpper.rgb
sky.a = vertexColor.a
output.rgb = sky.rgb * Params.y
output.a = sky.a
```

At Sunny source frame 70, all three observed atmosphere draws agree on the
same live constants: horizon `[0.866666734, 0.913725555, 0.925490260]`, lower
sky `[0.360784322, 0.450980425, 0.580392182]`, upper sky
`[0.280125320, 0.354837626, 0.621099293]`, and `Params.y = 0.880000055`.
They also agree on back-face culling, enabled depth test with no depth write,
ordinary source-alpha blending, RGB-only writes, disabled fog, and disabled
sRGB writes. The accepted unattended contract is
`D:\Builds\OpenNV-actor-retail-sunny-observation-20260825-r41-atmosphere-shader-contract`;
its JSONL SHA-256 is
`2d75415acce182d61a47d6167a93443cc115127420212f0edc62988fc3f9b2e5`.

The cloud dome uses `SKYCLOUDS.vso` (`aeb06784`, 500 bytes) and `SKYTEX.pso`
(`7c116e65`, 576 bytes). The live 341-vertex draw supplies cloud-layer color,
lower-sky color, and upper-sky color as the same authored RGB weights, uses
the same `0.880000055` RGB multiplier, and binds the owned
`sky\\nvcloudlight.dds` with sRGB sampling disabled. With the observed
`Params.x = 0`, the two-sampler pixel program reduces to the source cloud
sample, except that a sample whose red channel is exactly zero has alpha
forced to zero. The accepted cloud contract is
`D:\Builds\OpenNV-actor-retail-sunny-observation-20260825-r40-cloud-shader-contract`;
its JSONL SHA-256 is
`44c20b13e19c5075bdc5b0e9bc3ffb0586c2c09cd5e7b96d6a41d16baea5527b`.

OpenNV therefore uses the owned atmosphere/cloud mesh vertex colors directly.
The former configurable atmosphere height bands were guesses and are removed.

### HDR image-space join

The final retail HDR/cinematic program is entry 898,
`ISHDRBLENDINSHADERCIN.pso`, in the player's owned
`shaderpackage013.sdp`. Its private 748-byte program has FNV-1a 32-bit hash
`0a008802` (`167806978` decimal). The clean-room disassembly identifies
`s0 = Src0` and `s1 = DestBlend`; a bounded pre-draw readback at the matched
Sunny source frame proves their concrete roles:

- stage 0 is a 256x256 `D3DFMT_A16B16G16R16F` blurred bloom/adaptation input;
- stage 1 is the 1280x720 `D3DFMT_A16B16G16R16F` full-resolution HDR scene;
- both sampler sRGB states and the render-target sRGB-write state are disabled.

For each output pixel, retail linearly samples both inputs and evaluates this
derived RGB equation before the existing cinematic, tint, and fade terms:

```text
normalizer = max(blurred.a, HDRParam.x)
joined = hdrScene.rgb * HDRParam.x / normalizer
       + max(blurred.rgb * 0.5 / normalizer, 0)
luminance = dot(joined, [0.299, 0.587, 0.114])
saturated = lerp(luminance, joined, Cinematic.x)
tinted = lerp(saturated, luminance * Tint.rgb, Tint.a)
contrasted = ((tinted * Cinematic.w) - Cinematic.y) * Cinematic.z
           + Cinematic.y
output = lerp(contrasted, Fade.rgb, Fade.a)
```

The accepted unattended evidence is
`D:\Builds\OpenNV-actor-retail-sunny-observation-20260825-r22-hdr-inputs`.
Its run and classified report pass with no Windows app control, foreground
activation, or foreground input. The stage-0 artifact SHA-256 is
`3619a5974ffe11f671de8fa74b58cad42ea2fc8014b8243faff88395d0967f7b`;
the stage-1 artifact SHA-256 is
`40b8000a74ad237f02913c9f15fed2dc8ad8d5c6d9205356528b76b7c62c0cf6`.
The v6 private comparison contract is
`D:\Builds\OpenNV-actor-review-sunny-contract-20260825-r5-hdr-inputs-v6`
with SHA-256
`d511392845d0833d39e2ca8b0117a1afcf3b6c726560213d6cb16db06f0d42a5`.
It retains the immutable artifact descriptors and hashes without copying either
retail buffer into an OpenNV cache or release.

An offline replay of the recovered program against retail source frame 70
proves the boundary: 96.8916% of RGB components are byte-identical and every
component is within one 8-bit code value. Mean absolute error is 0.03108 code
values. This closes buffer roles, RGBA half-float ordering, linear output
transfer, and the final join equation; it does not claim that Godot yet
generates the matching upstream HDR scene or blurred adaptation buffer.

## Runtime mapping

- `NiAlphaProperty` flags determine opaque, blend, or alpha-test mode and cutoff.
- Vertex RGB/alpha are enabled only by their retail shader flags.
- `BSShaderNoLightingProperty.file_name` and legacy `NiTexturingProperty` sources
  are retained.
- NIF UV coordinates are retained without a V inversion. The decoded DDS/PNG
  row order and Godot glTF sampler already preserve the authored convention;
  an extra inversion was proven wrong by the intact pool-table atlas, whose
  felt occupies the source texture's upper half.
- DDS cubemaps retain all six faces and are converted from Gamebryo Z-up face
  order to Godot Y-up order.
- Material binding uses the imported NIF/glTF surface name, never list position.
- FaceGen base, normal, NPC detail, and tone inputs remain separate until the
  runtime material pass; FaceGen surfaces remain opaque and depth-writing.
- CELL fog color, near/far depth, and power drive Godot depth fog.
- Cell scene v5 and actor scene v3 reject older incomplete contracts. Actor v3
  also hashes the glTF, sidecar, and binary buffer independently.

## Still failing closed

Retail external-emittance color, environment-map light fade, the complete
non-PBR point-light equation, FaceGen lighting, Godot generation of the retail
HDR adaptation/bright-pass/bloom inputs, and the in-world matched differential
remain open. Current Godot frames are renderer diagnostics, not retail-fidelity
proof.
