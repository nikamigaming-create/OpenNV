import { createHash } from "node:crypto";
import {
  closeSync,
  existsSync,
  lstatSync,
  openSync,
  readFileSync,
  readdirSync,
  readSync,
  statSync
} from "node:fs";
import path from "node:path";

export const FO1_OWNED_PROFILE_SCHEMA = "opennv-fo1-owned-profile/v1";
const ARCHIVE_NAMES = Object.freeze(["master.dat", "critter.dat"]);
const OVERLAY = Object.freeze(["loose:data", "critter.dat", "master.dat"]);
const STORED_FLAG = 0x20;
const LZSS_FLAG = 0x40;
const SHA256_PATTERN = /^[0-9a-f]{64}$/u;
const MINIMUM_DAT1_ARCHIVE_BYTES = Number("16");
const MAXIMUM_DAT1_FOLDER_COUNT = Number("65535");
const MAXIMUM_DAT1_FILES_PER_FOLDER = Number("1000000");

function hashFile(file) {
  return createHash("sha256").update(readFileSync(file)).digest("hex");
}

function canonical(relative) {
  const parts = relative.replaceAll("/", "\\").replace(/^\\+|\\+$/gu, "").split("\\");
  if (!relative || path.isAbsolute(relative) || relative.includes(":") ||
      parts.some((part) => !part || part === "." || part === "..")) {
    throw new Error("A Fallout 1 resource path escapes its source namespace.");
  }
  return parts.join("\\").toLowerCase();
}

function dat1Identity(source) {
  const bytes = statSync(source).size;
  if (bytes < MINIMUM_DAT1_ARCHIVE_BYTES) throw new Error(`Fallout DAT1 archive is too small: ${source}`);
  const descriptor = openSync(source, "r");
  let position = 0;
  const read = (count, label) => {
    const buffer = Buffer.alloc(count);
    if (readSync(descriptor, buffer, 0, count, position) !== count) {
      throw new Error(`Fallout DAT1 directory is truncated at ${label}.`);
    }
    position += count;
    return buffer;
  };
  const uint32 = (label) => read(4, label).readUInt32BE();
  const pascal = (label) => {
    const length = read(1, `${label} length`)[0];
    if (length === 0) throw new Error(`Fallout DAT1 ${label} is empty.`);
    const value = read(length, label);
    if (value.some((byte) => byte > 0x7f)) throw new Error(`Fallout DAT1 ${label} is not ASCII.`);
    return value.toString("ascii");
  };
  try {
    const folderCount = uint32("folder count");
    if (folderCount < 1 || folderCount > MAXIMUM_DAT1_FOLDER_COUNT) {
      throw new Error("Fallout DAT1 folder count is invalid.");
    }
    const headerValues = [uint32("header 0"), uint32("header 1"), uint32("header 2")];
    const folders = Array.from({ length: folderCount }, (_, index) => pascal(`folder ${index}`));
    const paths = new Set();
    let firstMemberOffset = bytes;
    for (let folderIndex = 0; folderIndex < folders.length; folderIndex += 1) {
      const count = uint32(`folder ${folderIndex} count`);
      if (count > MAXIMUM_DAT1_FILES_PER_FOLDER) {
        throw new Error("Fallout DAT1 folder member count is invalid.");
      }
      uint32(`folder ${folderIndex} metadata 0`);
      uint32(`folder ${folderIndex} metadata 1`);
      uint32(`folder ${folderIndex} metadata 2`);
      let previous = "";
      for (let fileIndex = 0; fileIndex < count; fileIndex += 1) {
        const filename = pascal(`folder ${folderIndex} file ${fileIndex}`);
        const flag = uint32(`${filename} flag`);
        const offset = uint32(`${filename} offset`);
        const unpacked = uint32(`${filename} unpacked bytes`);
        const packed = uint32(`${filename} packed bytes`);
        const stored = flag === LZSS_FLAG ? packed : unpacked;
        const logical = canonical(folders[folderIndex] === "." ? filename : `${folders[folderIndex]}\\${filename}`);
        if (![STORED_FLAG, LZSS_FLAG].includes(flag) || offset + stored > bytes ||
            flag === STORED_FLAG && packed !== 0 && packed !== unpacked ||
            previous && previous.toLowerCase() > filename.toLowerCase() ||
            paths.has(logical)) {
          throw new Error(`Fallout DAT1 member identity is invalid: ${logical}`);
        }
        paths.add(logical);
        firstMemberOffset = Math.min(firstMemberOffset, offset);
        previous = filename;
      }
    }
    if (position > firstMemberOffset) throw new Error("Fallout DAT1 directory overlaps member data.");
    const directoryBytes = position;
    const directory = Buffer.alloc(directoryBytes);
    if (readSync(descriptor, directory, 0, directoryBytes, 0) !== directoryBytes) {
      throw new Error("Fallout DAT1 directory changed while registering.");
    }
    return {
      format: "fallout-dat1",
      entries: paths.size,
      headerValues,
      directoryBytes,
      directorySha256: createHash("sha256").update(directory).digest("hex")
    };
  } finally {
    closeSync(descriptor);
  }
}

function looseFiles(root) {
  if (!existsSync(root)) return [];
  const files = [];
  const visit = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const source = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`Fallout 1 loose links are unsupported: ${source}`);
      if (entry.isDirectory()) visit(source);
      else if (entry.isFile()) files.push(source);
    }
  };
  visit(root);
  return files.sort((left, right) => left.localeCompare(right, "en", { sensitivity: "base" })).map((source) => {
    const info = lstatSync(source);
    return {
      logicalPath: canonical(path.relative(root, source)),
      source: path.resolve(source),
      bytes: info.size,
      lastWriteTimeUtcUnixMilliseconds: Math.trunc(info.mtimeMs),
      sha256: hashFile(source)
    };
  });
}

export function createFo1OwnedProfile(installDirectory) {
  const root = path.resolve(installDirectory);
  const names = new Map(readdirSync(root, { withFileTypes: true })
    .filter((entry) => entry.isFile()).map((entry) => [entry.name.toLowerCase(), entry.name]));
  const identity = ["opennv-fo1-owned-profile/v1\0"];
  const archives = ARCHIVE_NAMES.map((file) => {
    const actual = names.get(file);
    if (!actual) throw new Error(`The selected Fallout 1 install is missing ${file}.`);
    const source = path.join(root, actual);
    const info = statSync(source);
    const sha256 = hashFile(source);
    const formatIdentity = dat1Identity(source);
    identity.push(`${file}\0${sha256}\0${formatIdentity.directorySha256}\0`);
    return {
      file,
      source: path.resolve(source),
      bytes: info.size,
      lastWriteTimeUtcUnixMilliseconds: Math.trunc(info.mtimeMs),
      sha256,
      formatIdentity
    };
  });
  const looseRoot = path.join(root, "DATA");
  const files = looseFiles(looseRoot);
  for (const row of files) identity.push(`${row.logicalPath}\0${row.sha256}\0`);
  const sourceProfileId = createHash("sha256").update(identity.join(""), "utf8").digest("hex");
  return {
    schema: FO1_OWNED_PROFILE_SCHEMA,
    status: "registered-owned-install",
    campaign: "Fallout1",
    sourceProfileId,
    saveCompatibilityId: `fallout1:${sourceProfileId}`,
    retailOrDerivedAssetsPackaged: false,
    install: {
      root,
      archives,
      loose: { root: looseRoot, count: files.length, files },
      overlayOrderHighToLow: [...OVERLAY]
    },
    runtimeCompatibility: {
      nativeResourceSource: true,
      mapProFrmClosure: true,
      fullMapObjectGraph: true,
      scripts: false,
      gameplay: false
    }
  };
}

export function validateFo1OwnedProfile(profile) {
  if (profile?.schema !== FO1_OWNED_PROFILE_SCHEMA || profile?.status !== "registered-owned-install" ||
      profile?.campaign !== "Fallout1" || !SHA256_PATTERN.test(profile?.sourceProfileId || "") ||
      profile?.saveCompatibilityId !== `fallout1:${profile.sourceProfileId}` ||
      profile?.retailOrDerivedAssetsPackaged !== false ||
      JSON.stringify(profile?.install?.overlayOrderHighToLow) !== JSON.stringify(OVERLAY) ||
      !Array.isArray(profile?.install?.archives) || profile.install.archives.length !== ARCHIVE_NAMES.length) {
    throw new Error("The Fallout 1 owned profile identity is invalid.");
  }
  const root = path.resolve(profile.install.root);
  for (const file of ARCHIVE_NAMES) {
    const row = profile.install.archives.find((candidate) => candidate?.file === file);
    const source = path.resolve(row?.source || "");
    const info = existsSync(source) ? statSync(source) : null;
    const format = info ? dat1Identity(source) : null;
    if (!row || path.resolve(path.dirname(source)).toLowerCase() !== root.toLowerCase() ||
        path.basename(source).toLowerCase() !== file || !info || info.size !== row.bytes ||
        Math.trunc(info.mtimeMs) !== row.lastWriteTimeUtcUnixMilliseconds ||
        !SHA256_PATTERN.test(row.sha256 || "") ||
        format.format !== row.formatIdentity?.format || format.entries !== row.formatIdentity?.entries ||
        format.directoryBytes !== row.formatIdentity?.directoryBytes ||
        format.directorySha256 !== row.formatIdentity?.directorySha256) {
      throw new Error(`The registered Fallout 1 archive changed: ${file}`);
    }
  }
  const loose = profile.install.loose;
  const looseRoot = path.resolve(loose?.root || "");
  if (path.dirname(looseRoot).toLowerCase() !== root.toLowerCase() ||
      path.basename(looseRoot).toLowerCase() !== "data" || !Array.isArray(loose?.files) ||
      loose.count !== loose.files.length) {
    throw new Error("The registered Fallout 1 loose inventory is invalid.");
  }
  const logicalPaths = new Set();
  for (const row of loose.files) {
    const logicalPath = canonical(row?.logicalPath || "");
    const source = path.resolve(row?.source || "");
    const expected = path.resolve(looseRoot, ...logicalPath.split("\\"));
    const info = existsSync(source) ? statSync(source) : null;
    if (source.toLowerCase() !== expected.toLowerCase() || logicalPaths.has(logicalPath) || !info ||
        info.size !== row.bytes || Math.trunc(info.mtimeMs) !== row.lastWriteTimeUtcUnixMilliseconds ||
        !SHA256_PATTERN.test(row.sha256 || "")) {
      throw new Error(`The registered Fallout 1 loose file changed: ${logicalPath}`);
    }
    logicalPaths.add(logicalPath);
  }
  return profile;
}
