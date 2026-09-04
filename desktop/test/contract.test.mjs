import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  CAMPAIGNS,
  createOfflineState,
  createRuntimeArguments,
  createTtwOpeningProofArguments,
  mergeRuntimeState,
  validateLaunchRequest
} from "../src/contract.mjs";

const NATIVE_STACK_ID = "d".repeat(64);

test("the launcher has four top-level games while TTW remains an edition", () => {
  const state = createOfflineState({ platform: "linux" });
  assert.deepEqual(state.campaigns.map((campaign) => campaign.id), ["fallout1", "fallout2", "newvegas", "fallout3", "ttw"]);
  assert.deepEqual(
    state.campaigns.filter((campaign) => !campaign.ttw).map((campaign) => campaign.id),
    ["fallout1", "fallout2", "newvegas", "fallout3"]
  );
  assert.deepEqual(
    state.campaigns.find((campaign) => campaign.id === "fallout1").presentations,
    ["hex-tactical", "first-person"]
  );
  assert.deepEqual(
    state.campaigns.find((campaign) => campaign.id === "fallout2").pendingPresentations,
    ["first-person", "openxr"]
  );
  for (const id of ["fallout1", "fallout2", "newvegas", "fallout3"]) {
    assert.match(
      state.campaigns.find((campaign) => campaign.id === id).launcherSummary,
      /FPS · Hex · VR/
    );
  }
  assert.deepEqual(state.campaigns.find((campaign) => campaign.id === "fallout2").presentations, ["hex-tactical"]);
  assert.equal(state.campaigns.find((campaign) => campaign.id === "ttw").ttw, true);
  assert.match(state.campaignRule, /before creating a character/i);
});

test("the compact renderer starts with one readable four-card row and two columns when narrow", () => {
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  const styles = readFileSync(new URL("../src/renderer/styles.css", import.meta.url), "utf8");
  const renderer = readFileSync(new URL("../src/renderer/app.mjs", import.meta.url), "utf8");
  assert.equal((html.match(/class="campaign-card /gu) || []).length, 4);
  for (const title of ["Fallout 1", "Fallout 2", "New Vegas", "Fallout 3"]) {
    assert.match(html, new RegExp(`card-title">${title.replace(" ", "\\s")}`));
  }
  assert.doesNotMatch(html, /card-title">TTW/);
  assert.match(styles, /grid-template-columns:\s*repeat\(4,/u);
  assert.match(styles, /@media \(max-width: 760px\)[\s\S]*grid-template-columns:\s*repeat\(2,/u);
  assert.match(renderer, /value="ttw-fnv"/u);
  assert.match(renderer, /value="ttw-fo3"/u);
  assert.match(renderer, /campaign:\s*selectedRouteId\(\)/u);
  assert.doesNotMatch(renderer, /campaign:\s*selectedCampaign\(\)\.id/u);
});

test("New Vegas uses an explicit native owned-Data registration", () => {
  const main = readFileSync(new URL("../src/main.mjs", import.meta.url), "utf8");
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  assert.match(main, /opennv-launcher-owned-data-registration\/v1/u);
  assert.match(main, /campaign:\s*"NewVegas"/u);
  assert.match(main, /OPENNV_NEWVEGAS_DATA_ROOT/u);
  assert.match(html, /id="choose-newvegas-data"/u);
  assert.doesNotMatch(main, /opennv-legal-asset-cache\/v1/u);
});

test("the desktop launch host has no dead standalone prepared-cache path plumbing", () => {
  const main = readFileSync(new URL("../src/main.mjs", import.meta.url), "utf8");
  for (const legacyName of [
    "fo2SlicePaths", "templeCache", "arroyoCache", "playerCache", "characterStartCache"
  ]) {
    assert.doesNotMatch(main, new RegExp(`\\b${legacyName}\\b`, "u"));
  }
});

test("standalone Fallout 3 uses launcher-owned Data and native-stack registration", () => {
  const main = readFileSync(new URL("../src/main.mjs", import.meta.url), "utf8");
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  const preload = readFileSync(new URL("../src/preload.cjs", import.meta.url), "utf8");
  assert.match(main, /opennv:choose-fallout3-data/u);
  assert.match(main, /createOwnedFallout3Stack/u);
  assert.match(main, /campaign:\s*"Fallout3"/u);
  assert.match(main, /fallout3-data-registration\.json/u);
  assert.match(html, /id="choose-fallout3-data"/u);
  assert.match(preload, /chooseFallout3Data/u);
  assert.doesNotMatch(main, /OPENNV_FO3_PROFILE|fallout3-profile\.json/u);
});

test("the launcher registers generic read-only mod folders for native resolution", () => {
  const main = readFileSync(new URL("../src/main.mjs", import.meta.url), "utf8");
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  const preload = readFileSync(new URL("../src/preload.cjs", import.meta.url), "utf8");
  assert.match(main, /opennv:add-mod-source-root/u);
  assert.match(main, /read-only native source layers registered/u);
  assert.match(html, /id="add-mod-source-root"/u);
  assert.match(preload, /addModSourceRoot/u);
});

test("Gate Vortex exposes a bounded local ZIP installer without claiming scripted FOMOD or downloads", () => {
  const main = readFileSync(new URL("../src/main.mjs", import.meta.url), "utf8");
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  const preload = readFileSync(new URL("../src/preload.cjs", import.meta.url), "utf8");
  assert.match(main, /opennv:install-local-mod-archive/u);
  assert.match(main, /provider:\s*"gate-vortex"/u);
  assert.match(html, /id="install-local-mod-archive"/u);
  assert.match(html, /id="managed-mod-layers"/u);
  assert.match(preload, /installLocalModArchive/u);
  assert.match(preload, /manageModLayer/u);
  assert.match(main, /opennv:manage-mod-layer/u);
  assert.doesNotMatch(html, /FOMOD|download/u);
  assert.match(main, /Fallout 1 mod layers are blocked/u);
  assert.match(main, /Fallout 2 mod layers are blocked/u);
  assert.match(main, /installedModsRoot\(game\)/u);
});

test("Fallout 2 native launch passes only its owned profile and save boundary", () => {
  const base = createOfflineState({ platform: "win32" });
  const profile = {
    ready: true,
    runtimeReady: true,
    validated: true,
    message: "Ready: bounded Fallout 2 Hex first slice.",
    path: "D:\\profiles\\fallout2-owned.json",
    savePath: "D:\\saves\\fo2-character-arroyo.json"
  };
  const merged = mergeRuntimeState(base, {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{
      id: "Fallout2",
      variants: {
        arroyo: {
          ready: true,
          presentations: {
            "hex-tactical": { ready: true },
            "first-person": { ready: false },
            openxr: { ready: false }
          }
        }
      }
    }]
  }, { fallout2Profile: profile });
  const fallout2 = merged.campaigns.find((campaign) => campaign.id === "fallout2");
  assert.equal(fallout2.ready, true);
  assert.deepEqual(fallout2.presentations, ["hex-tactical"]);
  const request = validateLaunchRequest({ campaign: "fallout2", presentation: "hex-tactical" });
  assert.deepEqual(createRuntimeArguments(request, { fallout2Profile: profile }), [
    "--xr-mode", "off", "--windowed", "--resolution", "1280x720",
    "res://src/Campaigns/Fallout2/CharacterStart/Fo2CharacterStart.tscn", "--",
    "--fo2-owned-profile", profile.path,
    "--fo2-save", profile.savePath
  ]);
  assert.equal(createRuntimeArguments(request, { fallout2Profile: profile })
    .some((value) => /cache|python/iu.test(value)), false);
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout2", presentation: "first-person" }),
    /not available/i
  );
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout2", presentation: "openxr" }),
    /not available/i
  );
});

test("flat and OpenXR launches separate engine and game arguments", () => {
  const profile = {
    ready: true,
    dataRoot: "D:\\games\\Fallout New Vegas\\Data",
    stackId: NATIVE_STACK_ID,
    savePath: "D:\\profiles\\courier-v1.json"
  };
  const modStack = {
    validated: true, stackId: NATIVE_STACK_ID, path: "D:\\profiles\\mod-stack.json",
    sha256: "a".repeat(64)
  };
  const flat = validateLaunchRequest({ campaign: "newvegas" });
  assert.deepEqual(createRuntimeArguments(flat, { newVegasProfile: profile, modStack }), [
    "--xr-mode", "off", "--",
    "--source-stack", modStack.path,
    "--source-stack-sha256", modStack.sha256,
    "--stack-id", modStack.stackId,
    "--campaign", "fallout-new-vegas",
    "--opening-menu", "--save-path", profile.savePath
  ]);
  const xr = validateLaunchRequest({ campaign: "newvegas", enableVr: true });
  assert.deepEqual(
    createRuntimeArguments(xr, { newVegasProfile: profile, modStack }),
    ["--xr-mode", "on", "--",
      "--source-stack", modStack.path,
      "--source-stack-sha256", modStack.sha256,
      "--stack-id", modStack.stackId,
      "--campaign", "fallout-new-vegas",
      "--opening-menu", "--save-path", profile.savePath, "--vr"]
  );
});

test("New Vegas native launch fails closed without its matching stack identity", () => {
  const profile = {
    ready: true,
    dataRoot: "D:\\games\\Fallout New Vegas\\Data",
    stackId: NATIVE_STACK_ID,
    savePath: "D:\\profiles\\courier-v1.json"
  };
  const flat = validateLaunchRequest({ campaign: "newvegas" });
  assert.throws(() => createRuntimeArguments(flat, { newVegasProfile: profile }), /unavailable/u);
  assert.throws(() => createRuntimeArguments(flat, {
    newVegasProfile: profile,
    modStack: { validated: true, stackId: "different", path: "D:\\profiles\\mod-stack.json" }
  }), /unavailable/u);
});

test("a validated mod stack reaches the native owned-source resolver", () => {
  const profile = {
    ready: true,
    dataRoot: "D:\\games\\Fallout New Vegas\\Data",
    stackId: NATIVE_STACK_ID,
    savePath: "D:\\profiles\\stacks\\stack-a\\courier-v1.json"
  };
  const modStack = {
    validated: true,
    stackId: NATIVE_STACK_ID,
    path: "D:\\profiles\\newvegas\\mod-stack.json",
    sha256: "b".repeat(64)
  };
  const args = createRuntimeArguments(
    validateLaunchRequest({ campaign: "newvegas" }),
    { newVegasProfile: profile, modStack });
  assert.equal(args[args.indexOf("--source-stack") + 1], modStack.path);
  assert.equal(args[args.indexOf("--campaign") + 1], "fallout-new-vegas");
  assert.equal(args[args.indexOf("--save-path") + 1], profile.savePath);
});

test("standalone routes cannot inherit TTW sources or each other's saves", () => {
  const newVegas = {
    ready: true,
    dataRoot: "D:\\games\\Fallout New Vegas\\Data",
    stackId: NATIVE_STACK_ID,
    savePath: "D:\\saves\\newvegas\\courier-v1.json"
  };
  const modStack = {
    validated: true, stackId: NATIVE_STACK_ID, path: "D:\\profiles\\mod-stack.json",
    sha256: "c".repeat(64)
  };
  const fallout3 = {
    ready: true,
    path: "D:\\profiles\\fallout3-vanilla.json",
    dataRoot: "D:\\games\\fallout3\\Data",
    stackPath: "D:\\profiles\\fallout3-native-mod-stack.json",
    stackId: "e".repeat(64),
    stackSha256: "f".repeat(64),
    savePath: "D:\\saves\\fallout3\\native\\campaign-v1.json"
  };
  const ttw = {
    ready: true,
    path: "D:\\profiles\\ttw-profile.json",
    savePath: "D:\\saves\\ttw\\courier-v1.json"
  };
  const nvArgs = createRuntimeArguments(
    validateLaunchRequest({ campaign: "newvegas" }),
    { newVegasProfile: newVegas, fallout3Profile: fallout3, ttwProfile: ttw, modStack });
  assert.deepEqual(nvArgs, [
    "--xr-mode", "off", "--",
    "--source-stack", modStack.path,
    "--source-stack-sha256", modStack.sha256,
    "--stack-id", modStack.stackId,
    "--campaign", "fallout-new-vegas",
    "--opening-menu", "--save-path", newVegas.savePath
  ]);
  assert.equal(nvArgs.includes(ttw.path), false);
  assert.equal(nvArgs.includes(fallout3.path), false);

  const fo3Args = createRuntimeArguments(
    validateLaunchRequest({ campaign: "fallout3" }),
    { newVegasProfile: newVegas, fallout3Profile: fallout3, ttwProfile: ttw });
  assert.deepEqual(fo3Args, [
    "--xr-mode", "off", "--",
    "--source-stack", fallout3.stackPath,
    "--source-stack-sha256", fallout3.stackSha256,
    "--stack-id", fallout3.stackId,
    "--campaign", "fallout-3",
    "--opening-menu",
    "--save-path", fallout3.savePath
  ]);
  assert.equal(fo3Args.includes(newVegas.dataRoot), false);
  assert.equal(fo3Args.includes(ttw.path), false);
});

test("Fallout 1 launches only the registered native owned profile", () => {
  const profile = {
    ready: true,
    path: "D:\\profiles\\fallout1-owned.json",
    savePath: "D:\\profiles\\vault-dweller-v1.json"
  };
  const request = validateLaunchRequest({ campaign: "fallout1", presentation: "hex-tactical" });
  assert.deepEqual(createRuntimeArguments(request, { fallout1Profile: profile }), [
    "--xr-mode", "off", "--rendering-method", "gl_compatibility", "--",
    "--fo1-owned-profile", profile.path,
    "--fo1-start-presentation", "hex-tactical",
    "--save-path", profile.savePath
  ]);
  assert.equal(createRuntimeArguments(request, { fallout1Profile: profile })
    .some((value) => /cache|hex-scene|character-start|python/iu.test(value)), false);
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout1", enableVr: true }),
    /OpenXR is not available/
  );
});

test("all four standalone games keep profile and save arguments isolated", () => {
  const profiles = {
    fallout1Profile: { ready: true, path: "D:\\profiles\\fo1.json", savePath: "D:\\saves\\fo1.json" },
    fallout2Profile: { ready: true, validated: true, path: "D:\\profiles\\fo2.json", savePath: "D:\\saves\\fo2.json" },
    newVegasProfile: {
      ready: true, dataRoot: "D:\\games\\fnv\\Data", stackId: "1".repeat(64),
      savePath: "D:\\saves\\fnv-stack.json"
    },
    fallout3Profile: {
      ready: true, dataRoot: "D:\\games\\fo3\\Data", stackPath: "D:\\profiles\\fo3-stack.json",
      stackId: "2".repeat(64), stackSha256: "3".repeat(64), savePath: "D:\\saves\\fo3-stack.json"
    },
    modStack: {
      validated: true, stackId: "1".repeat(64), path: "D:\\profiles\\fnv-stack.json",
      sha256: "4".repeat(64)
    }
  };
  const requests = [
    validateLaunchRequest({ campaign: "fallout1", presentation: "hex-tactical" }),
    validateLaunchRequest({ campaign: "fallout2", presentation: "hex-tactical" }),
    validateLaunchRequest({ campaign: "newvegas" }),
    validateLaunchRequest({ campaign: "fallout3" })
  ];
  const expectedSaves = [
    profiles.fallout1Profile.savePath,
    profiles.fallout2Profile.savePath,
    profiles.newVegasProfile.savePath,
    profiles.fallout3Profile.savePath
  ];
  for (const [index, request] of requests.entries()) {
    const args = createRuntimeArguments(request, profiles);
    assert.equal(args.includes(expectedSaves[index]), true);
    for (const [otherIndex, save] of expectedSaves.entries()) {
      if (otherIndex !== index) assert.equal(args.includes(save), false);
    }
  }
});

test("JAM is modular only for the supported character paths", () => {
  assert.equal(validateLaunchRequest({ campaign: "newvegas", enableJam: true }).enableJam, true);
  assert.throws(() => validateLaunchRequest({ campaign: "fallout3", enableJam: true }), /New Vegas and TTW/);
  assert.equal(validateLaunchRequest({ campaign: "newvegas", enableVr: true }).enableVr, true);

  const merged = mergeRuntimeState(createOfflineState({ platform: "win32" }), {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{
      id: "Fallout3",
      variants: {
        vanilla: { ready: true, presentations: { "first-person": { ready: true } } },
        jam: { ready: true }
      }
    }]
  }, {
    fallout3Profile: { ready: true, message: "Registered" },
    jamProfile: { ready: true, message: "Registered" }
  });
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout3").jamReady, false);
});

test("TTW and JAM remain disabled until both registered profiles report portable runtime readiness", () => {
  const runtime = {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{
      id: "TTW",
      variants: {
        vanilla: { ready: true, presentations: { "first-person": { ready: true } } },
        jam: { ready: true }
      }
    }]
  };
  const withoutProfiles = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime);
  assert.equal(withoutProfiles.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  assert.equal(withoutProfiles.campaigns.find((campaign) => campaign.id === "ttw").jamReady, false);

  const withProfiles = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime, {
    ttwProfile: { ready: true, message: "Registered" },
    jamProfile: { ready: true, message: "Registered" }
  });
  assert.equal(withProfiles.campaigns.find((campaign) => campaign.id === "ttw").ready, true);
  assert.equal(withProfiles.campaigns.find((campaign) => campaign.id === "ttw").jamReady, true);
});

test("TTW opening routes remain distinct and interactive Play cannot invoke proof-and-quit", () => {
  const ttwProfile = {
    validated: true,
    path: "D:\\profiles\\ttw-profile.json",
    sourceNamespacePath: "D:\\profiles\\ttw-effective-source.json",
    cacheCompatibilityId: `ttw-fo3-opening:${"a".repeat(64)}`,
    cacheRoot: `D:\\cache\\ttw\\${"b".repeat(64)}\\${"a".repeat(64)}`,
    saveCompatibilityId: `ttw:${"b".repeat(64)}`,
    savePath: "D:\\profiles\\ttw\\courier-v1.json",
    openings: {
      "ttw-fo3": {
        proofValidated: true,
        proofProfilePath: "D:\\profiles\\ttw-fo3-opening-profile.json",
        interactiveReady: false,
        blocker: "Vault 101 world runtime is not connected."
      },
      "ttw-fnv": {
        proofValidated: false,
        proofProfilePath: null,
        interactiveReady: false,
        blocker: "Doc Mitchell TTW opening is not compiled."
      }
    }
  };
  const jamProfile = { ready: true, path: "D:\\profiles\\jam-profile.json" };
  const fo3 = validateLaunchRequest({ campaign: "ttw-fo3", enableJam: true });
  const fnv = validateLaunchRequest({ campaign: "ttw-fnv" });
  assert.equal(fo3.ttwOpening, "ttw-fo3");
  assert.equal(fnv.ttwOpening, "ttw-fnv");
  assert.throws(() => validateLaunchRequest({ campaign: "ttw" }), /Choose Fallout 3 via TTW/);
  assert.throws(
    () => createRuntimeArguments(fo3, { ttwProfile, jamProfile }),
    /Vault 101 world runtime/
  );
  assert.throws(
    () => createRuntimeArguments(fnv, { ttwProfile }),
    /Doc Mitchell TTW opening/
  );
  assert.deepEqual(createTtwOpeningProofArguments("ttw-fo3", ttwProfile, {
    mode: "apply",
    reportPath: "D:\\proofs\\ttw-fo3-apply.json"
  }), [
    "--xr-mode", "off", "--",
    "--ttw-fo3-opening-profile", ttwProfile.openings["ttw-fo3"].proofProfilePath,
    "--ttw-fo3-opening-proof", "apply",
    "--save-path", ttwProfile.savePath,
    "--report", "D:\\proofs\\ttw-fo3-apply.json"
  ]);
  assert.throws(
    () => createTtwOpeningProofArguments("ttw-fnv", ttwProfile, {
      mode: "apply",
      reportPath: "D:\\proofs\\ttw-fnv-apply.json"
    }),
    /no bounded Doc Mitchell proof profile/i
  );
});

test("standalone, TTW, and optional JAM selections form a fail-closed scenario matrix", () => {
  assert.equal(validateLaunchRequest({ campaign: "newvegas" }).routeId, "newvegas");
  assert.equal(validateLaunchRequest({ campaign: "newvegas", enableJam: true }).enableJam, true);
  assert.equal(validateLaunchRequest({ campaign: "fallout3" }).routeId, "fallout3");
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout3", enableJam: true }),
    /New Vegas and TTW/
  );
  assert.deepEqual(
    ["ttw-fo3", "ttw-fnv"].map((campaign) => {
      const plain = validateLaunchRequest({ campaign });
      const jam = validateLaunchRequest({ campaign, enableJam: true });
      return [plain.ttwOpening, plain.enableJam, jam.enableJam];
    }),
    [["ttw-fo3", false, true], ["ttw-fnv", false, true]]
  );
});

test("a Godot runtime manifest augments rather than replaces product campaign rules", () => {
  const base = createOfflineState({ platform: "win32" });
  const merged = mergeRuntimeState(base, {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{
      id: "NewVegas",
      variants: {
        vanilla: {
          ready: true,
          presentations: { "first-person": { ready: true } },
          unavailableDlc: []
        }
      }
    }]
  }, {
    newVegasProfile: { ready: true, message: "Registered" }
  });
  assert.equal(merged.runtime.status, "ready");
  assert.equal(merged.runtime.canLaunch, true);
  assert.equal(merged.runtime.openXrLaunchable, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").jamReady, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  assert.equal(CAMPAIGNS.length, 5);
});

test("an experimental runtime connects without claiming campaigns are playable", () => {
  const merged = mergeRuntimeState(createOfflineState({ platform: "linux" }), {
    runtime: { status: "experimental", label: "Static geometry only", canLaunch: false },
    campaigns: []
  });
  assert.equal(merged.runtime.status, "experimental");
  assert.equal(merged.runtime.canLaunch, false);
  assert.match(merged.runtime.label, /Static geometry/);
});

test("Fallout 1 becomes launcher-ready only when runtime capability and local profile are both present", () => {
  const runtime = {
    runtime: { status: "ready", label: "Godot runtime ready", canLaunch: true },
    campaigns: [{
      id: "Fallout1EtTu",
      variants: {
        vault13Concept: {
          ready: true,
          presentations: { "hex-tactical": { ready: true } }
        }
      }
    }]
  };
  const withoutProfile = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime);
  assert.equal(withoutProfile.campaigns.find((campaign) => campaign.id === "fallout1").ready, false);
  const withProfile = mergeRuntimeState(createOfflineState({ platform: "win32" }), runtime, {
    fallout1Profile: { ready: true, message: "Registered" }
  });
  assert.equal(withProfile.campaigns.find((campaign) => campaign.id === "fallout1").ready, true);
  assert.deepEqual(
    withProfile.campaigns.find((campaign) => campaign.id === "fallout1").presentations,
    ["hex-tactical"]
  );
});

test("the checked-in runtime keeps owned-data routes profile-gated", () => {
  const manifest = JSON.parse(readFileSync(new URL("../../runtime/runtime-manifest.json", import.meta.url), "utf8"));
  const merged = mergeRuntimeState(createOfflineState({ platform: "win32" }), manifest, {
    newVegasProfile: { ready: true, message: "Registered" }
  });
  assert.equal(merged.runtime.canLaunch, true);
  assert.equal(merged.runtime.openXrLaunchable, true);
  assert.equal(merged.runtime.openXrHardwareValidated, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").ready, true);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "newvegas").jamReady, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout1").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout2").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "fallout3").ready, false);
  assert.equal(merged.campaigns.find((campaign) => campaign.id === "ttw").ready, false);
  const ttwRuntime = manifest.campaigns.find((campaign) => campaign.id === "TTW").variants.vanilla;
  assert.equal(ttwRuntime.ready, false);
  assert.equal(ttwRuntime.requiresOpeningProfile, "opennv-ttw-fo3-opening-profile/v1");
  assert.match(ttwRuntime.message, /cold-restores all 38 admitted commands/);
  assert.match(ttwRuntime.message, /Vault 101 world and movie presentation/);

  const withFo2 = mergeRuntimeState(createOfflineState({ platform: "win32" }), manifest, {
    fallout2Profile: { ready: true, message: "Prepared owned Hex cache" }
  });
  assert.equal(withFo2.campaigns.find((campaign) => campaign.id === "fallout2").ready, true);
  assert.deepEqual(withFo2.campaigns.find((campaign) => campaign.id === "fallout2").presentations, ["hex-tactical"]);
});
