import { app, BrowserWindow, dialog, ipcMain, shell } from "electron";
import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, readdirSync, readSync, renameSync, statSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createOfflineState, createRuntimeArguments, mergeRuntimeState, validateLaunchRequest } from "./contract.mjs";
import { installLocalZip, removeLocalInstall } from "./local-mod-installer.mjs";
import { synchronizeManagedLayers, updateManagedLayer, validateManagedLayers } from "./gate-vortex-layers.mjs";
import { createLaunchInvocation } from "./native-launch-contract.mjs";
import {
  appendSourceRoot,
  importMo2Profile,
  importTtwInstallerProfile,
  inspectOwnedNewVegasDataRoot,
  rebuildManagedSourceLayers,
  validateInstalledModStack
} from "./mod-stack-contract.mjs";
import { createFo1OwnedProfile, validateFo1OwnedProfile } from "./fo1-owned-profile.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const renderer = path.join(here, "renderer", "index.html");
const DAT2_FOOTER_BYTES = 8;
const HASH_READ_CHUNK_BYTES = 1024 * 1024;
const RUNTIME_CONFIG_JSON_INDENT = 2;
const SHA256_HEX_CHARACTERS = 64;
const FALLOUT_PLUGIN_MASTER_EXTENSION = ".esm";
const FALLOUT_NV_MASTER = `FalloutNV${FALLOUT_PLUGIN_MASTER_EXTENSION}`;
const TTW_PLUGIN_STACK_ID_PREFIX = "opennv-ttw-plugin-stack-v1\0";
const REQUIRED_TTW_PLUGINS = [
  "falloutnv.esm",
  "fallout3.esm",
  "taleoftwowastelands.esm",
  "yupttw.esm"
];

function productConfigurationPath() {
  return app.isPackaged
    ? path.join(process.resourcesPath, "config", "open-nv-runtime-v1.json")
    : path.join(here, "..", "..", "runtime", "config", "open-nv-runtime-v1.json");
}

function jamTrustedRequirementsPath() {
  return app.isPackaged
    ? path.join(process.resourcesPath, "config", "jam-trusted-requirements-v1.json")
    : path.join(here, "..", "..", "runtime", "config", "jam-trusted-requirements-v1.json");
}

function jamTrustedRequirements() {
  const trusted = JSON.parse(readFileSync(jamTrustedRequirementsPath(), "utf8"));
  if (trusted?.schema !== "opennv-jam-trusted-requirements/v1" ||
      typeof trusted?.requirementsId !== "string" ||
      !isSha256(trusted?.requirementsSha256) ||
      !Array.isArray(trusted?.supportedPluginContracts) ||
      trusted.supportedPluginContracts.length === 0) {
    throw new Error("The shipped JAM requirements identity is invalid.");
  }
  const pluginHashes = new Set();
  for (const contract of trusted.supportedPluginContracts) {
    if (!isSha256(contract?.jamPluginSha256) ||
        !isSha256(contract?.portableCapabilitiesSha256) ||
        pluginHashes.has(contract.jamPluginSha256)) {
      throw new Error("The shipped JAM plugin contract is invalid.");
    }
    pluginHashes.add(contract.jamPluginSha256);
  }
  return trusted;
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
  return path.join(app.getPath("userData"), "profiles", "fallout1", "fallout1-profile.json");
}

function fo2ProfileRegistrationPath() {
  return path.join(app.getPath("userData"), "fallout2-profile-registration.json");
}

function newVegasDataRegistrationPath() {
  return path.join(app.getPath("userData"), "newvegas-data-registration.json");
}

function fallout3DataRegistrationPath() {
  return path.join(app.getPath("userData"), "fallout3-data-registration.json");
}

function modStackPath() {
  return path.join(app.getPath("userData"), "profiles", "newvegas", "mod-stack.json");
}

function installedModsRoot(game = "newvegas") {
  return path.join(app.getPath("userData"), "mods", game);
}

function managedLayersPath(game = "newvegas") {
  return path.join(app.getPath("userData"), "profiles", game, "layers.json");
}

function writeJsonAtomic(destination, document) {
  mkdirSync(path.dirname(destination), { recursive: true });
  const pending = `${destination}.next`;
  writeFileSync(pending, `${JSON.stringify(document, null, RUNTIME_CONFIG_JSON_INDENT)}\n`, "utf8");
  renameSync(pending, destination);
}

function managedLayers(stack, game = "newvegas") {
  const previous = existsSync(managedLayersPath(game))
    ? validateManagedLayers(JSON.parse(readFileSync(managedLayersPath(game), "utf8")))
    : null;
  return synchronizeManagedLayers(stack, previous);
}

function persistManagedStack(stack, game = "newvegas") {
  const layers = managedLayers(stack, game);
  writeJsonAtomic(managedLayersPath(game), layers);
  writeJsonAtomic(game === "fallout3" ? fo3NativeStackPath() : modStackPath(), stack);
  return layers;
}

function configuredFnvLoadOrderRoot() {
  if (process.env.OPENNV_FNV_PROFILE_ROOT) {
    return path.resolve(process.env.OPENNV_FNV_PROFILE_ROOT);
  }
  if (!process.env.LOCALAPPDATA) return null;
  const candidate = path.join(process.env.LOCALAPPDATA, "FalloutNV");
  return existsSync(path.join(candidate, "plugins.txt")) ? candidate : null;
}

function defaultFo2ProfilePath() {
  return path.join(fo2LocalDataRoot(), "profiles", "fallout2", "fallout2-profile.json");
}

function fo2LocalDataRoot() {
  return process.env.LOCALAPPDATA
    ? path.join(process.env.LOCALAPPDATA, "OpenNV")
    : app.getPath("userData");
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

function configuredNewVegasDataRoot() {
  if (process.env.OPENNV_NEWVEGAS_DATA_ROOT) {
    return path.resolve(process.env.OPENNV_NEWVEGAS_DATA_ROOT);
  }
  try {
    const registration = JSON.parse(
      readFileSync(newVegasDataRegistrationPath(), "utf8"));
    if (registration?.schema === "opennv-live-install-registration/v1" &&
        registration?.campaign === "NewVegas" &&
        typeof registration?.dataRoot === "string") {
      return path.resolve(registration.dataRoot);
    }
  } catch {
    // No live Data folder has been selected yet.
  }
  return null;
}

function fo3NativeStackPath() {
  return path.join(app.getPath("userData"), "profiles", "fallout3", "mod-stack.json");
}

function configuredFallout3DataRoot() {
  if (process.env.OPENNV_FALLOUT3_DATA_ROOT) {
    return path.resolve(process.env.OPENNV_FALLOUT3_DATA_ROOT);
  }
  try {
    const registration = JSON.parse(
      readFileSync(fallout3DataRegistrationPath(), "utf8"));
    if (registration?.schema === "opennv-live-install-registration/v1" &&
        registration?.campaign === "Fallout3" &&
        typeof registration?.dataRoot === "string") {
      return path.resolve(registration.dataRoot);
    }
  } catch {
    // No standalone Fallout 3 Data folder has been selected yet.
  }
  return null;
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

function configuredTtwFo3OpeningProfilePath(ttwManifestPath) {
  if (process.env.OPENNV_TTW_FO3_OPENING_PROFILE) {
    return path.resolve(process.env.OPENNV_TTW_FO3_OPENING_PROFILE);
  }
  return path.join(path.dirname(ttwManifestPath), "ttw-fo3-opening-profile.json");
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

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).sort().map((key) =>
      `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

function ttwPluginStackId(profile) {
  const identity = {
    schema: "opennv-ttw-profile/v1",
    plugins: profile.plugins.map((row) => ({
      file: row.file,
      bytes: row.bytes,
      sha256: row.sha256,
      masters: row.masters
    }))
  };
  return createHash("sha256")
    .update(TTW_PLUGIN_STACK_ID_PREFIX, "utf8")
    .update(canonicalJson(identity), "utf8")
    .digest("hex");
}

function jamProfileIdentity(profile) {
  const rows = [...profile.files.gameRoot, ...profile.files.effectiveData];
  const identity = {
    present: rows.map((row) => [row.component, row.logicalPath, row.sha256]),
    missing: profile.missingDependencies,
    missingMasters: profile.missingPluginMasters,
    requirementsSha256: profile.requirements.sha256,
    portableCapabilitiesSha256: profile.portableCapabilitiesSha256
  };
  return createHash("sha256")
    .update("opennv-jam-profile/v1\0", "utf8")
    .update(canonicalJson(identity), "utf8")
    .digest("hex")
    .slice(0, 20);
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

function hasCurrentNativeTtwSnapshot(profile, manifestPath) {
  try {
    const stack = validateInstalledModStack(JSON.parse(readFileSync(modStackPath(), "utf8")));
    if (stack?.edition !== "ttw" ||
        stack?.orderSource?.kind !== "ttw-profile" ||
        !stack.orderSource.files.some((row) =>
          path.resolve(row.path) === path.resolve(manifestPath) && row.sha256 === sha256(manifestPath)) ||
        stack.plugins.length !== profile.plugins.length) {
      return false;
    }
    return stack.plugins.every((row, index) => {
      const expected = profile.plugins[index];
      const sourceRoot = stack.roots.find((root) => root.id === row.rootId)?.root;
      return row.file.toLowerCase() === String(expected?.file || "").toLowerCase() &&
        row.bytes === expected?.bytes &&
        path.resolve(sourceRoot || "") === path.resolve(profile.sourceRoots[expected?.sourceRootIndex] || "");
    });
  } catch {
    return false;
  }
}

function readTtwProfile(manifestOverride = null) {
  const manifestPath = path.resolve(manifestOverride || configuredModProfilePath("ttw"));
  const message = "TTW direct runtime support is not implemented.";
  return {
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected: existsSync(manifestPath),
    message,
    openings: {
      "ttw-fo3": { proofValidated: false, proofProfilePath: null, interactiveReady: false, blocker: message },
      "ttw-fnv": { proofValidated: false, proofProfilePath: null, interactiveReady: false, blocker: message }
    },
    path: manifestPath
  };
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
    const knownStatus = new Set([
      "validated-local-dependency-profile",
      "incomplete-local-dependency-profile"
    ]);
    if (profile?.schema !== "opennv-jam-profile/v1" ||
        !knownStatus.has(profile?.status) ||
        profile?.kind !== "jam" || typeof profile?.profileId !== "string" ||
        !Array.isArray(profile?.files?.gameRoot) ||
        !Array.isArray(profile?.files?.effectiveData) ||
        !Array.isArray(profile?.missingDependencies) ||
        !Array.isArray(profile?.missingPluginMasters) ||
        !Array.isArray(profile?.portableCapabilities) ||
        typeof profile?.portableCapabilitiesCanonical !== "string" ||
        !isSha256(profile?.portableCapabilitiesSha256) ||
        !isSha256(profile?.requirements?.sha256) ||
        !isSha256(profile?.jamPlugin?.sha256) ||
        profile.runtimeCompatibility?.nativeDllLoading !== false) {
      return unavailable("The selected JAM manifest is not a safe validated local profile.", true);
    }
    const trustedRequirements = jamTrustedRequirements();
    if (profile.requirements.id !== trustedRequirements.requirementsId ||
        profile.requirements.sha256 !== trustedRequirements.requirementsSha256) {
      return unavailable("The JAM profile was generated from another requirements contract.", true);
    }
    const trustedPluginContract = trustedRequirements.supportedPluginContracts.find(
      (contract) => contract.jamPluginSha256 === profile.jamPlugin.sha256
    );
    if (!trustedPluginContract ||
        trustedPluginContract.portableCapabilitiesSha256 !== profile.portableCapabilitiesSha256) {
      return unavailable("The installed JAM plugin has no shipped portable capability contract.", true);
    }
    if (createHash("sha256").update(profile.portableCapabilitiesCanonical, "utf8").digest("hex") !==
          profile.portableCapabilitiesSha256 ||
        canonicalJson(JSON.parse(profile.portableCapabilitiesCanonical)) !==
          canonicalJson(profile.portableCapabilities) ||
        jamProfileIdentity(profile) !== profile.profileId ||
        profile.saveCompatibilityId !== `fallout-new-vegas+jam:${profile.profileId}`) {
      return unavailable("The selected JAM manifest identity changed; register it again.", true);
    }
    const jamRows = profile.files.effectiveData.filter((row) => row?.component === "jam");
    if (jamRows.length !== 1 || jamRows[0]?.sha256 !== profile.jamPlugin.sha256) {
      return unavailable("The JAM plugin identity is inconsistent.", true);
    }
    const rows = [...profile.files.gameRoot, ...profile.files.effectiveData];
    if (rows.length === 0) return unavailable("The selected JAM manifest contains no dependencies.", true);
    for (const row of rows) {
      if (typeof row?.source !== "string") {
        return unavailable("The selected JAM manifest has an invalid dependency source.", true);
      }
      validateHashBoundFile(path.resolve(row.source), row, `JAM dependency ${row.logicalPath || row.source}`);
    }
    const dependenciesComplete = profile.status === "validated-local-dependency-profile";
    const runtimeReady = dependenciesComplete && profile?.runtimeCompatibility?.ready === true;
    const reason = String(profile?.runtimeCompatibility?.reason || "JAM runtime compatibility is not ready.");
    return {
      ready: runtimeReady,
      runtimeReady,
      validated: true,
      manifestDetected: true,
      message: runtimeReady
        ? "JAM profile and portable runtime compatibility are ready."
        : (dependenciesComplete
          ? "JAM profile registered; portable xNVSE/JAM support is still pending."
          : "JAM profile registered; local dependencies and complete portable semantics are still missing."),
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
  const profilePath = path.resolve(fo1ProfileConfigPath());
  const unavailable = (message) => ({ ready: false, validated: false, message, path: profilePath });
  try {
    const configured = validateFo1OwnedProfile(JSON.parse(readFileSync(profilePath, "utf8")));
    return {
      ready: true,
      validated: true,
      runtimeReady: false,
      message: "Fallout 1 owned DAT1/loose profile registered; native gameplay remains fail-closed.",
      path: profilePath,
      dataRoot: configured.install.root,
      profileId: configured.sourceProfileId,
      saveCompatibilityId: configured.saveCompatibilityId,
      savePath: path.join(app.getPath("userData"), "profiles", "fallout1", "vault-dweller-v1.json")
    };
  } catch (error) {
    return unavailable(error instanceof Error
      ? error.message
      : "Register a legally owned Fallout 1 install to enable the native route.");
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
      const archivePath = path.resolve(archive.source);
      if (!existsSync(archivePath) || statSync(archivePath).size !== archive.bytes) {
        return unavailable(`Fallout 2 archive ${archive.file} is missing or changed.`, true);
      }
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
      ready: true,
      runtimeReady: true,
      validated: true,
      manifestDetected: true,
      message: "Ready: native owned-data Map 3 presentation; gameplay semantics remain fail-closed.",
      path: manifestPath,
      dataRoot: root,
      sourceProfileId: profile.sourceProfileId,
      saveCompatibilityId: profile.saveCompatibilityId,
      savePath: path.join(app.getPath("userData"), "profiles", "fallout2", "chosen-v1.json")
    };
  } catch (error) {
    return unavailable(error instanceof Error ? error.message : "Set up the local Fallout 2 profile first.");
  }
}

function readFo3Profile() {
  const configuredRoot = configuredFallout3DataRoot();
  const unavailable = (message, manifestDetected = Boolean(configuredRoot)) => ({
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected,
    message
  });
  try {
    if (!configuredRoot) {
      return unavailable("Choose the legally owned standalone Fallout 3 GOTY Data folder.", false);
    }
    const dataRoot = path.resolve(configuredRoot);
    if (!existsSync(path.join(dataRoot, "Fallout3.esm"))) {
      return unavailable("The selected folder has no live Fallout3.esm.");
    }
    const names = readdirSync(dataRoot);
    const plugins = names.filter((name) => /\.(?:esm|esp)$/iu.test(name));
    const archives = names.filter((name) => /\.bsa$/iu.test(name));
    if (archives.length === 0) return unavailable("The selected folder has no live BSA files.");
    return {
      ready: true,
      runtimeReady: true,
      validated: true,
      manifestDetected: true,
      message: `${plugins.length} live Fallout 3 plugins and ${archives.length} live archives ready.`,
      dataRoot,
      savePath: path.join(
        app.getPath("userData"),
        "profiles",
        "fallout3",
        "live",
        "campaign-v1.json")
    };
  } catch (error) {
    return unavailable(error instanceof Error
      ? error.message
      : "Register the local Fallout 3 GOTY Data folder again.");
  }
}

function readNewVegasProfile(dataRootOverride = null) {
  const configuredRoot = dataRootOverride || configuredNewVegasDataRoot();
  const unavailable = (message, manifestDetected = Boolean(configuredRoot)) => ({
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected,
    message
  });
  try {
    if (!configuredRoot) {
      return unavailable("Choose the legally owned Fallout: New Vegas Data folder.", false);
    }
    const inspected = inspectOwnedNewVegasDataRoot(configuredRoot);
    return {
      ready: true,
      runtimeReady: true,
      validated: true,
      manifestDetected: true,
      message: `${inspected.plugins.length} plugins and ${inspected.archives.length} archives ` +
        "ready for direct live loading.",
      dataRoot: inspected.root,
      savePath: path.join(
        app.getPath("userData"), "profiles", "newvegas", "live", "courier-v1.json")
    };
  } catch (error) {
    return unavailable(error instanceof Error
      ? error.message
      : "The legally owned New Vegas Data folder could not be validated.");
  }
}

function readModStack() {
  const profilePath = modStackPath();
  const unavailable = (message, manifestDetected = existsSync(profilePath)) => ({
    ready: false,
    runtimeReady: false,
    validated: false,
    manifestDetected,
    message,
    path: profilePath
  });
  try {
    const profile = validateInstalledModStack(JSON.parse(readFileSync(profilePath, "utf8")));
    if (profile.edition !== "fallout-new-vegas" ||
        profile.roots[0]?.id !== "owned-data" ||
        !existsSync(path.join(profile.roots[0].root, FALLOUT_NV_MASTER))) {
      return unavailable(`The native source stack has no owned ${FALLOUT_NV_MASTER} base layer.`, true);
    }
    return {
      ready: true,
      runtimeReady: true,
      validated: true,
      manifestDetected: true,
      message: `${profile.roots.length} read-only native source layers registered.`,
      path: profilePath,
      sha256: sha256(profilePath),
      stackId: profile.stackId,
      dataRoot: path.resolve(profile.roots[0].root),
      roots: profile.roots.length,
      plugins: profile.plugins.length,
      archives: profile.archives.length
    };
  } catch (error) {
    return unavailable(error instanceof Error ? error.message : "The mod stack could not be read.");
  }
}

function readManagedLayerState(game = "newvegas") {
  try {
    const stackPath = game === "fallout3" ? fo3NativeStackPath() : modStackPath();
    const stack = validateInstalledModStack(JSON.parse(readFileSync(stackPath, "utf8")));
    const catalog = managedLayers(stack, game);
    return {
      validated: true,
      catalogId: catalog.catalogId,
      layers: catalog.layers.map((layer) => ({
        id: layer.id,
        provider: layer.provider,
        displayName: layer.displayName,
        enabled: layer.enabled,
        order: layer.order,
        plugins: layer.plugins.length,
        removable: layer.removable
      }))
    };
  } catch (error) {
    return {
      validated: false,
      message: error instanceof Error ? error.message : "Gate Vortex layers could not be read.",
      layers: []
    };
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
  const modStack = readModStack();
  const ttwProfile = readTtwProfile();
  const jamProfile = readJamProfile();
  const newVegasManagedLayers = readManagedLayerState();
  const fallout3ManagedLayers = readManagedLayerState("fallout3");
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
    mods: [{
      id: "source-stack",
      title: "Unified mod source stack",
      status: !modStack.manifestDetected
        ? "not-installed"
        : modStack.validated
          ? "ready"
          : "profile-changed",
      detail: modStack.message
    }, ...merged.mods.map((mod) => {
      const profile = mod.id === "ttw" ? ttwProfile : mod.id === "jam" ? jamProfile : null;
      return profile
        ? { ...mod, status: profileStatus(profile), detail: profile.message }
        : mod;
    })],
    profiles: {
      fallout1: fallout1Profile,
      fallout2: fallout2Profile,
      fallout3: fallout3Profile,
      newVegas: newVegasProfile,
      ttw: ttwProfile,
      jam: jamProfile,
      modStack
    },
    managedLayers: {
      newvegas: newVegasManagedLayers,
      fallout3: fallout3ManagedLayers,
      fallout1: {
        validated: false,
        layers: [],
        message: "Fallout 1 currently admits only install/Data over critter.dat and master.dat; ordered external loose roots are not implemented."
      },
      fallout2: {
        validated: false,
        layers: [],
        message: "Fallout 2 currently admits patch000.dat, critter.dat, and master.dat only; no direct loose-root overlay contract exists."
      }
    },
    desktopLauncher: desktopLauncherPolicy()
  };
}

function requireManagedGame(game) {
  if (game === "fallout1") {
    throw new Error(
      "Fallout 1 mod layers are blocked: the direct runtime currently admits only install/Data over critter.dat and master.dat. External loose roots, DAT replacement, and executable or script-extender mods are not implemented.");
  }
  if (game === "fallout2") {
    throw new Error(
      "Fallout 2 mod layers are blocked: the direct runtime currently admits patch000.dat, critter.dat, and master.dat only. Loose roots, DAT replacement, and executable or script-extender mods are not implemented.");
  }
  if (game !== "newvegas" && game !== "fallout3") {
    throw new Error(`Gate Vortex does not recognize the game profile: ${game}`);
  }
  return game;
}

async function addModSourceRoot(_event, requestedGame = "newvegas") {
  let game;
  try {
    game = requireManagedGame(requestedGame);
  } catch (error) {
    return { ok: false, message: error.message };
  }
  const selection = await dialog.showOpenDialog({
    title: `Add a read-only ${game === "fallout3" ? "Fallout 3" : "New Vegas"} mod folder as the highest-priority layer`,
    properties: ["openDirectory"]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: "Mod source registration canceled." };
  }
  try {
    let current = null;
    const stackPath = game === "fallout3" ? fo3NativeStackPath() : modStackPath();
    if (existsSync(stackPath)) {
      current = validateInstalledModStack(JSON.parse(readFileSync(stackPath, "utf8")));
    }
    const expectedBase = game === "fallout3" ? "owned-fallout3-data" : "owned-data";
    if (!current || current.roots[0]?.id !== expectedBase) {
      throw new Error(`Choose the owned ${game === "fallout3" ? "Fallout 3" : "New Vegas"} Data folder before adding mod layers.`);
    }
    const selected = path.resolve(selection.filePaths[0]);
    if (game === "fallout3" && existsSync(path.join(selected, "modlist.txt")) &&
        existsSync(path.join(selected, "plugins.txt"))) {
      throw new Error(
        "Fallout 3 MO2/Wabbajack profile import is not implemented yet; add its already-deployed mod folders individually."
      );
    }
    if (game === "newvegas" && existsSync(path.join(selected, "modlist.txt")) &&
        existsSync(path.join(selected, "plugins.txt"))) {
      const profile = importMo2Profile(current, selected, { provider: "mo2" });
      persistManagedStack(profile, game);
      return {
        ok: true,
        message: `Imported ${path.basename(selected)} with ${profile.roots.length - 1} enabled mod layers ` +
          `and ${profile.plugins.length} enabled plugins in profile order.`
      };
    }
    const nextIndex = current?.roots.length || 0;
    const idBase = path.basename(selected).toLowerCase()
      .replaceAll(/[^a-z\d]+/gu, "-")
      .replaceAll(/^-|-$/gu, "") || "layer";
    const profile = appendSourceRoot(current, {
      id: `${nextIndex.toString().padStart(3, "0")}-${idBase}`,
      provider: "manual",
      root: selected,
      name: "OpenNV New Vegas Mod Stack"
    });
    persistManagedStack(profile, game);
    return {
      ok: true,
      message: `Added ${path.basename(selected)} as layer ${profile.roots.length - 1}; ` +
        `${profile.plugins.length} effective plugins and ${profile.archives.length} archives indexed.`
    };
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error ? error.message : "The mod source folder could not be registered."
    };
  }
}

async function installLocalModArchive(_event, requestedGame = "newvegas") {
  let game;
  try {
    game = requireManagedGame(requestedGame);
  } catch (error) {
    return { ok: false, message: error.message };
  }
  const selection = await dialog.showOpenDialog({
    title: `Install a local ${game === "fallout3" ? "Fallout 3" : "New Vegas"} mod ZIP into Gate Vortex`,
    properties: ["openFile"],
    filters: [{ name: "ZIP mod archive", extensions: ["zip"] }]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: "Local mod installation canceled." };
  }
  let installed = null;
  try {
    const stackPath = game === "fallout3" ? fo3NativeStackPath() : modStackPath();
    if (!existsSync(stackPath)) {
      throw new Error(`Choose the owned ${game === "fallout3" ? "Fallout 3" : "New Vegas"} Data folder before installing mods.`);
    }
    const current = validateInstalledModStack(JSON.parse(readFileSync(stackPath, "utf8")));
    const expectedBase = game === "fallout3" ? "owned-fallout3-data" : "owned-data";
    if (current.roots[0]?.id !== expectedBase) {
      throw new Error(`The native mod stack has no owned ${game === "fallout3" ? "Fallout 3" : "New Vegas"} base layer.`);
    }
    installed = installLocalZip(selection.filePaths[0], installedModsRoot(game));
    const profile = appendSourceRoot(current, {
      id: `gate-${installed.installId}`,
      provider: "gate-vortex",
      root: installed.contentRoot,
      name: "OpenNV Gate Vortex Mod Stack"
    });
    persistManagedStack(profile, game);
    return {
      ok: true,
      message: `Installed ${installed.displayName} as a private Gate Vortex layer; ` +
        `${profile.plugins.length} effective plugins and ${profile.archives.length} archives are now indexed.`
    };
  } catch (error) {
    if (installed !== null) {
      try {
        removeLocalInstall(installed);
      } catch {
        // Preserve the original fail-closed installation error.
      }
    }
    return {
      ok: false,
      message: error instanceof Error ? error.message : "The local mod ZIP could not be installed."
    };
  }
}

async function manageModLayer(_event, request) {
  try {
    const game = requireManagedGame(request?.game);
    if (typeof request?.layerId !== "string" || typeof request?.action !== "string") {
      throw new Error("Choose one visible Gate Vortex layer action.");
    }
    const stackPath = game === "fallout3" ? fo3NativeStackPath() : modStackPath();
    const current = validateInstalledModStack(JSON.parse(readFileSync(stackPath, "utf8")));
    const catalog = managedLayers(current, game);
    const target = catalog.layers.find((layer) => layer.id === request.layerId);
    if (!target) throw new Error(`Managed mod layer is unknown: ${request.layerId}`);
    const updated = updateManagedLayer(catalog, request.layerId, request.action);
    const profile = rebuildManagedSourceLayers(current, updated.layers);
    writeJsonAtomic(managedLayersPath(game), updated);
    writeJsonAtomic(stackPath, profile);
    if (request.action === "uninstall") {
      removeLocalInstall({ installId: target.installId, metadataPath: target.metadataPath });
    }
    return {
      ok: true,
      message: `${target.displayName}: ${request.action}. Active layers are sealed low-to-high as ` +
        `${profile.stackId}; its saves remain isolated from every other stack identity.`
    };
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error ? error.message : "The Gate Vortex layer action failed closed."
    };
  }
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
  const selection = await dialog.showOpenDialog({
    title: "Choose the legally owned Fallout 1 install folder",
    properties: ["openDirectory"]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: "Fallout 1 profile registration canceled." };
  }
  const profile = createFo1OwnedProfile(selection.filePaths[0]);
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

async function chooseNewVegasData() {
  const selection = await dialog.showOpenDialog({
    title: "Choose your legally owned Fallout: New Vegas Data folder",
    properties: ["openDirectory"]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: "New Vegas Data registration canceled." };
  }
  try {
    const inspected = inspectOwnedNewVegasDataRoot(selection.filePaths[0]);
    const dataRoot = inspected.root;
    const registration = {
      schema: "opennv-live-install-registration/v1",
      campaign: "NewVegas",
      dataRoot
    };
    mkdirSync(path.dirname(newVegasDataRegistrationPath()), { recursive: true });
    writeFileSync(
      newVegasDataRegistrationPath(),
      `${JSON.stringify(registration, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
      "utf8"
    );
    const profile = readNewVegasProfile(dataRoot);
    return profile.ready
      ? { ok: true, message: profile.message }
      : { ok: false, message: profile.message };
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error
        ? error.message
        : "The selected New Vegas Data folder could not be registered."
    };
  }
}

async function chooseFallout3Data() {
  const selection = await dialog.showOpenDialog({
    title: "Choose your legally owned standalone Fallout 3 GOTY Data folder",
    properties: ["openDirectory"]
  });
  if (selection.canceled || selection.filePaths.length !== 1) {
    return { ok: false, message: "Fallout 3 Data registration canceled." };
  }
  try {
    const dataRoot = path.resolve(selection.filePaths[0]);
    if (!existsSync(path.join(dataRoot, "Fallout3.esm"))) {
      throw new Error("The selected folder has no live Fallout3.esm.");
    }
    const registration = {
      schema: "opennv-live-install-registration/v1",
      campaign: "Fallout3",
      dataRoot
    };
    mkdirSync(path.dirname(fallout3DataRegistrationPath()), { recursive: true });
    writeFileSync(
      fallout3DataRegistrationPath(),
      `${JSON.stringify(registration, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
      "utf8");
    const profile = readFo3Profile();
    return profile.ready
      ? { ok: true, message: profile.message }
      : { ok: false, message: profile.message };
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error
        ? error.message
        : "The selected Fallout 3 Data folder could not be registered."
    };
  }
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
  let nativeTtwStack = null;
  try {
    nativeTtwStack = kind === "ttw"
      ? importTtwInstallerProfile(manifest, { verifyPluginHashes: false })
      : null;
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error ? error.message : "The TTW native source stack could not be registered."
    };
  }
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
  if (nativeTtwStack !== null) {
    mkdirSync(path.dirname(modStackPath()), { recursive: true });
    writeFileSync(
      modStackPath(),
      `${JSON.stringify(nativeTtwStack, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
      "utf8"
    );
    const dataRoot = nativeTtwStack.roots[0].root;
    mkdirSync(path.dirname(newVegasDataRegistrationPath()), { recursive: true });
    writeFileSync(
      newVegasDataRegistrationPath(),
      `${JSON.stringify({
        schema: "opennv-launcher-owned-data-registration/v1",
        campaign: "NewVegas",
        dataRoot
      }, null, RUNTIME_CONFIG_JSON_INDENT)}\n`,
      "utf8"
    );
  }
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
  const { campaign, ttwOpening, enableJam, enableVr } = validatedRequest;
  const installed = runtimeManifest();
  if (!installed) {
    return { ok: false, code: "runtime-not-found", message: "Choose an installed OpenNV runtime before launching a world." };
  }
  if (!installed.manifest.runtime?.canLaunch) {
    return { ok: false, code: "runtime-slice-not-playable", message: installed.manifest.runtime?.label || "This runtime slice is not playable yet." };
  }
  const selectedTtwProfile = campaign.id === "ttw" ? readTtwProfile() : null;
  if (campaign.id === "ttw") {
    const opening = selectedTtwProfile?.openings?.[ttwOpening];
    if (!opening?.interactiveReady) {
      return {
        ok: false,
        code: "ttw-opening-not-ready",
        message: opening?.blocker || "The selected TTW opening has no interactive world runtime yet."
      };
    }
  }
  const runtimeCampaign = installed.manifest.campaigns?.find((entry) =>
    String(entry?.id ?? "").toLowerCase() === campaign.engineCampaign.toLowerCase());
  const runtimeVariant = runtimeCampaign?.variants?.[campaign.runtimeVariant];
  if (!runtimeVariant?.ready) {
    return { ok: false, code: "campaign-not-ready", message: runtimeVariant?.message || `${campaign.title} is not ready in this runtime.` };
  }
  if (runtimeVariant.presentations?.[validatedRequest.presentation]?.ready !== true) {
    return {
      ok: false,
      code: "presentation-not-ready",
      message: `${campaign.title} ${validatedRequest.presentation} is not ready in this runtime.`
    };
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
  const modStack = readModStack();
  const ttwProfile = selectedTtwProfile || readTtwProfile();
  const jamProfile = readJamProfile();
  if (campaign.id === "fallout1" && !fallout1Profile.ready) {
    return { ok: false, code: "fallout1-profile-not-ready", message: fallout1Profile.message };
  }
  if (campaign.id === "fallout1") {
    mkdirSync(path.dirname(fallout1Profile.savePath), { recursive: true });
  }
  if (campaign.id === "fallout2") {
    if (!fallout2Profile.ready) {
      return { ok: false, code: "fallout2-profile-not-ready", message: fallout2Profile.reason || fallout2Profile.message };
    }
    mkdirSync(path.dirname(fallout2Profile.savePath), { recursive: true });
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
    {
      fallout1Profile,
      fallout2Profile,
      fallout3Profile,
      newVegasProfile,
      ttwProfile,
      jamProfile,
      modStack
    });
  const invocation = createLaunchInvocation(command, runtimeArguments);
  const child = spawn(invocation.executable, invocation.arguments, {
    detached: true,
    stdio: "ignore",
    windowsHide: true
  });
  child.unref();
  const presentationLabel = {
    "first-person": "FPS",
    "hex-tactical": "Hex",
    openxr: "VR"
  }[validatedRequest.presentation] || validatedRequest.presentation;
  return { ok: true, message: `${campaign.title} ${presentationLabel} launch handed to the local OpenNV runtime.` };
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
  ipcMain.handle("opennv:choose-newvegas-data", chooseNewVegasData);
  ipcMain.handle("opennv:choose-fallout3-data", chooseFallout3Data);
  ipcMain.handle("opennv:choose-ttw-profile", () => chooseModProfile("ttw"));
  ipcMain.handle("opennv:choose-jam-profile", () => chooseModProfile("jam"));
  ipcMain.handle("opennv:add-mod-source-root", addModSourceRoot);
  ipcMain.handle("opennv:install-local-mod-archive", installLocalModArchive);
  ipcMain.handle("opennv:manage-mod-layer", manageModLayer);
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
