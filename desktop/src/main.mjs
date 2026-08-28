import { app, BrowserWindow, dialog, ipcMain, shell } from "electron";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, readSync, statSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createOfflineState, createRuntimeArguments, mergeRuntimeState, validateLaunchRequest } from "./contract.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const renderer = path.join(here, "renderer", "index.html");
const DAT2_FOOTER_BYTES = 8;
const HASH_READ_CHUNK_BYTES = 1024 * 1024;
const RUNTIME_CONFIG_JSON_INDENT = 2;
const SHA256_HEX_CHARACTERS = 64;

function productConfigurationPath() {
  return app.isPackaged
    ? path.join(process.resourcesPath, "config", "open-nv-runtime-v1.json")
    : path.join(here, "..", "..", "runtime", "config", "open-nv-runtime-v1.json");
}

function productConfiguration() {
  return JSON.parse(readFileSync(productConfigurationPath(), "utf8"));
}

function desktopLauncherPolicy() {
  const configuration = productConfiguration();
  const policy = configuration?.desktopLauncher;
  if (!policy) throw new Error("OpenNV desktop launcher policy is missing.");
  return policy;
}

function runtimeConfigPath() {
  return path.join(app.getPath("userData"), "runtime.json");
}

function fo1ProfileConfigPath() {
  return path.join(app.getPath("userData"), "fallout1-profile.json");
}

function fo2ProfileRegistrationPath() {
  return path.join(app.getPath("userData"), "fallout2-profile-registration.json");
}

function defaultFo2ProfilePath() {
  const localAppData = process.env.LOCALAPPDATA;
  return localAppData
    ? path.join(localAppData, "OpenNV", "profiles", "fallout2", "fallout2-profile.json")
    : path.join(app.getPath("userData"), "profiles", "fallout2", "fallout2-profile.json");
}

function configuredFo2ProfilePath() {
  if (process.env.OPENNV_FO2_PROFILE) return path.resolve(process.env.OPENNV_FO2_PROFILE);
  try {
    const registration = JSON.parse(readFileSync(fo2ProfileRegistrationPath(), "utf8"));
    if (registration?.schema === "opennv-launcher-owned-profile-registration/v1" &&
        registration?.campaign === "Fallout2" && typeof registration?.manifest === "string") {
      const registeredPath = path.resolve(registration.manifest);
      if (existsSync(registeredPath)) return registeredPath;
    }
  } catch {
    // Fall through to the documented LocalAppData profile location.
  }
  return defaultFo2ProfilePath();
}

function fo3ProfileConfigPath() {
  if (process.env.OPENNV_FO3_PROFILE) return process.env.OPENNV_FO3_PROFILE;
  const localAppData = process.env.LOCALAPPDATA;
  return localAppData
    ? path.join(localAppData, "OpenNV", "profiles", "fallout3", "vanilla", "fallout3-profile.json")
    : path.join(app.getPath("userData"), "profiles", "fallout3", "vanilla", "fallout3-profile.json");
}

function modProfileRegistrationPath(kind) {
  return path.join(app.getPath("userData"), `${kind}-profile-registration.json`);
}

function defaultModProfilePath(kind) {
  const localAppData = process.env.LOCALAPPDATA;
  return localAppData
    ? path.join(localAppData, "OpenNV", "profiles", `${kind}-profile.json`)
    : path.join(app.getPath("userData"), "profiles", `${kind}-profile.json`);
}

function configuredModProfilePath(kind) {
  const environmentPath = process.env[`OPENNV_${kind.toUpperCase()}_PROFILE`];
  if (environmentPath) return path.resolve(environmentPath);
  try {
    const registration = JSON.parse(readFileSync(modProfileRegistrationPath(kind), "utf8"));
    if (registration?.schema === "opennv-launcher-mod-profile-registration/v1" &&
        registration?.kind === kind && typeof registration?.manifest === "string") {
      const registeredPath = path.resolve(registration.manifest);
      if (existsSync(registeredPath)) return registeredPath;
    }
  } catch {
    // Fall through to the documented LocalAppData profile location.
  }
  return defaultModProfilePath(kind);
}

function sha256(filePath) {
  const digest = createHash("sha256");
  const handle = openSync(filePath, "r");
  const buffer = Buffer.allocUnsafe(HASH_READ_CHUNK_BYTES);
  try {
    for (;;) {
      const bytes = readSync(handle, buffer, 0, buffer.length, null);
      if (bytes === 0) break;
      digest.update(buffer.subarray(0, bytes));
    }
  } finally {
    closeSync(handle);
  }
  return digest.digest("hex");
}

function isSha256(value) {
  return typeof value === "string" &&
    value.length === SHA256_HEX_CHARACTERS && /^[0-9a-f]+$/u.test(value);
}

function validateHashBoundFile(filePath, row, label) {
  if (!existsSync(filePath)) throw new Error(`${label} is missing or moved.`);
  if (!Number.isSafeInteger(row?.bytes) || row.bytes < 1 || row.bytes !== statSync(filePath).size) {
    throw new Error(`${label} size changed; register the profile again.`);
  }
  if (!isSha256(row?.sha256) || sha256(filePath) !== row.sha256) {
    throw new Error(`${label} hash changed; register the profile again.`);
  }
}

function readTtwProfile(manifestOverride = null) {
  const manifestPath = path.resolve(manifestOverride || configuredModProfilePath("ttw"));
  const unavailable = (message, manifestDetected = existsSync(manifestPath)) => ({
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected,
    message,
    path: manifestPath
  });
  try {
    const profile = JSON.parse(readFileSync(manifestPath, "utf8"));
    if (profile?.schema !== "opennv-ttw-profile/v1" ||
        profile?.status !== "validated-generated-plugin-profile" ||
        profile?.kind !== "ttw" || !isSha256(profile?.pluginStackId) ||
        profile?.saveCompatibilityId !== `ttw:${profile.pluginStackId}` ||
        !Array.isArray(profile?.sourceRoots) || profile.sourceRoots.length === 0 ||
        !Array.isArray(profile?.plugins) || profile.plugins.length === 0) {
      return unavailable("The selected TTW manifest is not a validated generated profile.", true);
    }
    const roots = profile.sourceRoots.map((root) => path.resolve(root));
    for (const row of profile.plugins) {
      if (!Number.isInteger(row?.sourceRootIndex) || !roots[row.sourceRootIndex] ||
          typeof row?.file !== "string" || path.basename(row.file) !== row.file) {
        return unavailable("The selected TTW manifest has an invalid plugin source.", true);
      }
      validateHashBoundFile(path.join(roots[row.sourceRootIndex], row.file), row, `TTW plugin ${row.file}`);
    }
    const loadOrder = profile?.loadOrderSource;
    if (!loadOrder?.file || !isSha256(loadOrder?.sha256) ||
        !existsSync(loadOrder.file) || sha256(loadOrder.file) !== loadOrder.sha256) {
      return unavailable("The TTW active load order changed; register it again.", true);
    }
    const runtimeReady = profile?.runtimeCompatibility?.ready === true;
    const reason = String(profile?.runtimeCompatibility?.reason || "TTW runtime compatibility is not ready.");
    return {
      ready: runtimeReady,
      runtimeReady,
      validated: true,
      manifestDetected: true,
      message: runtimeReady
        ? "TTW profile and portable runtime compatibility are ready."
        : "TTW profile registered; portable runtime support is still pending.",
      reason,
      path: manifestPath,
      pluginStackId: profile.pluginStackId,
      saveCompatibilityId: profile.saveCompatibilityId,
      savePath: path.join(app.getPath("userData"), "profiles", "ttw", profile.pluginStackId, "courier-v1.json")
    };
  } catch (error) {
    return unavailable(error instanceof Error ? error.message : "The TTW profile could not be read.");
  }
}

function readJamProfile(manifestOverride = null) {
  const manifestPath = path.resolve(manifestOverride || configuredModProfilePath("jam"));
  const unavailable = (message, manifestDetected = existsSync(manifestPath)) => ({
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected,
    message,
    path: manifestPath
  });
  try {
    const profile = JSON.parse(readFileSync(manifestPath, "utf8"));
    if (profile?.schema !== "opennv-jam-profile/v1" ||
        profile?.status !== "validated-local-dependency-profile" ||
        profile?.kind !== "jam" || typeof profile?.profileId !== "string" ||
        !Array.isArray(profile?.files?.gameRoot) ||
        !Array.isArray(profile?.files?.effectiveData) ||
        profile.runtimeCompatibility?.nativeDllLoading !== false) {
      return unavailable("The selected JAM manifest is not a safe validated local profile.", true);
    }
    const rows = [...profile.files.gameRoot, ...profile.files.effectiveData];
    if (rows.length === 0) return unavailable("The selected JAM manifest contains no dependencies.", true);
    for (const row of rows) {
      if (typeof row?.source !== "string") {
        return unavailable("The selected JAM manifest has an invalid dependency source.", true);
      }
      validateHashBoundFile(path.resolve(row.source), row, `JAM dependency ${row.logicalPath || row.source}`);
    }
    const runtimeReady = profile?.runtimeCompatibility?.ready === true;
    const reason = String(profile?.runtimeCompatibility?.reason || "JAM runtime compatibility is not ready.");
    return {
      ready: runtimeReady,
      runtimeReady,
      validated: true,
      manifestDetected: true,
      message: runtimeReady
        ? "JAM profile and portable runtime compatibility are ready."
        : "JAM profile registered; portable xNVSE/JAM support is still pending.",
      reason,
      path: manifestPath,
      profileId: profile.profileId,
      saveCompatibilityId: profile.saveCompatibilityId
    };
  } catch (error) {
    return unavailable(error instanceof Error ? error.message : "The JAM profile could not be read.");
  }
}

function readFo1Profile() {
  const unavailable = (message) => ({ ready: false, message });
  try {
    const configured = JSON.parse(readFileSync(fo1ProfileConfigPath(), "utf8"));
    if (configured?.schema !== "opennv-launcher-fo1-profile/v1") {
      return unavailable("The registered Fallout 1 launcher profile has an unsupported schema.");
    }
    for (const filePath of [configured.hexScene, configured.characterStart]) {
      if (!filePath || !existsSync(filePath)) {
        return unavailable("The registered Fallout 1 generated cache is missing. Register it again.");
      }
    }
    const hexScene = JSON.parse(readFileSync(configured.hexScene, "utf8"));
    if (hexScene?.schema !== "opennv-fo1-hex-scene/v1" || hexScene?.status !== "interactive-hex-topology-proof") {
      return unavailable("The selected Fallout 1 hex scene is not the playable V13ENT contract.");
    }
    const characterStart = JSON.parse(readFileSync(configured.characterStart, "utf8"));
    if (characterStart?.schema !== "opennv-fo1-character-start/v1" ||
        characterStart?.status !== "prepared-owned-data" ||
        characterStart?.retailOrDerivedAssetsPackaged !== false) {
      return unavailable("The selected Fallout 1 character-start cache is not a valid local owned-data contract.");
    }
    const characterStartSha256 = sha256(configured.characterStart);
    if (characterStartSha256 !== configured.characterStartSha256) {
      return unavailable("The registered Fallout 1 character-start contract changed. Register it again.");
    }
    return {
      ready: true,
      message: "Generated Fallout 1 V13ENT and character-opening caches registered.",
      hexScene: path.resolve(configured.hexScene),
      characterStart: path.resolve(configured.characterStart),
      characterStartSha256,
      savePath: path.join(app.getPath("userData"), "profiles", "fallout1", "vault-dweller-v1.json")
    };
  } catch {
    return unavailable("Register the generated Fallout 1 hex-scene.json and character-start.json to enable this route.");
  }
}

function readFo2Profile(manifestOverride = null) {
  const manifestPath = path.resolve(manifestOverride || configuredFo2ProfilePath());
  const unavailable = (message, manifestDetected = existsSync(manifestPath)) => ({
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected,
    message,
    path: manifestPath
  });
  try {
    const profile = JSON.parse(readFileSync(manifestPath, "utf8"));
    if (profile?.schema !== "opennv-fo2-owned-profile/v1" ||
        profile?.status !== "registered-owned-install" ||
        profile?.campaign !== "Fallout2" || !isSha256(profile?.sourceProfileId) ||
        profile?.saveCompatibilityId !== `fallout2:${profile.sourceProfileId}` ||
        profile?.runtimeCompatibility?.ready !== false ||
        profile?.retailOrDerivedAssetsPackaged !== false ||
        !Array.isArray(profile?.generatedCaches) || profile.generatedCaches.length !== 0 ||
        !Array.isArray(profile?.install?.archives) || profile.install.archives.length !== 3) {
      return unavailable("The selected Fallout 2 manifest is not a safe owned-install profile.", true);
    }
    const root = path.resolve(profile.install.root);
    const expected = new Set(["master.dat", "critter.dat", "patch000.dat"]);
    for (const archive of profile.install.archives) {
      if (typeof archive?.source !== "string" || !expected.delete(String(archive.file).toLowerCase()) ||
          path.dirname(path.resolve(archive.source)) !== root ||
          path.basename(archive.source).toLowerCase() !== String(archive.file).toLowerCase() ||
          archive?.formatIdentity?.format !== "fallout-dat2" ||
          archive.formatIdentity.footerBytes !== DAT2_FOOTER_BYTES ||
          !Number.isSafeInteger(archive.formatIdentity.entries) || archive.formatIdentity.entries < 1 ||
          !isSha256(archive.formatIdentity.directorySha256) ||
          !isSha256(archive.formatIdentity.indexSha256)) {
        return unavailable("The selected Fallout 2 manifest has an invalid DAT2 source identity.", true);
      }
      validateHashBoundFile(path.resolve(archive.source), archive, `Fallout 2 archive ${archive.file}`);
    }
    if (expected.size !== 0) {
      return unavailable("The selected Fallout 2 manifest is missing a required DAT2 archive.", true);
    }
    const presentations = profile.runtimeCompatibility.presentations;
    if (!presentations ||
        !["hex-tactical", "first-person", "openxr"].every((id) => presentations[id]?.ready === false)) {
      return unavailable("The Fallout 2 profile overstates runtime presentation readiness.", true);
    }
    return {
      ready: false,
      runtimeReady: false,
      validated: true,
      manifestDetected: true,
      message: "Fallout 2 owned DAT2 install registered; Hex, FPS, and VR runtime work is pending.",
      reason: String(profile.runtimeCompatibility.firstSliceBlocker || "Fallout 2 runtime slice pending."),
      path: manifestPath,
      sourceProfileId: profile.sourceProfileId,
      saveCompatibilityId: profile.saveCompatibilityId
    };
  } catch (error) {
    return unavailable(error instanceof Error ? error.message : "Set up the local Fallout 2 profile first.");
  }
}

function readFo3Profile() {
  const unavailable = (message) => ({ ready: false, message });
  try {
    const profilePath = path.resolve(fo3ProfileConfigPath());
    const profile = JSON.parse(readFileSync(profilePath, "utf8"));
    if (profile?.schema !== "opennv-owned-game-profile/v1" ||
        profile?.status !== "registered-owned-profile" ||
        profile?.campaign !== "Fallout3" ||
        profile?.capabilities?.runtimeBootReady !== true) {
      return unavailable("Set up the local Fallout 3 GOTY profile first.");
    }
    const master = profile?.install?.master?.source;
    if (!master || !existsSync(master)) {
      return unavailable("The registered Fallout 3 installation is missing or moved.");
    }
    return {
      ready: true,
      message: "Fallout 3 CG00 birth profile registered.",
      path: profilePath,
      savePath: path.join(app.getPath("userData"), "profiles", "fallout3", "cg00-character-v1.json")
    };
  } catch {
    return unavailable("Set up the local Fallout 3 GOTY profile first.");
  }
}

function readNewVegasProfile() {
  const unavailable = (message) => ({ ready: false, message });
  try {
    const defaultCellRecipe = productConfiguration()?.legalAssets?.defaultCellRecipe;
    if (typeof defaultCellRecipe !== "string" || path.basename(defaultCellRecipe) !== defaultCellRecipe) {
      return unavailable("The OpenNV default New Vegas cell recipe is invalid.");
    }
    const cacheRoot = path.join(
      app.getPath("appData"),
      "Godot", "app_userdata", "OpenNV", "cache", "legal-assets-v1");
    const required = [
      ["install-manifest.json", "opennv-legal-asset-cache/v1"],
      [path.join("generated", "cells", defaultCellRecipe, "cell-scene.json"), "opennv-cell-scene/v11"],
      [path.join("generated", "actors", "actor-scenes.json"), "opennv-world-actor-scenes/v2"],
      [path.join("generated", "opening", "opening-manifest.json"), "opennv-owned-opening-manifest/v1"]
    ];
    for (const [relativePath, schema] of required) {
      const document = JSON.parse(readFileSync(path.join(cacheRoot, relativePath), "utf8"));
      if (document?.schema !== schema) return unavailable("Rebuild the local New Vegas owned-data cache.");
    }
    return {
      ready: true,
      message: "New Vegas owned menu, opening, actor, and Doc Mitchell cell cache registered.",
      savePath: path.join(app.getPath("userData"), "profiles", "newvegas", "courier-v1.json")
    };
  } catch {
    return unavailable("Import the legally owned New Vegas data once to enable this route.");
  }
}

function configuredRuntimeRoot() {
  try {
    const configured = JSON.parse(readFileSync(runtimeConfigPath(), "utf8"));
    if (configured?.runtimeRoot && existsSync(configured.runtimeRoot)) return configured.runtimeRoot;
  } catch {
    // The launcher remains usable before a user chooses a runtime.
  }
  return null;
}

function runtimeRoot() {
  const explicit = process.env.OPENNV_RUNTIME_ROOT || process.env.OPENNV_HOME;
  if (explicit && existsSync(explicit)) return explicit;
  const configured = configuredRuntimeRoot();
  if (configured) return configured;
  return null;
}

function runtimeManifest() {
  const root = runtimeRoot();
  if (!root) return null;
  const manifestPath = path.join(root, "runtime-manifest.json");
  if (!existsSync(manifestPath)) return null;
  try {
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    return manifest?.schema === "opennv-runtime-manifest/v1" ? { root, manifest } : null;
  } catch {
    return null;
  }
}

async function readRuntimeState() {
  return runtimeManifest()?.manifest ?? null;
}

async function launcherState() {
  const base = createOfflineState();
  const fallout1Profile = readFo1Profile();
  const fallout2Profile = readFo2Profile();
  const fallout3Profile = readFo3Profile();
  const newVegasProfile = readNewVegasProfile();
  const ttwProfile = readTtwProfile();
  const jamProfile = readJamProfile();
  const merged = mergeRuntimeState(
    base,
    await readRuntimeState(),
    { fallout1Profile, fallout2Profile, fallout3Profile, newVegasProfile, ttwProfile, jamProfile });
  const profileStatus = (profile) => !profile.manifestDetected
    ? "not-installed"
    : !profile.validated
      ? "profile-changed"
      : profile.runtimeReady
        ? "ready"
        : "registered-runtime-pending";
  return {
    ...merged,
    mods: merged.mods.map((mod) => {
      const profile = mod.id === "ttw" ? ttwProfile : mod.id === "jam" ? jamProfile : null;
      return profile
        ? { ...mod, status: profileStatus(profile), detail: profile.message }
        : mod;
    }),
    profiles: {
      fallout1: fallout1Profile,
      fallout2: fallout2Profile,
      fallout3: fallout3Profile,
      newVegas: newVegasProfile,
      ttw: ttwProfile,
      jam: jamProfile
    },
    desktopLauncher: desktopLauncherPolicy()
  };
}

function isRuntimeRoot(candidate) {
  const manifestPath = path.join(candidate, "runtime-manifest.json");
  if (!existsSync(manifestPath)) return false;
  try {
    return JSON.parse(readFileSync(manifestPath, "utf8"))?.schema === "opennv-runtime-manifest/v1";
  } catch {
    return false;
  }
}

async function chooseRuntime() {
  const selection = await dialog.showOpenDialog({
    title: "Choose an extracted OpenNV runtime folder",
    properties: ["openDirectory", "createDirectory"]
  });
  if (selection.canceled || selection.filePaths.length === 0) return { ok: false, message: "No runtime folder selected." };
  const candidate = selection.filePaths[0];
  if (!isRuntimeRoot(candidate)) {
    return { ok: false, message: "That folder has no valid OpenNV Godot runtime-manifest.json." };
  }
  mkdirSync(path.dirname(runtimeConfigPath()), { recursive: true });
  writeFileSync(
    runtimeConfigPath(),
    `${JSON.stringify({ runtimeRoot: candidate }, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
    "utf8"
  );
  return { ok: true, message: "OpenNV runtime bridge connected." };
}

async function chooseFo1Profile() {
  const selectContract = async (title) => dialog.showOpenDialog({
    title,
    properties: ["openFile"],
    filters: [{ name: "OpenNV JSON contract", extensions: ["json"] }]
  });
  const hexSelection = await selectContract("Choose the generated Fallout 1 hex-scene.json");
  if (hexSelection.canceled || hexSelection.filePaths.length !== 1) {
    return { ok: false, message: "Fallout 1 profile registration canceled." };
  }
  const characterSelection = await selectContract("Choose the generated Fallout 1 character-start.json");
  if (characterSelection.canceled || characterSelection.filePaths.length !== 1) {
    return { ok: false, message: "Fallout 1 profile registration canceled." };
  }
  const profile = {
    schema: "opennv-launcher-fo1-profile/v1",
    hexScene: path.resolve(hexSelection.filePaths[0]),
    characterStart: path.resolve(characterSelection.filePaths[0]),
    characterStartSha256: sha256(characterSelection.filePaths[0])
  };
  mkdirSync(path.dirname(fo1ProfileConfigPath()), { recursive: true });
  writeFileSync(fo1ProfileConfigPath(), `${JSON.stringify(profile, null, RUNTIME_CONFIG_JSON_INDENT)}\n`, "utf8");
  const validated = readFo1Profile();
  return validated.ready
    ? { ok: true, message: validated.message }
    : { ok: false, message: validated.message };
}

async function chooseFo2Profile() {
  const selection = await dialog.showOpenDialog({
    title: "Choose the locally generated Fallout 2 owned-install profile",
    properties: ["openFile"],
    filters: [{ name: "OpenNV Fallout 2 profile", extensions: ["json"] }]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: "Fallout 2 profile registration canceled." };
  }
  const manifest = path.resolve(selection.filePaths[0]);
  const profile = readFo2Profile(manifest);
  if (!profile.validated) return { ok: false, message: profile.message };
  const registration = {
    schema: "opennv-launcher-owned-profile-registration/v1",
    campaign: "Fallout2",
    manifest
  };
  mkdirSync(path.dirname(fo2ProfileRegistrationPath()), { recursive: true });
  writeFileSync(
    fo2ProfileRegistrationPath(),
    `${JSON.stringify(registration, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
    "utf8"
  );
  return { ok: true, message: profile.message };
}

async function chooseModProfile(kind) {
  const title = kind === "ttw"
    ? "Choose the locally generated TTW profile manifest"
    : "Choose the locally generated JAM profile manifest";
  const selection = await dialog.showOpenDialog({
    title,
    properties: ["openFile"],
    filters: [{ name: "OpenNV profile manifest", extensions: ["json"] }]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: `${kind.toUpperCase()} profile registration canceled.` };
  }
  const manifest = path.resolve(selection.filePaths[0]);
  const profile = kind === "ttw" ? readTtwProfile(manifest) : readJamProfile(manifest);
  if (!profile.validated) return { ok: false, message: profile.message };
  const registration = {
    schema: "opennv-launcher-mod-profile-registration/v1",
    kind,
    manifest
  };
  mkdirSync(path.dirname(modProfileRegistrationPath(kind)), { recursive: true });
  writeFileSync(
    modProfileRegistrationPath(kind),
    `${JSON.stringify(registration, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
    "utf8"
  );
  return { ok: true, message: profile.message };
}

function runtimeCommand(installed) {
  const relativeExecutable = installed.manifest.runtime?.executables?.[process.platform];
  const packagedExecutable = relativeExecutable ? path.join(installed.root, relativeExecutable) : null;
  if (packagedExecutable && existsSync(packagedExecutable)) {
    return { executable: packagedExecutable, prefixArguments: [] };
  }
  const developmentGodot = process.env.OPENNV_GODOT;
  if (developmentGodot && existsSync(developmentGodot) &&
      existsSync(path.join(installed.root, "project.godot"))) {
    return { executable: developmentGodot, prefixArguments: ["--path", installed.root] };
  }
  return null;
}

function launch(request) {
  const validatedRequest = validateLaunchRequest(request);
  const { campaign, enableJam, enableVr } = validatedRequest;
  const installed = runtimeManifest();
  if (!installed) {
    return { ok: false, code: "runtime-not-found", message: "Choose an installed OpenNV runtime before launching a world." };
  }
  if (!installed.manifest.runtime?.canLaunch) {
    return { ok: false, code: "runtime-slice-not-playable", message: installed.manifest.runtime?.label || "This runtime slice is not playable yet." };
  }
  const runtimeCampaign = installed.manifest.campaigns?.find((entry) =>
    String(entry?.id ?? "").toLowerCase() === campaign.engineCampaign.toLowerCase());
  const runtimeVariant = runtimeCampaign?.variants?.[campaign.runtimeVariant];
  if (!runtimeVariant?.ready) {
    return { ok: false, code: "campaign-not-ready", message: runtimeVariant?.message || `${campaign.title} is not ready in this runtime.` };
  }
  if (enableJam && !runtimeCampaign?.variants?.jam?.ready) {
    return { ok: false, code: "jam-not-ready", message: runtimeCampaign?.variants?.jam?.message || "JAM is not ready in this runtime." };
  }
  const openXr = installed.manifest.runtime?.presentationModes?.openxr;
  if (enableVr && !openXr?.launchable) {
    return { ok: false, code: "openxr-not-ready", message: "OpenXR is not launchable in this runtime." };
  }
  const command = runtimeCommand(installed);
  if (!command) {
    return { ok: false, code: "runtime-executable-missing", message: `The runtime has no ${process.platform} executable. Development launches can set OPENNV_GODOT.` };
  }

  const fallout1Profile = readFo1Profile();
  const fallout2Profile = readFo2Profile();
  const fallout3Profile = readFo3Profile();
  const newVegasProfile = readNewVegasProfile();
  const ttwProfile = readTtwProfile();
  const jamProfile = readJamProfile();
  if (campaign.id === "fallout1" && !fallout1Profile.ready) {
    return { ok: false, code: "fallout1-profile-not-ready", message: fallout1Profile.message };
  }
  if (campaign.id === "fallout1") {
    mkdirSync(path.dirname(fallout1Profile.savePath), { recursive: true });
  }
  if (campaign.id === "fallout2") {
    return {
      ok: false,
      code: "fallout2-runtime-not-ready",
      message: fallout2Profile.validated
        ? fallout2Profile.reason
        : fallout2Profile.message
    };
  }
  if (campaign.id === "fallout3" && !fallout3Profile.ready) {
    return { ok: false, code: "fallout3-profile-not-ready", message: fallout3Profile.message };
  }
  if (campaign.id === "fallout3") {
    mkdirSync(path.dirname(fallout3Profile.savePath), { recursive: true });
  }
  if (campaign.id === "newvegas") {
    if (!newVegasProfile.ready) {
      return { ok: false, code: "newvegas-profile-not-ready", message: newVegasProfile.message };
    }
    mkdirSync(path.dirname(newVegasProfile.savePath), { recursive: true });
  }
  if (campaign.id === "ttw") {
    if (!ttwProfile.ready) {
      return { ok: false, code: "ttw-profile-not-ready", message: ttwProfile.message };
    }
    mkdirSync(path.dirname(ttwProfile.savePath), { recursive: true });
  }
  if (enableJam && !jamProfile.ready) {
    return { ok: false, code: "jam-profile-not-ready", message: jamProfile.message };
  }
  const runtimeArguments = createRuntimeArguments(
    validatedRequest,
    { fallout1Profile, fallout2Profile, fallout3Profile, newVegasProfile, ttwProfile, jamProfile });
  const args = [...command.prefixArguments, ...runtimeArguments];
  const child = spawn(command.executable, args, { detached: true, stdio: "ignore", windowsHide: true });
  child.unref();
  return { ok: true, message: `${campaign.title} ${enableVr ? "OpenXR" : "flat"} launch handed to the local OpenNV runtime.` };
}

function createWindow() {
  const policy = desktopLauncherPolicy();
  const window = new BrowserWindow({
    width: policy.mainWindowWidthPixels,
    height: policy.mainWindowHeightPixels,
    minWidth: policy.mainWindowMinimumWidthPixels,
    minHeight: policy.mainWindowMinimumHeightPixels,
    backgroundColor: "#050b12",
    title: "Open Nevada",
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: path.join(here, "preload.cjs")
    }
  });
  window.removeMenu();
  window.webContents.on("console-message", (event, legacyLevel, legacyMessage) => {
    const details = event?.message ? event : { level: legacyLevel, message: legacyMessage };
    if (details?.message) console.error(`OPENNV_LAUNCHER_RENDERER_CONSOLE ${details.level} ${details.message}`);
  });
  window.loadFile(renderer);
}

app.whenReady().then(() => {
  ipcMain.handle("opennv:get-state", launcherState);
  ipcMain.handle("opennv:choose-runtime", chooseRuntime);
  ipcMain.handle("opennv:choose-fo1-profile", chooseFo1Profile);
  ipcMain.handle("opennv:choose-fo2-profile", chooseFo2Profile);
  ipcMain.handle("opennv:choose-ttw-profile", () => chooseModProfile("ttw"));
  ipcMain.handle("opennv:choose-jam-profile", () => chooseModProfile("jam"));
  ipcMain.handle("opennv:launch", (_, request) => launch(request));
  ipcMain.handle("opennv:open-external", async (_, value) => {
    const url = new URL(value);
    if (url.protocol !== "https:") throw new Error("Open Nevada only opens secure external links.");
    await shell.openExternal(url.toString());
  });
  createWindow();
  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
