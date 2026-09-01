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

test("New Vegas uses an explicit immutable cache registration", () => {
  const main = readFileSync(new URL("../src/main.mjs", import.meta.url), "utf8");
  const html = readFileSync(new URL("../src/renderer/index.html", import.meta.url), "utf8");
  assert.match(main, /opennv-launcher-owned-cache-registration\/v1/u);
  assert.match(main, /campaign:\s*"NewVegas"/u);
  assert.match(main, /OPENNV_NEWVEGAS_CACHE_ROOT/u);
  assert.match(html, /id="choose-newvegas-cache"/u);
  assert.match(main, /opennv-cell-scene\/v14/u);
  assert.doesNotMatch(main, /opennv-cell-scene\/v13/u);
});

test("Fallout 2 enables only the matching owned-cache Hex first slice", () => {
  const base = createOfflineState({ platform: "win32" });
  const profile = {
    ready: true,
    runtimeReady: true,
    validated: true,
    message: "Ready: bounded Fallout 2 Hex first slice.",
    templeCache: "D:\\cache\\fo2-temple.json",
    templeTransitions: "D:\\profiles\\fo2-transitions.json",
    arroyoCache: "D:\\cache\\fo2-arroyo.json",
    playerCache: "D:\\cache\\fo2-player.json",
    characterStartCache: "D:\\cache\\fo2-character-start.json",
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
    "--fo2-temple-cache", profile.templeCache,
    "--fo2-temple-transitions", profile.templeTransitions,
    "--fo2-arroyo-cache", profile.arroyoCache,
    "--fo2-player-cache", profile.playerCache,
    "--fo2-character-start-cache", profile.characterStartCache,
    "--fo2-save", profile.savePath
  ]);
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
    cacheRoot: "D:\\cache\\newvegas-vanilla",
    savePath: "D:\\profiles\\courier-v1.json"
  };
  const flat = validateLaunchRequest({ campaign: "newvegas" });
  assert.deepEqual(createRuntimeArguments(flat, { newVegasProfile: profile }), [
    "--xr-mode", "off", "--", "--reuse-cache",
    "--cache-root", profile.cacheRoot,
    "--opening-menu", "--save-path", profile.savePath
  ]);
  const xr = validateLaunchRequest({ campaign: "newvegas", enableVr: true });
  assert.deepEqual(
    createRuntimeArguments(xr, { newVegasProfile: profile }),
    ["--xr-mode", "on", "--", "--reuse-cache",
      "--cache-root", profile.cacheRoot,
      "--opening-menu", "--save-path", profile.savePath, "--vr"]
  );
});

test("bounded New Vegas hot-play profile is explicit in the runtime handoff", () => {
  const profile = {
    ready: true,
    cacheRoot: "D:\\cache\\newvegas-bounded-default",
    savePath: "D:\\profiles\\courier-v1.json",
    boundedDefaultProfile: true
  };
  const flat = validateLaunchRequest({ campaign: "newvegas" });
  assert.deepEqual(createRuntimeArguments(flat, { newVegasProfile: profile }), [
    "--xr-mode", "off", "--", "--reuse-cache",
    "--cache-root", profile.cacheRoot,
    "--opening-menu", "--save-path", profile.savePath,
    "--bounded-default-profile"
  ]);
});

test("standalone routes cannot inherit TTW sources or each other's saves", () => {
  const newVegas = {
    ready: true,
    cacheRoot: "D:\\cache\\newvegas-vanilla",
    savePath: "D:\\saves\\newvegas\\courier-v1.json"
  };
  const fallout3 = {
    ready: true,
    path: "D:\\profiles\\fallout3-vanilla.json",
    savePath: "D:\\saves\\fallout3\\cg00-character-v1.json"
  };
  const ttw = {
    ready: true,
    path: "D:\\profiles\\ttw-profile.json",
    savePath: "D:\\saves\\ttw\\courier-v1.json"
  };
  const nvArgs = createRuntimeArguments(
    validateLaunchRequest({ campaign: "newvegas" }),
    { newVegasProfile: newVegas, fallout3Profile: fallout3, ttwProfile: ttw });
  assert.deepEqual(nvArgs, [
    "--xr-mode", "off", "--", "--reuse-cache",
    "--cache-root", newVegas.cacheRoot,
    "--opening-menu", "--save-path", newVegas.savePath
  ]);
  assert.equal(nvArgs.includes(ttw.path), false);
  assert.equal(nvArgs.includes(fallout3.path), false);

  const fo3Args = createRuntimeArguments(
    validateLaunchRequest({ campaign: "fallout3" }),
    { newVegasProfile: newVegas, fallout3Profile: fallout3, ttwProfile: ttw });
  assert.deepEqual(fo3Args, [
    "--xr-mode", "off", "--",
    "--fo3-profile", fallout3.path,
    "--save-path", fallout3.savePath
  ]);
  assert.equal(fo3Args.includes(newVegas.cacheRoot), false);
  assert.equal(fo3Args.includes(ttw.path), false);
});

test("Fallout 1 launches the registered local contracts into the selected presentation", () => {
  const profile = {
    ready: true,
    hexScene: "D:\\cache\\hex-scene.json",
    characterStart: "D:\\cache\\character-start.json",
    characterStartSha256: "a".repeat(64),
    savePath: "D:\\profiles\\vault-dweller-v1.json"
  };
  const request = validateLaunchRequest({ campaign: "fallout1", presentation: "hex-tactical" });
  assert.deepEqual(createRuntimeArguments(request, { fallout1Profile: profile }), [
    "--xr-mode", "off", "--rendering-method", "gl_compatibility", "--",
    "--fo1-hex-scene", profile.hexScene,
    "--fo1-new-game",
    "--fo1-character-start", profile.characterStart,
    "--fo1-character-start-sha256", profile.characterStartSha256,
    "--fo1-start-presentation", "hex-tactical",
    "--save-path", profile.savePath
  ]);
  assert.throws(
    () => validateLaunchRequest({ campaign: "fallout1", enableVr: true }),
    /OpenXR is not available/
  );
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
