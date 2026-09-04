import { createHash } from "node:crypto";
import { closeSync, existsSync, lstatSync, openSync, readFileSync, readSync, readdirSync, statSync } from "node:fs";
import path from "node:path";

export const MOD_STACK_SCHEMA = "opennv-mod-stack/v2";
export const MOD_STACK_PROVIDERS = Object.freeze([
  "manual",
  "gate-vortex",
  "mo2",
  "wabbajack",
  "vortex",
  "nexus-mods-app",
  "thunderstore",
  "ttw-installer"
]);

const EDITION_TO_GAME = Object.freeze({
  "fallout-new-vegas": "fallout-new-vegas",
  "fallout-3": "fallout-3",
  ttw: "fallout-new-vegas"
});

const makeEditionProfile = ({
  engineBuild,
  contentVersion,
  supportedCampaigns,
  requiredSemanticExtensions = [],
  cleanRoomSemanticCapabilities = []
}) => Object.freeze({
  engineBuild,
  contentVersion,
  supportedCampaigns: Object.freeze([...supportedCampaigns]),
  semanticExtensions: Object.freeze({
    mode: "clean-room",
    required: Object.freeze([...requiredSemanticExtensions]),
    cleanRoomCapabilities: Object.freeze([...cleanRoomSemanticCapabilities])
  })
});

export const MOD_STACK_EDITION_PROFILES = Object.freeze({
  "fallout-new-vegas": makeEditionProfile({
    engineBuild: "1.4.0.525",
    contentVersion: "1.4.0.525",
    supportedCampaigns: ["fallout-new-vegas"]
  }),
  "fallout-3": makeEditionProfile({
    engineBuild: "1.7.0.4",
    contentVersion: "1.7.0.4",
    supportedCampaigns: ["fallout-3"]
  }),
  ttw: makeEditionProfile({
    engineBuild: "1.4.0.525",
    contentVersion: "3.4",
    supportedCampaigns: ["fallout-3", "fallout-new-vegas"],
    requiredSemanticExtensions: ["xnvse", "jip-ln", "showoff"],
    cleanRoomSemanticCapabilities: [
      "xnvse-semantics",
      "jip-ln-semantics",
      "showoff-semantics"
    ]
  })
});

export const MOD_STACK_EDITIONS = Object.freeze(Object.keys(MOD_STACK_EDITION_PROFILES));

const PROVIDERS = new Set(MOD_STACK_PROVIDERS);
const PLUGIN_MASTER_EXTENSION = ".esm";
const PLUGIN_EXTENSIONS = new Set([PLUGIN_MASTER_EXTENSION, ".esp"]);
const ARCHIVE_EXTENSIONS = new Set([".bsa"]);
const FALLOUT_NV_MASTER = `FalloutNV${PLUGIN_MASTER_EXTENSION}`;
const FALLOUT_3_MASTER = `Fallout3${PLUGIN_MASTER_EXTENSION}`;
const SUPPORTED_GAMES = new Set(["fallout-new-vegas", "fallout-3"]);
const SHA256_HEX_CHARACTERS = 64;
const FILE_HASH_READ_CHUNK_BYTES = 1_048_576;
const SHA256_PATTERN = new RegExp(`^[a-f\\d]{${SHA256_HEX_CHARACTERS}}$`, "u");
const REQUIRED_TTW_PLUGINS = new Set([
  FALLOUT_NV_MASTER.toLowerCase(),
  "fallout3.esm",
  "taleoftwowastelands.esm",
  "yupttw.esm"
]);
const TES4_RECORD_SIGNATURE = Buffer.from("TES4", "ascii");
const OFFICIAL_PLUGIN_ORDER = Object.freeze([
  FALLOUT_NV_MASTER,
  "DeadMoney.esm",
  "HonestHearts.esm",
  "OldWorldBlues.esm",
  "LonesomeRoad.esm",
  "GunRunnersArsenal.esm",
  "ClassicPack.esm",
  "MercenaryPack.esm",
  "TribalPack.esm",
  "CaravanPack.esm"
]);
const FALLOUT_3_OFFICIAL_PLUGIN_ORDER = Object.freeze([
  FALLOUT_3_MASTER,
  "Anchorage.esm",
  "ThePitt.esm",
  "BrokenSteel.esm",
  "PointLookout.esm",
  "Zeta.esm"
]);

function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).sort().map((key) =>
      `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

function stackIdentity(document) {
  const identity = {
    schema: MOD_STACK_SCHEMA,
    edition: document.edition,
    engineBuild: document.engineBuild,
    contentVersion: document.contentVersion,
    supportedCampaigns: document.supportedCampaigns,
    semanticExtensions: document.semanticExtensions,
    game: document.game,
    sourceOrder: document.sourceOrder,
    roots: document.roots.map(({ id, provider, root, priority }) => ({
      id,
      provider,
      root: path.resolve(root),
      priority
    })),
    plugins: document.plugins,
    archives: document.archives,
    looseFiles: document.looseFiles,
    orderSource: document.orderSource || null,
    archiveOrderSource: document.archiveOrderSource || null
  };
  return createHash("sha256")
    .update("opennv-mod-stack-v2\0", "utf8")
    .update(canonicalJson(identity), "utf8")
    .digest("hex");
}

export function createModStack({
  name,
  roots,
  plugins = [],
  archives = [],
  orderSource = null,
  archiveOrderSource = null,
  edition = null,
  game = null,
  engineBuild = null,
  contentVersion = null,
  supportedCampaigns = null,
  semanticExtensions = null
}) {
  const selectedEdition = edition ?? game ?? "fallout-new-vegas";
  const editionProfile = MOD_STACK_EDITION_PROFILES[selectedEdition];
  if (!editionProfile) {
    throw new Error(`The OpenNV mod stack has an unsupported edition: ${selectedEdition}`);
  }
  const orderedRoots = roots.map((root, priority) => ({ ...root, priority }));
  const document = {
    schema: MOD_STACK_SCHEMA,
    status: "registered-read-only-source-stack",
    edition: selectedEdition,
    game: EDITION_TO_GAME[selectedEdition],
    engineBuild: engineBuild ?? editionProfile.engineBuild,
    contentVersion: contentVersion ?? editionProfile.contentVersion,
    supportedCampaigns: [...(supportedCampaigns ?? editionProfile.supportedCampaigns)],
    semanticExtensions: cloneSemanticExtensions(
      semanticExtensions ?? editionProfile.semanticExtensions),
    name,
    sourceOrder: "low-to-high-last-wins",
    roots: orderedRoots,
    plugins: [...plugins],
    archives: [...archives],
    looseFiles: inventoryLooseFiles(orderedRoots),
    orderSource,
    archiveOrderSource,
    runtimeCompatibility: {
      ready: false,
      reason: "The source stack is registered; runtime record/resource coverage remains capability-gated."
    }
  };
  document.stackId = stackIdentity(document);
  document.saveCompatibilityId = `${selectedEdition}:${document.stackId}`;
  return validateModStack(document, { requireRoots: false });
}

export function validateModStack(document, { requireRoots = true } = {}) {
  if (document?.schema !== MOD_STACK_SCHEMA ||
      document?.status !== "registered-read-only-source-stack" ||
      !MOD_STACK_EDITIONS.includes(document?.edition) ||
      document?.game !== EDITION_TO_GAME[document?.edition] ||
      !SUPPORTED_GAMES.has(document?.game) ||
      typeof document?.engineBuild !== "string" || !document.engineBuild ||
      typeof document?.contentVersion !== "string" || !document.contentVersion ||
      !Array.isArray(document?.supportedCampaigns) ||
      document?.semanticExtensions === null ||
      typeof document?.semanticExtensions !== "object" ||
      document?.sourceOrder !== "low-to-high-last-wins" ||
      typeof document?.name !== "string" || !document.name.trim() ||
      !Array.isArray(document?.roots) || (requireRoots && document.roots.length === 0) ||
      !Array.isArray(document?.plugins) || !Array.isArray(document?.archives) ||
      !Array.isArray(document?.looseFiles)) {
    throw new Error("The OpenNV mod stack has an invalid root contract.");
  }
  validateEditionMetadata(document);
  const rootIds = new Set();
  for (const [priority, root] of document.roots.entries()) {
    if (typeof root?.id !== "string" || !root.id || rootIds.has(root.id) ||
        !PROVIDERS.has(root?.provider) || root?.priority !== priority ||
        typeof root?.root !== "string" || !path.isAbsolute(root.root)) {
      throw new Error("The OpenNV mod stack contains an invalid source root.");
    }
    rootIds.add(root.id);
  }
  validateOrderedFiles(document.plugins, rootIds, PLUGIN_EXTENSIONS, "plugin");
  validateOrderedFiles(document.archives, rootIds, ARCHIVE_EXTENSIONS, "archive");
  validateLooseFiles(document.looseFiles, rootIds);
  for (const row of document.archives) validateArchiveActivation(row.activation);
  validateOrderSource(document.orderSource);
  validateArchiveOrderSource(document.archiveOrderSource);
  if (document.stackId !== stackIdentity(document)) {
    throw new Error("The OpenNV mod-stack identity changed.");
  }
  return document;
}

function cloneSemanticExtensions(value) {
  return {
    mode: value?.mode ?? "clean-room",
    required: [...(value?.required ?? [])],
    cleanRoomCapabilities: [...(value?.cleanRoomCapabilities ?? [])]
  };
}

function validateEditionMetadata(document) {
  const profile = MOD_STACK_EDITION_PROFILES[document.edition];
  if (document.engineBuild !== profile.engineBuild ||
      document.contentVersion !== profile.contentVersion ||
      document.supportedCampaigns.length !== profile.supportedCampaigns.length ||
      document.supportedCampaigns.some((campaign, index) =>
        campaign !== profile.supportedCampaigns[index])) {
    throw new Error("The OpenNV mod stack edition metadata is not canonical.");
  }
  if (document.saveCompatibilityId !== `${document.edition}:${document.stackId}`) {
    throw new Error("The OpenNV mod stack save namespace is not stack-scoped.");
  }
  if (document.semanticExtensions?.mode !== "clean-room" ||
      !Array.isArray(document.semanticExtensions.required) ||
      !Array.isArray(document.semanticExtensions.cleanRoomCapabilities)) {
    throw new Error("The OpenNV mod stack semantic-extension contract is invalid.");
  }
  const required = new Set(document.semanticExtensions.required);
  const capabilities = new Set(document.semanticExtensions.cleanRoomCapabilities);
  if (required.size !== document.semanticExtensions.required.length ||
      capabilities.size !== document.semanticExtensions.cleanRoomCapabilities.length ||
      [...required].some((value) => typeof value !== "string" || !value) ||
      [...capabilities].some((value) => typeof value !== "string" || !value)) {
    throw new Error("The OpenNV mod stack semantic-extension contract contains duplicates or empty IDs.");
  }
  const expectedRequired = new Set(profile.semanticExtensions.required);
  const expectedCapabilities = new Set(profile.semanticExtensions.cleanRoomCapabilities);
  if (required.size !== expectedRequired.size ||
      [...expectedRequired].some((value) => !required.has(value)) ||
      capabilities.size !== expectedCapabilities.size ||
      [...expectedCapabilities].some((value) => !capabilities.has(value))) {
    throw new Error("The OpenNV mod stack semantic-extension requirements are not canonical.");
  }
}

function preserveEditionMetadata(document) {
  return {
    edition: document.edition,
    engineBuild: document.engineBuild,
    contentVersion: document.contentVersion,
    supportedCampaigns: [...document.supportedCampaigns],
    semanticExtensions: cloneSemanticExtensions(document.semanticExtensions)
  };
}

function validateLooseFiles(rows, rootIds) {
  const namesByRoot = new Map();
  for (const [index, row] of rows.entries()) {
    const logicalPath = String(row?.path || "");
    const segments = logicalPath.split("/");
    if (row?.index !== index || !rootIds.has(row?.rootId) ||
        path.isAbsolute(logicalPath) || logicalPath.includes("\\") ||
        segments.some((segment) => !segment || segment === "." || segment === "..") ||
        !Number.isSafeInteger(row?.bytes) || row.bytes < 0 ||
        !Number.isSafeInteger(row?.mtimeMs) || row.mtimeMs < 0) {
      throw new Error("The OpenNV mod stack contains an invalid loose-file inventory.");
    }
    const folded = logicalPath.toLowerCase();
    const names = namesByRoot.get(row.rootId) || new Set();
    if (names.has(folded)) {
      throw new Error(`The OpenNV mod stack contains a case-colliding loose path: ${logicalPath}`);
    }
    names.add(folded);
    namesByRoot.set(row.rootId, names);
  }
}

function inventoryLooseFiles(roots) {
  const rows = [];
  for (const root of roots) {
    const sourceRoot = path.resolve(root.root);
    if (!existsSync(sourceRoot) || !statSync(sourceRoot).isDirectory()) {
      throw new Error(`Mod source root is missing: ${sourceRoot}`);
    }
    const pending = [{ absolute: sourceRoot, relative: "" }];
    const foldedPaths = new Set();
    const rootRows = [];
    while (pending.length > 0) {
      const current = pending.pop();
      const entries = readdirSync(current.absolute, { withFileTypes: true })
        .sort((left, right) => left.name.localeCompare(right.name, "en", { sensitivity: "base" }));
      for (const entry of entries) {
        const absolute = path.join(current.absolute, entry.name);
        const relative = current.relative ? `${current.relative}/${entry.name}` : entry.name;
        if (entry.isSymbolicLink() || lstatSync(absolute).isSymbolicLink()) {
          throw new Error(`Mod source roots cannot contain symbolic links or junctions: ${absolute}`);
        }
        if (entry.isDirectory()) {
          pending.push({ absolute, relative });
          continue;
        }
        if (!entry.isFile()) {
          throw new Error(`Mod source roots cannot contain special files: ${absolute}`);
        }
        if (!current.relative &&
            (PLUGIN_EXTENSIONS.has(path.extname(entry.name).toLowerCase()) ||
             ARCHIVE_EXTENSIONS.has(path.extname(entry.name).toLowerCase()))) {
          continue;
        }
        const folded = relative.toLowerCase();
        if (foldedPaths.has(folded)) {
          throw new Error(`Mod source root contains case-colliding loose files: ${relative}`);
        }
        foldedPaths.add(folded);
        const metadata = statSync(absolute);
        rootRows.push({
          rootId: root.id,
          path: relative.replaceAll("\\", "/"),
          bytes: metadata.size,
          mtimeMs: Math.trunc(metadata.mtimeMs)
        });
      }
    }
    rootRows.sort((left, right) => left.path.localeCompare(right.path, "en", { sensitivity: "base" }));
    rows.push(...rootRows);
  }
  return rows.map((row, index) => ({ index, ...row }));
}

function validateArchiveActivation(activation) {
  if (activation === undefined) return;
  if (activation?.kind === "fallout-default-ini" && /^sarchivelist\d*$/iu.test(activation?.key)) return;
  if (activation?.kind === "enabled-plugin" && typeof activation?.plugin === "string" &&
      PLUGIN_EXTENSIONS.has(path.extname(activation.plugin).toLowerCase()) &&
      path.basename(activation.plugin) === activation.plugin) return;
  throw new Error("The OpenNV mod stack contains invalid archive activation provenance.");
}

function validateArchiveOrderSource(source) {
  if (source === null || source === undefined) return;
  if (source?.kind !== "fallout-default-ini" || !Array.isArray(source?.files) ||
      source.files.length !== 1 || !Array.isArray(source?.entries) || source.entries.length === 0) {
    throw new Error("The OpenNV mod stack has an invalid archive-order source.");
  }
  validateSourceFiles(source.files, "archive-order");
  const seen = new Set();
  for (const row of source.entries) {
    const file = String(row?.file || "");
    const folded = file.toLowerCase();
    if (!/^sarchivelist\d*$/iu.test(String(row?.key || "")) ||
        path.basename(file) !== file || path.extname(file).toLowerCase() !== ".bsa" ||
        seen.has(folded)) {
      throw new Error("The OpenNV mod stack has invalid archive-list entries.");
    }
    seen.add(folded);
  }
}

function validateOrderSource(source) {
  if (source === null || source === undefined) return;
  if (!["official-default", "fnv-profile", "mo2-profile", "ttw-profile", "explicit-layer-order"].includes(source?.kind) ||
      !Array.isArray(source?.files)) {
    throw new Error("The OpenNV mod stack has an invalid load-order source.");
  }
  validateSourceFiles(source.files, "load-order");
}

function validateSourceFiles(files, label) {
  const seen = new Set();
  for (const row of files) {
    if (typeof row?.path !== "string" || !path.isAbsolute(row.path) || seen.has(row.path.toLowerCase()) ||
        !Number.isSafeInteger(row?.bytes) || row.bytes < 0 ||
        !Number.isSafeInteger(row?.mtimeMs) || row.mtimeMs < 0 ||
        !SHA256_PATTERN.test(String(row?.sha256 || ""))) {
      throw new Error(`The OpenNV mod stack has invalid ${label} provenance.`);
    }
    seen.add(row.path.toLowerCase());
  }
}

function fileIdentity(filePath) {
  const resolved = path.resolve(filePath);
  const metadata = statSync(resolved);
  if (!metadata.isFile()) throw new Error(`Load-order source is not a file: ${resolved}`);
  return {
    path: resolved,
    bytes: metadata.size,
    mtimeMs: Math.trunc(metadata.mtimeMs),
    sha256: createHash("sha256").update(readFileSync(resolved)).digest("hex")
  };
}

function validateOrderedFiles(rows, rootIds, extensions, label) {
  const names = new Set();
  for (const [index, row] of rows.entries()) {
    const name = String(row?.file || "");
    const folded = name.toLowerCase();
    if (row?.index !== index || !rootIds.has(row?.rootId) ||
        path.basename(name) !== name || !extensions.has(path.extname(name).toLowerCase()) ||
        !Number.isSafeInteger(row?.bytes) || row.bytes <= 0 ||
        !Number.isSafeInteger(row?.mtimeMs) || row.mtimeMs < 0 || names.has(folded)) {
      throw new Error(`The OpenNV mod stack contains an invalid ${label} order.`);
    }
    names.add(folded);
  }
}

export function inspectSourceRoot(root) {
  const inspected = inspectTopLevelSourceRoot(root);
  return {
    ...inspected,
    looseFiles: inventoryLooseFiles([{
      id: "inspection", provider: "manual", root: inspected.root, priority: 0
    }])
  };
}

function inspectTopLevelSourceRoot(root) {
  const resolved = path.resolve(root);
  if (!existsSync(resolved) || !statSync(resolved).isDirectory()) {
    throw new Error(`Mod source root is missing: ${resolved}`);
  }
  const files = readdirSync(resolved, { withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => {
      const metadata = statSync(path.join(resolved, entry.name));
      return {
        file: entry.name,
        bytes: metadata.size,
        mtimeMs: Math.trunc(metadata.mtimeMs)
      };
    })
    .sort((left, right) => left.file.localeCompare(right.file, "en", { sensitivity: "base" }));
  return {
    root: resolved,
    plugins: files.filter((row) => PLUGIN_EXTENSIONS.has(path.extname(row.file).toLowerCase())),
    archives: files.filter((row) => ARCHIVE_EXTENSIONS.has(path.extname(row.file).toLowerCase()))
  };
}

export function inspectOwnedNewVegasDataRoot(candidate) {
  const selected = path.resolve(candidate);
  const dataRoot = path.basename(selected).toLowerCase() === "data"
    ? selected
    : path.join(selected, "Data");
  const inspected = inspectSourceRoot(dataRoot);
  const master = inspected.plugins.find((row) =>
    row.file.toLowerCase() === FALLOUT_NV_MASTER.toLowerCase());
  if (!master) {
    throw new Error(`The selected Data folder has no ${FALLOUT_NV_MASTER}.`);
  }
  const masterPath = path.join(inspected.root, master.file);
  const handle = openSync(masterPath, "r");
  const signature = Buffer.alloc(TES4_RECORD_SIGNATURE.length);
  try {
    if (readSync(handle, signature, 0, signature.length, 0) !== signature.length ||
        !signature.equals(TES4_RECORD_SIGNATURE)) {
      throw new Error(`${FALLOUT_NV_MASTER} does not begin with a TES4 record.`);
    }
  } finally {
    closeSync(handle);
  }
  return { ...inspected, gameRoot: path.dirname(inspected.root) };
}

export function createOwnedNewVegasStack(candidate, { configRoot = null } = {}) {
  const inspected = inspectOwnedNewVegasDataRoot(candidate);
  const archiveOrderSource = readArchiveOrderSource(path.join(inspected.gameRoot, "Fallout_default.ini"));
  const roots = [{ id: "owned-data", provider: "manual", root: inspected.root }];
  const plugins = configRoot === null
    ? officialDefaultOrder(inspected.plugins, "owned-data")
    : inspected.plugins.map((row, index) => ({ index, rootId: "owned-data", ...row }));
  const base = createModStack({
    name: "OpenNV New Vegas Native Source Stack",
    edition: "fallout-new-vegas",
    roots,
    plugins,
    archives: deriveActiveArchives(roots, plugins, archiveOrderSource),
    orderSource: { kind: "official-default", files: [] },
    archiveOrderSource
  });
  if (configRoot === null) return base;
  return applyPluginProfileOrder(base, discoverFnvProfileFiles(configRoot));
}

export function createOwnedFallout3Stack(candidate) {
  const inspected = inspectOwnedGameDataRoot(candidate, FALLOUT_3_MASTER);
  const archiveOrderSource = readArchiveOrderSource(path.join(inspected.gameRoot, "Fallout_default.ini"));
  const roots = [{ id: "owned-fallout3-data", provider: "manual", root: inspected.root }];
  const plugins = officialDefaultOrder(
    inspected.plugins,
    "owned-fallout3-data",
    FALLOUT_3_OFFICIAL_PLUGIN_ORDER);
  return createModStack({
    edition: "fallout-3",
    name: "OpenNV Fallout 3 Native Source Stack",
    roots,
    plugins,
    archives: deriveActiveArchives(roots, plugins, archiveOrderSource),
    orderSource: { kind: "official-default", files: [] },
    archiveOrderSource
  });
}

function inspectOwnedGameDataRoot(candidate, masterName) {
  const selected = path.resolve(candidate);
  const dataRoot = path.basename(selected).toLowerCase() === "data"
    ? selected
    : path.join(selected, "Data");
  const inspected = inspectSourceRoot(dataRoot);
  const master = inspected.plugins.find((row) =>
    row.file.toLowerCase() === masterName.toLowerCase());
  if (!master) throw new Error(`The selected Data folder has no ${masterName}.`);
  const masterPath = path.join(inspected.root, master.file);
  const handle = openSync(masterPath, "r");
  const signature = Buffer.alloc(TES4_RECORD_SIGNATURE.length);
  try {
    if (readSync(handle, signature, 0, signature.length, 0) !== signature.length ||
        !signature.equals(TES4_RECORD_SIGNATURE)) {
      throw new Error(`${masterName} does not begin with a TES4 record.`);
    }
  } finally {
    closeSync(handle);
  }
  return { ...inspected, gameRoot: path.dirname(inspected.root) };
}

function readArchiveOrderSource(iniPath) {
  const resolved = path.resolve(iniPath);
  if (!existsSync(resolved) || !statSync(resolved).isFile()) {
    throw new Error(`The owned game root has no Fallout_default.ini: ${resolved}`);
  }
  let section = "";
  const entries = [];
  const seen = new Set();
  for (const sourceLine of readFileSync(resolved, "utf8").replace(/^\uFEFF/u, "").split(/\r?\n/u)) {
    const line = sourceLine.trim();
    if (!line || line.startsWith(";") || line.startsWith("#")) continue;
    const sectionMatch = /^\[([^\]]+)\]$/u.exec(line);
    if (sectionMatch) {
      section = sectionMatch[1].trim().toLowerCase();
      continue;
    }
    if (section !== "archive") continue;
    const separator = line.indexOf("=");
    if (separator < 1) continue;
    const key = line.slice(0, separator).trim();
    if (!/^sarchivelist\d*$/iu.test(key)) continue;
    for (const rawName of line.slice(separator + 1).split(",")) {
      const file = rawName.trim();
      if (!file) continue;
      const folded = file.toLowerCase();
      if (path.basename(file) !== file || path.extname(file).toLowerCase() !== ".bsa") {
        throw new Error(`Invalid BSA entry in Fallout_default.ini: ${file}`);
      }
      if (seen.has(folded)) continue;
      seen.add(folded);
      entries.push({ key, file });
    }
  }
  if (entries.length === 0) {
    throw new Error("Fallout_default.ini has no [Archive] SArchiveList entries.");
  }
  return { kind: "fallout-default-ini", files: [fileIdentity(resolved)], entries };
}

function officialDefaultOrder(plugins, rootId, officialOrder = OFFICIAL_PLUGIN_ORDER) {
  const available = new Map(plugins.map((row) => [row.file.toLowerCase(), row]));
  const ordered = [];
  for (const name of officialOrder) {
    const row = available.get(name.toLowerCase());
    if (row) {
      ordered.push({ index: ordered.length, rootId, ...row });
      available.delete(name.toLowerCase());
    }
  }
  if (available.size !== 0) {
    throw new Error(
      "The Data folder contains non-official plugins but no explicit plugins.txt/loadorder.txt was selected.");
  }
  return ordered;
}

function discoverFnvProfileFiles(configRoot) {
  const resolved = path.resolve(configRoot);
  const pluginsPath = path.join(resolved, "plugins.txt");
  const loadOrderPath = path.join(resolved, "loadorder.txt");
  if (!existsSync(pluginsPath)) {
    throw new Error(`The Fallout New Vegas profile has no plugins.txt: ${pluginsPath}`);
  }
  return {
    kind: "fnv-profile",
    pluginsPath,
    loadOrderPath: existsSync(loadOrderPath) ? loadOrderPath : null
  };
}

function parsePluginFile(filePath) {
  const lines = readFileSync(filePath, "utf8").replace(/^\uFEFF/u, "").split(/\r?\n/u);
  const entries = [];
  let usesEnabledMarkers = false;
  for (const sourceLine of lines) {
    const line = sourceLine.trim();
    if (!line || line.startsWith("#") || line.startsWith(";")) continue;
    const enabled = line.startsWith("*");
    usesEnabledMarkers ||= enabled;
    const name = enabled ? line.slice(1).trim() : line;
    if (!PLUGIN_EXTENSIONS.has(path.extname(name).toLowerCase()) || path.basename(name) !== name) {
      throw new Error(`Invalid plugin entry in ${filePath}: ${sourceLine}`);
    }
    entries.push({ name, enabled });
  }
  const selected = usesEnabledMarkers ? entries.filter((row) => row.enabled) : entries;
  const names = [];
  const seen = new Set();
  for (const row of selected) {
    const folded = row.name.toLowerCase();
    if (seen.has(folded)) throw new Error(`Duplicate plugin entry in ${filePath}: ${row.name}`);
    seen.add(folded);
    names.push(row.name);
  }
  return names;
}

export function applyPluginProfileOrder(document, { kind, pluginsPath, loadOrderPath = null }) {
  validateModStack(document);
  if (!["fnv-profile", "mo2-profile"].includes(kind)) {
    throw new Error("Unsupported plugin profile kind.");
  }
  const enabled = parsePluginFile(path.resolve(pluginsPath));
  const enabledSet = new Set(enabled.map((name) => name.toLowerCase()));
  const order = loadOrderPath === null
    ? enabled
    : parsePluginFile(path.resolve(loadOrderPath)).filter((name) => enabledSet.has(name.toLowerCase()));
  if (!enabledSet.has(FALLOUT_NV_MASTER.toLowerCase())) order.unshift(FALLOUT_NV_MASTER);
  const orderSet = new Set(order.map((name) => name.toLowerCase()));
  for (const name of enabled) {
    if (!orderSet.has(name.toLowerCase())) {
      throw new Error(`Enabled plugin is absent from loadorder.txt: ${name}`);
    }
  }
  const available = effectiveFilesFromRoots(document.roots, "plugins");
  const plugins = order.map((name, index) => {
    const row = available.get(name.toLowerCase());
    if (!row) throw new Error(`Enabled plugin is missing from the registered source roots: ${name}`);
    const { index: ignored, rootPriority: ignoredPriority, ...withoutIndex } = row;
    return { index, ...withoutIndex };
  });
  return createModStack({
    ...preserveEditionMetadata(document),
    name: document.name,
    roots: document.roots.map(({ id, provider, root }) => ({ id, provider, root })),
    plugins,
    archives: deriveActiveArchives(document.roots, plugins, document.archiveOrderSource),
    orderSource: {
      kind,
      files: [pluginsPath, loadOrderPath].filter(Boolean).map(fileIdentity)
    },
    archiveOrderSource: document.archiveOrderSource
  });
}

export function importMo2Profile(document, profileRoot, { modsRoot = null, provider = "mo2" } = {}) {
  validateModStack(document);
  if (!["mo2", "wabbajack"].includes(provider)) {
    throw new Error("MO2 profile imports must use the mo2 or wabbajack provider.");
  }
  if (document.roots.length !== 1 || document.roots[0].id !== "owned-data") {
    throw new Error("Import an MO2 profile into a clean owned-data stack to avoid ambiguous layer precedence.");
  }
  const profile = path.resolve(profileRoot);
  const modListPath = path.join(profile, "modlist.txt");
  const pluginsPath = path.join(profile, "plugins.txt");
  const loadOrderPath = path.join(profile, "loadorder.txt");
  if (!existsSync(modListPath) || !existsSync(pluginsPath)) {
    throw new Error("The selected MO2 profile requires both modlist.txt and plugins.txt.");
  }
  const resolvedModsRoot = path.resolve(modsRoot || path.join(profile, "..", "..", "mods"));
  const enabledHighToLow = parseMo2ModList(modListPath);
  let layered = document;
  for (const [offset, name] of [...enabledHighToLow].reverse().entries()) {
    const modRoot = path.join(resolvedModsRoot, name);
    if (!existsSync(modRoot) || !statSync(modRoot).isDirectory()) {
      throw new Error(`Enabled MO2 mod folder is missing: ${modRoot}`);
    }
    layered = appendSourceRoot(layered, {
      id: `${(offset + 1).toString().padStart(3, "0")}-${slug(name)}`,
      provider,
      root: modRoot
    });
  }
  const ordered = applyPluginProfileOrder(layered, {
    kind: "mo2-profile",
    pluginsPath,
    loadOrderPath: existsSync(loadOrderPath) ? loadOrderPath : null
  });
  return createModStack({
    ...preserveEditionMetadata(document),
    name: `OpenNV ${provider === "wabbajack" ? "Wabbajack" : "MO2"} Profile`,
    roots: ordered.roots.map(({ id, provider: rowProvider, root }) => ({ id, provider: rowProvider, root })),
    plugins: ordered.plugins,
    archives: ordered.archives,
    orderSource: {
      kind: "mo2-profile",
      files: [modListPath, pluginsPath, existsSync(loadOrderPath) ? loadOrderPath : null]
        .filter(Boolean).map(fileIdentity)
    },
    archiveOrderSource: ordered.archiveOrderSource
  });
}

export function importTtwInstallerProfile(profilePath, { verifyPluginHashes = true } = {}) {
  const resolvedProfile = path.resolve(profilePath);
  if (!existsSync(resolvedProfile) || !statSync(resolvedProfile).isFile()) {
    throw new Error(`The registered TTW profile is missing: ${resolvedProfile}`);
  }
  const profile = JSON.parse(readFileSync(resolvedProfile, "utf8"));
  if (profile?.schema !== "opennv-ttw-profile/v1" ||
      profile?.status !== "validated-generated-plugin-profile" || profile?.kind !== "ttw" ||
      !SHA256_PATTERN.test(String(profile?.pluginStackId || "")) ||
      profile?.saveCompatibilityId !== `ttw:${profile.pluginStackId}` ||
      !Array.isArray(profile?.sourceRoots) || profile.sourceRoots.length < 2 ||
      !Array.isArray(profile?.plugins) || profile.plugins.length === 0 ||
      !Array.isArray(profile?.archives)) {
    throw new Error("The selected TTW manifest is not a validated generated profile.");
  }

  const roots = profile.sourceRoots.map((sourceRoot, index) => {
    const resolved = path.resolve(sourceRoot);
    if (!path.isAbsolute(sourceRoot) || !existsSync(resolved) || !statSync(resolved).isDirectory()) {
      throw new Error(`TTW source root is missing or invalid: ${resolved}`);
    }
    return {
      id: index === 0 ? "owned-data" : `ttw-${index.toString().padStart(3, "0")}`,
      provider: index === 0 ? "manual" : "ttw-installer",
      root: resolved
    };
  });
  if (new Set(roots.map((row) => row.root.toLowerCase())).size !== roots.length) {
    throw new Error("The TTW profile repeats a source root.");
  }
  const base = inspectOwnedNewVegasDataRoot(roots[0].root);
  if (base.root.toLowerCase() !== roots[0].root.toLowerCase()) {
    throw new Error("The first TTW source root must be the owned New Vegas Data folder.");
  }

  const names = new Set();
  const plugins = profile.plugins.map((row, index) => {
    const file = String(row?.file || "");
    const folded = file.toLowerCase();
    const sourceRoot = roots[row?.sourceRootIndex];
    if (row?.loadOrderIndex !== index || !sourceRoot || path.basename(file) !== file ||
        !PLUGIN_EXTENSIONS.has(path.extname(file).toLowerCase()) || names.has(folded) ||
        !Number.isSafeInteger(row?.bytes) || row.bytes <= 0 ||
        !SHA256_PATTERN.test(String(row?.sha256 || ""))) {
      throw new Error("The TTW profile contains an invalid active plugin row.");
    }
    names.add(folded);
    const source = path.join(sourceRoot.root, file);
    if (!existsSync(source) || !statSync(source).isFile()) {
      throw new Error(`TTW plugin is missing or moved: ${source}`);
    }
    const metadata = statSync(source);
    if (metadata.size !== row.bytes || (verifyPluginHashes && hashFile(source) !== row.sha256)) {
      throw new Error(`TTW plugin changed; generate and register the TTW profile again: ${source}`);
    }
    return {
      index,
      rootId: sourceRoot.id,
      file,
      bytes: metadata.size,
      mtimeMs: Math.trunc(metadata.mtimeMs)
    };
  });
  if (plugins[0]?.file.toLowerCase() !== FALLOUT_NV_MASTER.toLowerCase() ||
      [...REQUIRED_TTW_PLUGINS].some((name) => !names.has(name))) {
    throw new Error("The TTW profile does not contain the required generated plugin stack.");
  }

  const loadOrderPath = path.resolve(profile?.loadOrderSource?.file || "");
  if (!existsSync(loadOrderPath) || !statSync(loadOrderPath).isFile() ||
      hashFile(loadOrderPath) !== profile?.loadOrderSource?.sha256) {
    throw new Error("The TTW active load-order snapshot is missing or changed.");
  }
  const declaredOrder = parsePluginFile(loadOrderPath).map((name) => name.toLowerCase());
  if (declaredOrder.length !== plugins.length ||
      declaredOrder.some((name, index) => name !== plugins[index].file.toLowerCase())) {
    throw new Error("The TTW active load-order snapshot differs from the registered plugin rows.");
  }

  const archiveOrderSource = readArchiveOrderSource(path.join(path.dirname(roots[0].root), "Fallout_default.ini"));
  const archives = deriveActiveArchives(roots, plugins, archiveOrderSource);
  const inventory = new Map(profile.archives.map((row) => [String(row?.file || "").toLowerCase(), row]));
  for (const archive of archives) {
    const declared = inventory.get(archive.file.toLowerCase());
    const declaredRoot = roots[declared?.sourceRootIndex];
    if (!declared || declaredRoot?.id !== archive.rootId || declared.bytes !== archive.bytes) {
      throw new Error(`The active TTW BSA winner differs from its registered profile: ${archive.file}`);
    }
  }

  return createModStack({
    name: "OpenNV TTW Native Source Stack",
    ...preserveEditionMetadata({
      edition: "ttw",
      engineBuild: MOD_STACK_EDITION_PROFILES.ttw.engineBuild,
      contentVersion: `3.4`,
      supportedCampaigns: MOD_STACK_EDITION_PROFILES.ttw.supportedCampaigns,
      semanticExtensions: MOD_STACK_EDITION_PROFILES.ttw.semanticExtensions
    }),
    roots,
    plugins,
    archives,
    orderSource: {
      kind: "ttw-profile",
      files: [fileIdentity(resolvedProfile), fileIdentity(loadOrderPath)]
    },
    archiveOrderSource
  });
}

function hashFile(filePath) {
  const digest = createHash("sha256");
  const handle = openSync(filePath, "r");
  const buffer = Buffer.allocUnsafe(FILE_HASH_READ_CHUNK_BYTES);
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

function parseMo2ModList(filePath) {
  const names = [];
  const seen = new Set();
  for (const sourceLine of readFileSync(filePath, "utf8").replace(/^\uFEFF/u, "").split(/\r?\n/u)) {
    const line = sourceLine.trim();
    if (!line || line.startsWith("#") || line.startsWith("-") || line.startsWith("*")) continue;
    if (!line.startsWith("+") || !line.slice(1).trim()) {
      throw new Error(`Invalid MO2 modlist entry: ${sourceLine}`);
    }
    const name = line.slice(1).trim();
    if (name.toLowerCase().startsWith("unmanaged:")) continue;
    const folded = name.toLowerCase();
    if (seen.has(folded)) throw new Error(`Duplicate enabled MO2 mod: ${name}`);
    if (name === "." || name === ".." || name.includes("/") || name.includes("\\")) {
      throw new Error(`MO2 mod name escapes the mods root: ${name}`);
    }
    seen.add(folded);
    names.push(name);
  }
  return names;
}

function slug(value) {
  return value.toLowerCase().replaceAll(/[^a-z\d]+/gu, "-").replaceAll(/^-|-$/gu, "") || "mod";
}

export function validateInstalledModStack(document) {
  validateModStack(document);
  const roots = new Map();
  for (const root of document.roots) {
    const resolved = path.resolve(root.root);
    if (!existsSync(resolved) || !statSync(resolved).isDirectory()) {
      throw new Error(`Mod source root is missing or moved: ${resolved}`);
    }
    roots.set(root.id, resolved);
  }
  for (const row of [...document.plugins, ...document.archives]) {
    const candidate = path.join(roots.get(row.rootId), row.file);
    if (!existsSync(candidate) || !statSync(candidate).isFile()) {
      throw new Error(`Declared source file is missing or moved: ${candidate}`);
    }
    const metadata = statSync(candidate);
    if (metadata.size !== row.bytes || Math.trunc(metadata.mtimeMs) !== row.mtimeMs) {
      throw new Error(`Declared source file changed; register its source root again: ${candidate}`);
    }
  }
  const currentLooseFiles = inventoryLooseFiles(document.roots);
  if (currentLooseFiles.length !== document.looseFiles.length ||
      currentLooseFiles.some((row, index) => {
        const declared = document.looseFiles[index];
        return row.index !== declared.index || row.rootId !== declared.rootId ||
          row.path !== declared.path || row.bytes !== declared.bytes || row.mtimeMs !== declared.mtimeMs;
      })) {
    throw new Error("Loose-file inventory changed; register the source stack again.");
  }
  for (const row of document.orderSource?.files || []) {
    if (!existsSync(row.path) || !statSync(row.path).isFile()) {
      throw new Error(`Load-order source is missing or moved: ${row.path}`);
    }
    const current = fileIdentity(row.path);
    if (current.bytes !== row.bytes || current.mtimeMs !== row.mtimeMs || current.sha256 !== row.sha256) {
      throw new Error(`Load-order source changed; import the manager profile again: ${row.path}`);
    }
  }
  for (const row of document.archiveOrderSource?.files || []) {
    if (!existsSync(row.path) || !statSync(row.path).isFile()) {
      throw new Error(`Archive-order source is missing or moved: ${row.path}`);
    }
    const current = fileIdentity(row.path);
    if (current.bytes !== row.bytes || current.mtimeMs !== row.mtimeMs || current.sha256 !== row.sha256) {
      throw new Error(`Archive-order source changed; register the owned game again: ${row.path}`);
    }
  }
  return document;
}

export function appendSourceRoot(document, source) {
  if (document !== null) validateModStack(document);
  if (typeof source?.id !== "string" || !source.id ||
      !PROVIDERS.has(source?.provider) || typeof source?.root !== "string") {
    throw new Error("The new mod source root is invalid.");
  }
  const inspected = inspectSourceRoot(source.root);
  const roots = document ? document.roots.map(({ id, provider, root }) => ({ id, provider, root })) : [];
  if (roots.some((root) => root.id === source.id ||
      path.resolve(root.root).toLowerCase() === inspected.root.toLowerCase())) {
    throw new Error("That mod source root is already registered.");
  }
  roots.push({ id: source.id, provider: source.provider, root: inspected.root });
  const plugins = mergeEffectiveFiles(document?.plugins || [], inspected.plugins, source.id);
  const archiveOrderSource = document?.archiveOrderSource || null;
  const archives = deriveActiveArchives(roots, plugins, archiveOrderSource);
  const metadata = document
    ? preserveEditionMetadata(document)
    : preserveEditionMetadata({
      edition: "fallout-new-vegas",
      engineBuild: MOD_STACK_EDITION_PROFILES["fallout-new-vegas"].engineBuild,
      contentVersion: MOD_STACK_EDITION_PROFILES["fallout-new-vegas"].contentVersion,
      supportedCampaigns: MOD_STACK_EDITION_PROFILES["fallout-new-vegas"].supportedCampaigns,
      semanticExtensions: MOD_STACK_EDITION_PROFILES["fallout-new-vegas"].semanticExtensions
    });
  return createModStack({
    ...metadata,
    name: document?.name || source.name || "OpenNV Mod Stack",
    roots,
    plugins,
    archives,
    orderSource: { kind: "explicit-layer-order", files: [] },
    archiveOrderSource
  });
}

export function rebuildManagedSourceLayers(document, layers) {
  validateModStack(document);
  const expectedBase = document.game === "fallout-3"
    ? { rootId: "owned-fallout3-data", master: FALLOUT_3_MASTER }
    : { rootId: "owned-data", master: FALLOUT_NV_MASTER };
  if (document.roots[0]?.id !== expectedBase.rootId || !Array.isArray(layers)) {
    throw new Error("Managed layers require a sealed standalone Gamebryo owned-data stack.");
  }
  const enabled = layers.filter((layer) => layer?.enabled)
    .sort((left, right) => left.order - right.order);
  const roots = [document.roots[0], ...enabled].map(({ id, provider, root }) => ({ id, provider, root }));
  const available = effectiveFilesFromRoots(roots, "plugins");
  const permitted = new Set(document.plugins.map((row) => row.file.toLowerCase()));
  for (const layer of enabled) {
    for (const file of layer.plugins) permitted.add(file.toLowerCase());
  }
  const orderedNames = document.plugins.map((row) => row.file)
    .filter((file) => available.has(file.toLowerCase()));
  for (const layer of enabled) {
    for (const file of layer.plugins) {
      if (available.has(file.toLowerCase()) &&
          !orderedNames.some((name) => name.toLowerCase() === file.toLowerCase())) {
        orderedNames.push(file);
      }
    }
  }
  const plugins = orderedNames.filter((file) => permitted.has(file.toLowerCase())).map((file, index) => {
    const row = available.get(file.toLowerCase());
    if (!row) throw new Error(`Enabled managed plugin is missing: ${file}`);
    const { rootPriority: ignored, index: ignoredIndex, ...source } = row;
    return { index, ...source };
  });
  if (plugins[0]?.file.toLowerCase() !== expectedBase.master.toLowerCase()) {
    throw new Error(`Managed layers cannot remove or reorder ${expectedBase.master} from the base of the plugin order.`);
  }
  return createModStack({
    ...preserveEditionMetadata(document),
    name: document.name,
    roots,
    plugins,
    archives: deriveActiveArchives(roots, plugins, document.archiveOrderSource),
    orderSource: { kind: "explicit-layer-order", files: [] },
    archiveOrderSource: document.archiveOrderSource
  });
}

function effectiveFilesFromRoots(roots, collection) {
  const effective = new Map();
  for (const [priority, root] of roots.entries()) {
    for (const row of inspectTopLevelSourceRoot(root.root)[collection]) {
      effective.set(row.file.toLowerCase(), { rootId: root.id, rootPriority: priority, ...row });
    }
  }
  return effective;
}

function deriveActiveArchives(roots, plugins, archiveOrderSource) {
  const available = effectiveFilesFromRoots(roots, "archives");
  const active = [];
  const seen = new Set();
  const add = (row, activation) => {
    const folded = row.file.toLowerCase();
    if (seen.has(folded)) return;
    seen.add(folded);
    const { rootPriority: ignored, ...source } = row;
    active.push({ index: active.length, ...source, activation });
  };

  for (const entry of archiveOrderSource?.entries || []) {
    const row = available.get(entry.file.toLowerCase());
    if (!row) {
      throw new Error(`Fallout_default.ini activates a missing BSA: ${entry.file}`);
    }
    add(row, { kind: "fallout-default-ini", key: entry.key });
  }

  for (const plugin of plugins) {
    const pluginStem = path.basename(plugin.file, path.extname(plugin.file)).toLowerCase();
    const matches = [...available.values()]
      .filter((row) => {
        const archiveStem = path.basename(row.file, path.extname(row.file)).toLowerCase();
        return archiveStem === pluginStem || archiveStem.startsWith(`${pluginStem} - `) ||
          (pluginStem === "falloutnv" && archiveStem === "update");
      })
      .sort((left, right) => left.rootPriority - right.rootPriority ||
        left.file.localeCompare(right.file, "en", { sensitivity: "base", numeric: true }));
    for (const row of matches) {
      add(row, { kind: "enabled-plugin", plugin: plugin.file });
    }
  }
  return active;
}

function mergeEffectiveFiles(existing, discovered, rootId) {
  const replacements = new Set(discovered.map((row) => row.file.toLowerCase()));
  const rows = existing
    .filter((row) => !replacements.has(row.file.toLowerCase()))
    .map(({ rootId: owner, file, bytes, mtimeMs }) =>
      ({ rootId: owner, file, bytes, mtimeMs }));
  for (const discoveredRow of discovered) {
    rows.push({ rootId, ...discoveredRow });
  }
  return rows.map((row, index) => ({ index, ...row }));
}

export function resolveLooseFile(document, logicalPath) {
  validateModStack(document);
  const normalized = normalizeLogicalPath(logicalPath);
  const folded = normalized.toLowerCase();
  const roots = new Map(document.roots.map((root) => [root.id, root]));
  const versions = [];
  for (const row of document.looseFiles) {
    if (row.path.toLowerCase() !== folded) continue;
    const root = roots.get(row.rootId);
    versions.push({
      rootId: row.rootId,
      provider: root.provider,
      path: path.join(root.root, ...row.path.split("/"))
    });
  }
  return versions.length === 0
    ? null
    : { logicalPath: normalized.replaceAll("/", "\\"), winner: versions.at(-1), overridden: versions.slice(0, -1) };
}

function normalizeLogicalPath(value) {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error("A mod resource path is required.");
  }
  const normalized = value.replaceAll("\\", "/");
  if (path.posix.isAbsolute(normalized) || normalized.includes(":") ||
      normalized.split("/").some((segment) => !segment || segment === "." || segment === "..")) {
    throw new Error("The mod resource path escapes its source root.");
  }
  return normalized;
}
