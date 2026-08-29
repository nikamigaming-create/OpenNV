import { createHash } from "node:crypto";
import { closeSync, existsSync, openSync, readFileSync, readSync, statSync } from "node:fs";
import path from "node:path";

const HASH_READ_CHUNK_BYTES = 1024 * 1024;
const SHA256_HEX_CHARACTERS = 64;
const CACHE_ID_PREFIX = "opennv-ttw-fo3-opening-cache-v1\0";
const OPENING_SCHEMA = "opennv-ttw-fo3-opening-profile/v1";
const OPENING_STATUS = "transported-bounded-ttw-fo3-opening-command-contract";
const SOURCE_SCHEMA = "opennv-ttw-effective-source-namespace/v1";
const SOURCE_STATUS = "validated-neutral-effective-source-namespace";
const SOURCE_RESOLUTION_POLICY = "top-level-case-insensitive-last-data-root-wins";
const FLATTENED_SOURCE_MODE = "flattened-installer-output-plugin-mtime";
const REQUIRED_UPPER_PLUGINS = new Set([
  "fallout3.esm",
  "taleoftwowastelands.esm",
  "yupttw.esm"
]);

function isSha256(value) {
  return typeof value === "string" &&
    value.length === SHA256_HEX_CHARACTERS && /^[0-9a-f]+$/u.test(value);
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

export function validateTtwProfileSourceLayout(profile) {
  const roots = profile?.sourceRoots;
  const plugins = profile?.plugins;
  if (!Array.isArray(roots) || roots.length === 0 ||
      roots.some((root) => typeof root !== "string" || root.length === 0) ||
      new Set(roots.map((root) => path.resolve(root).toLowerCase())).size !== roots.length ||
      !Array.isArray(plugins) || plugins.length === 0) {
    throw new Error("The selected TTW manifest has an invalid source-root layout.");
  }

  const derivation = profile?.loadOrderSource?.derivation;
  if (derivation !== undefined) {
    const flattenedIndex = derivation?.flattenedSourceRootIndex;
    const evidence = derivation?.plugins;
    if (derivation?.mode !== FLATTENED_SOURCE_MODE ||
        derivation?.allPluginsActive !== true ||
        derivation?.strictlyIncreasingPluginModificationTimes !== true ||
        !Number.isInteger(flattenedIndex) || flattenedIndex !== roots.length - 1 ||
        !Array.isArray(evidence) || evidence.length !== plugins.length) {
      throw new Error("The selected TTW flattened-source derivation is invalid.");
    }

    let previousTimestamp = -1;
    for (const [index, row] of plugins.entries()) {
      const source = evidence[index];
      const timestamp = source?.lastWriteTimeNs;
      if (row?.sourceRootIndex !== flattenedIndex || source?.file !== row?.file ||
          !Number.isFinite(timestamp) || timestamp <= previousTimestamp) {
        throw new Error("The selected TTW flattened-source evidence changed.");
      }
      previousTimestamp = timestamp;
    }
    return { mode: FLATTENED_SOURCE_MODE, sourceRootIndex: flattenedIndex };
  }

  if (roots.length < 2 || plugins.some((row) =>
    REQUIRED_UPPER_PLUGINS.has(String(row?.file).toLowerCase()) && row?.sourceRootIndex === 0)) {
    throw new Error("The selected TTW manifest has no validated upper source layer.");
  }
  return { mode: "layered-data-roots", sourceRootIndex: roots.length - 1 };
}

export function ttwFo3OpeningCacheCompatibilityId(document) {
  const payload = {
    schema: document.schema,
    sourceProfile: document.sourceProfile,
    sourceNamespace: document.sourceNamespace,
    recipe: document.recipe,
    forms: document.forms,
    operands: document.operands,
    stages: document.stages,
    movies: document.movies
  };
  return `ttw-fo3-opening:${createHash("sha256")
    .update(CACHE_ID_PREFIX, "utf8")
    .update(canonicalJson(payload), "utf8")
    .digest("hex")}`;
}

function requireFileHash(filePath, expectedHash, label) {
  if (!existsSync(filePath) || !isSha256(expectedHash) || sha256(filePath) !== expectedHash) {
    throw new Error(`${label} is missing or changed.`);
  }
}

function requireMovie(baseProfile, name, movie) {
  const logicalPath = movie?.logicalPath;
  const winner = movie?.winner;
  if (typeof logicalPath !== "string" || !Number.isInteger(winner?.sourceRootIndex) ||
      !Number.isSafeInteger(winner?.bytes) || winner.bytes < 1 || !isSha256(winner?.sha256)) {
    throw new Error(`TTW Fallout 3 opening movie binding is invalid: ${name}.`);
  }
  const rootValue = baseProfile.sourceRoots[winner.sourceRootIndex];
  if (typeof rootValue !== "string") {
    throw new Error(`TTW Fallout 3 opening movie source root is invalid: ${name}.`);
  }
  const root = path.resolve(rootValue);
  const source = path.resolve(root, logicalPath);
  const relative = path.relative(root, source);
  if (!relative || relative.startsWith("..") || path.isAbsolute(relative) ||
      !existsSync(source) || statSync(source).size !== winner.bytes || sha256(source) !== winner.sha256) {
    throw new Error(`TTW Fallout 3 opening movie source changed: ${name}.`);
  }
}

export function readTtwFo3OpeningContract({
  baseManifestPath,
  baseProfile,
  openingManifestPath
}) {
  const manifestPath = path.resolve(openingManifestPath);
  const unavailable = (message, manifestDetected = existsSync(manifestPath)) => ({
    validated: false,
    runtimeReady: false,
    manifestDetected,
    message,
    path: manifestPath
  });
  try {
    const document = JSON.parse(readFileSync(manifestPath, "utf8"));
    if (document?.schema !== OPENING_SCHEMA || document?.status !== OPENING_STATUS ||
        document?.campaign !== "Fallout3" || document?.edition !== "TTW") {
      return unavailable("The TTW Fallout 3 opening manifest has an unsupported contract.", true);
    }
    const source = document.sourceProfile;
    const expectedBaseHash = sha256(path.resolve(baseManifestPath));
    if (typeof source?.file !== "string" || source.sha256 !== expectedBaseHash ||
        source.pluginStackId !== baseProfile.pluginStackId ||
        source.saveCompatibilityId !== baseProfile.saveCompatibilityId ||
        document.saveCompatibilityId !== baseProfile.saveCompatibilityId) {
      return unavailable("The TTW Fallout 3 opening profile does not bind the registered TTW stack.", true);
    }
    requireFileHash(path.resolve(source.file), source.sha256, "TTW opening source profile");

    const sourceNamespace = document.sourceNamespace;
    if (typeof sourceNamespace?.file !== "string" || sourceNamespace.schema !== SOURCE_SCHEMA ||
        sourceNamespace.status !== SOURCE_STATUS) {
      return unavailable("The TTW Fallout 3 opening profile has no strict effective-source namespace.", true);
    }
    const namespacePath = path.resolve(sourceNamespace.file);
    requireFileHash(namespacePath, sourceNamespace.sha256, "TTW effective-source namespace");
    const namespace = JSON.parse(readFileSync(namespacePath, "utf8"));
    if (namespace?.schema !== SOURCE_SCHEMA || namespace?.status !== SOURCE_STATUS ||
        namespace?.resolutionPolicy !== SOURCE_RESOLUTION_POLICY ||
        namespace?.runtimeCompatibility?.ready !== false ||
        typeof namespace?.sourceProfile?.file !== "string" ||
        path.resolve(namespace.sourceProfile.file) !== path.resolve(source.file) ||
        namespace?.sourceProfile?.sha256 !== source.sha256 ||
        namespace?.sourceProfile?.pluginStackId !== baseProfile.pluginStackId ||
        namespace?.sourceProfile?.saveCompatibilityId !== baseProfile.saveCompatibilityId ||
        canonicalJson(namespace.sourceRoots) !== canonicalJson(baseProfile.sourceRoots) ||
        canonicalJson(namespace.plugins) !== canonicalJson(baseProfile.plugins)) {
      return unavailable("The TTW effective-source namespace no longer matches the registered stack.", true);
    }

    const cache = document.cacheBoundary;
    const expectedCacheId = ttwFo3OpeningCacheCompatibilityId(document);
    if (cache?.kind !== "dedicated-ttw-opening-profile" ||
        cache?.standaloneFallout3ProfileAccepted !== false ||
        cache?.standaloneFallout3CacheReused !== false ||
        cache?.standaloneNewVegasProfileAccepted !== false ||
        cache?.standaloneNewVegasCacheReused !== false ||
        cache?.compatibilityId !== expectedCacheId) {
      return unavailable("The TTW Fallout 3 opening cache boundary changed.", true);
    }
    if (!document.movies || Object.keys(document.movies).length === 0) {
      return unavailable("The TTW Fallout 3 opening movie boundary is absent.", true);
    }
    for (const [name, movie] of Object.entries(document.movies)) {
      requireMovie(baseProfile, name, movie);
    }
    if (document?.runtimeCompatibility?.ready !== false ||
        typeof document?.runtimeCompatibility?.reason !== "string" ||
        !Array.isArray(document?.unsupportedSemantics) ||
        !document.unsupportedSemantics.includes("ttw-save-runtime-and-world-transition") ||
        !document.unsupportedSemantics.includes("xnvse-and-jam-native-plugin-execution")) {
      return unavailable("The TTW Fallout 3 opening runtime boundary is overstated.", true);
    }
    return {
      validated: true,
      runtimeReady: false,
      manifestDetected: true,
      message: "TTW source and bounded Fallout 3 opening state validated; Vault 101 world presentation is still pending.",
      reason: String(document.runtimeCompatibility.reason),
      path: manifestPath,
      sourceNamespacePath: namespacePath,
      pluginStackId: baseProfile.pluginStackId,
      saveCompatibilityId: baseProfile.saveCompatibilityId,
      cacheCompatibilityId: expectedCacheId
    };
  } catch (error) {
    return unavailable(error instanceof Error ? error.message : "The TTW Fallout 3 opening profile could not be read.");
  }
}
