import { createHash, randomUUID } from "node:crypto";
import {
  closeSync,
  existsSync,
  lstatSync,
  mkdirSync,
  openSync,
  readFileSync,
  renameSync,
  rmSync,
  statSync,
  utimesSync,
  writeFileSync
} from "node:fs";
import path from "node:path";
import { inflateRawSync } from "node:zlib";

export const LOCAL_MOD_INSTALL_SCHEMA = "opennv-local-mod-install/v1";

const EOCD_SIGNATURE = 0x06054b50;
const CENTRAL_SIGNATURE = 0x02014b50;
const LOCAL_SIGNATURE = 0x04034b50;
const MAX_EOCD_SEARCH = 65_557;
const MAX_ENTRIES = 100_000;
const MAX_UNCOMPRESSED_BYTES = 17_179_869_184;
const MAX_ENTRY_BYTES = 4_294_967_296;
const ZIP64_SENTINEL = 0xffffffff;
const ZIP_ENCRYPTED = 0x0001;
const ZIP_UTF8 = 0x0800;
const UNIX_FILE_TYPE_MASK = 0xf000;
const UNIX_DIRECTORY = 0x4000;
const UNIX_REGULAR = 0x8000;
const UNIX_SYMLINK = 0xa000;
const CRC32_BYTE_BITS = 8;
const ZIP_EOCD_FIXED_BYTES = 22;
const ZIP_EOCD_CENTRAL_DISK_OFFSET = 6;
const ZIP_EOCD_DISK_ENTRIES_OFFSET = 8;
const ZIP_EOCD_ENTRIES_OFFSET = 10;
const ZIP_EOCD_CENTRAL_BYTES_OFFSET = 12;
const ZIP_EOCD_CENTRAL_OFFSET_OFFSET = 16;
const ZIP_CENTRAL_FIXED_BYTES = 46;
const ZIP_CENTRAL_FLAGS_OFFSET = 8;
const ZIP_CENTRAL_METHOD_OFFSET = 10;
const ZIP_CENTRAL_CRC_OFFSET = 16;
const ZIP_CENTRAL_COMPRESSED_BYTES_OFFSET = 20;
const ZIP_CENTRAL_UNCOMPRESSED_BYTES_OFFSET = 24;
const ZIP_CENTRAL_NAME_BYTES_OFFSET = 28;
const ZIP_CENTRAL_EXTRA_BYTES_OFFSET = 30;
const ZIP_CENTRAL_COMMENT_BYTES_OFFSET = 32;
const ZIP_CENTRAL_EXTERNAL_ATTRIBUTES_OFFSET = 38;
const ZIP_CENTRAL_LOCAL_OFFSET_OFFSET = 42;
const ZIP_STORED_METHOD = 0;
const ZIP_DEFLATE_METHOD = 8;
const ZIP_HOST_SYSTEM_SHIFT = 8;
const ZIP_UNIX_HOST_SYSTEM = 3;
const ZIP_UNIX_MODE_SHIFT = 16;
const ZIP_LOCAL_FIXED_BYTES = 30;
const ZIP_LOCAL_NAME_BYTES_OFFSET = 26;
const ZIP_LOCAL_EXTRA_BYTES_OFFSET = 28;
const INSTALL_ID_DIGEST_CHARACTERS = 12;
const DATA_DIRECTORY_NAME = "data";

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < CRC32_BYTE_BITS; bit += 1) {
      crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function findEocd(archive) {
  const floor = Math.max(0, archive.length - MAX_EOCD_SEARCH);
  for (let offset = archive.length - ZIP_EOCD_FIXED_BYTES; offset >= floor; offset -= 1) {
    if (archive.readUInt32LE(offset) === EOCD_SIGNATURE) return offset;
  }
  throw new Error("The selected file is not a complete ZIP archive.");
}

function decodeName(bytes, flags) {
  const name = bytes.toString((flags & ZIP_UTF8) !== 0 ? "utf8" : "latin1");
  if (name.includes("\u0000") || name.includes("\ufffd")) {
    throw new Error("The ZIP contains an invalid member name.");
  }
  return name;
}

function safeMemberName(sourceName) {
  const normalized = sourceName.replaceAll("\\", "/");
  if (!normalized || normalized.startsWith("/") || /^[a-z]:/iu.test(normalized)) {
    throw new Error(`The ZIP member escapes the install folder: ${sourceName}`);
  }
  const directory = normalized.endsWith("/");
  const segments = normalized.split("/").filter((segment, index, rows) =>
    directory && index === rows.length - 1 ? false : true);
  if (segments.some((segment) => !segment || segment === "." || segment === ".." || segment.includes(":"))) {
    throw new Error(`The ZIP member escapes the install folder: ${sourceName}`);
  }
  return { directory, segments };
}

function parseEntries(archive) {
  const eocd = findEocd(archive);
  const disk = archive.readUInt16LE(eocd + 4);
  const centralDisk = archive.readUInt16LE(eocd + ZIP_EOCD_CENTRAL_DISK_OFFSET);
  const diskEntries = archive.readUInt16LE(eocd + ZIP_EOCD_DISK_ENTRIES_OFFSET);
  const entries = archive.readUInt16LE(eocd + ZIP_EOCD_ENTRIES_OFFSET);
  const centralBytes = archive.readUInt32LE(eocd + ZIP_EOCD_CENTRAL_BYTES_OFFSET);
  const centralOffset = archive.readUInt32LE(eocd + ZIP_EOCD_CENTRAL_OFFSET_OFFSET);
  if (disk !== 0 || centralDisk !== 0 || diskEntries !== entries) {
    throw new Error("Multi-volume ZIP archives are not supported.");
  }
  if (entries === 0xffff || centralBytes === ZIP64_SENTINEL || centralOffset === ZIP64_SENTINEL) {
    throw new Error("ZIP64 archives are not supported by the built-in installer yet.");
  }
  if (entries === 0 || entries > MAX_ENTRIES || centralOffset + centralBytes > eocd) {
    throw new Error("The ZIP central directory is invalid.");
  }
  const result = [];
  const foldedPaths = new Set();
  let totalBytes = 0;
  let cursor = centralOffset;
  for (let index = 0; index < entries; index += 1) {
    if (cursor + ZIP_CENTRAL_FIXED_BYTES > eocd || archive.readUInt32LE(cursor) !== CENTRAL_SIGNATURE) {
      throw new Error("The ZIP central directory is truncated.");
    }
    const madeBy = archive.readUInt16LE(cursor + 4);
    const flags = archive.readUInt16LE(cursor + ZIP_CENTRAL_FLAGS_OFFSET);
    const method = archive.readUInt16LE(cursor + ZIP_CENTRAL_METHOD_OFFSET);
    const expectedCrc = archive.readUInt32LE(cursor + ZIP_CENTRAL_CRC_OFFSET);
    const compressedBytes = archive.readUInt32LE(cursor + ZIP_CENTRAL_COMPRESSED_BYTES_OFFSET);
    const uncompressedBytes = archive.readUInt32LE(cursor + ZIP_CENTRAL_UNCOMPRESSED_BYTES_OFFSET);
    const nameBytes = archive.readUInt16LE(cursor + ZIP_CENTRAL_NAME_BYTES_OFFSET);
    const extraBytes = archive.readUInt16LE(cursor + ZIP_CENTRAL_EXTRA_BYTES_OFFSET);
    const commentBytes = archive.readUInt16LE(cursor + ZIP_CENTRAL_COMMENT_BYTES_OFFSET);
    const externalAttributes = archive.readUInt32LE(cursor + ZIP_CENTRAL_EXTERNAL_ATTRIBUTES_OFFSET);
    const localOffset = archive.readUInt32LE(cursor + ZIP_CENTRAL_LOCAL_OFFSET_OFFSET);
    const end = cursor + ZIP_CENTRAL_FIXED_BYTES + nameBytes + extraBytes + commentBytes;
    if (end > eocd || [compressedBytes, uncompressedBytes, localOffset].includes(ZIP64_SENTINEL)) {
      throw new Error("The ZIP uses an unsupported or truncated member layout.");
    }
    if ((flags & ZIP_ENCRYPTED) !== 0) throw new Error("Encrypted ZIP members are not supported.");
    if (![ZIP_STORED_METHOD, ZIP_DEFLATE_METHOD].includes(method)) {
      throw new Error(`ZIP compression method ${method} is not supported.`);
    }
    if (uncompressedBytes > MAX_ENTRY_BYTES || totalBytes + uncompressedBytes > MAX_UNCOMPRESSED_BYTES) {
      throw new Error("The ZIP expands beyond the built-in install safety limit.");
    }
    const sourceName = decodeName(
      archive.subarray(cursor + ZIP_CENTRAL_FIXED_BYTES, cursor + ZIP_CENTRAL_FIXED_BYTES + nameBytes),
      flags);
    const safe = safeMemberName(sourceName);
    const unixMode = (madeBy >>> ZIP_HOST_SYSTEM_SHIFT) === ZIP_UNIX_HOST_SYSTEM
      ? (externalAttributes >>> ZIP_UNIX_MODE_SHIFT) & UNIX_FILE_TYPE_MASK
      : 0;
    if (unixMode === UNIX_SYMLINK || (unixMode !== 0 && unixMode !== UNIX_REGULAR && unixMode !== UNIX_DIRECTORY)) {
      throw new Error(`The ZIP contains a link or special file: ${sourceName}`);
    }
    const folded = safe.segments.join("/").toLowerCase();
    if (foldedPaths.has(folded)) throw new Error(`The ZIP repeats a path case-insensitively: ${sourceName}`);
    foldedPaths.add(folded);
    totalBytes += uncompressedBytes;
    result.push({
      sourceName,
      segments: safe.segments,
      directory: safe.directory || unixMode === UNIX_DIRECTORY,
      method,
      expectedCrc,
      compressedBytes,
      uncompressedBytes,
      localOffset
    });
    cursor = end;
  }
  if (cursor !== centralOffset + centralBytes) throw new Error("The ZIP central-directory size does not match its entries.");
  return result;
}

function contentPrefix(entries) {
  const files = entries.filter((entry) => !entry.directory);
  if (files.length === 0) return 0;
  const first = files[0].segments;
  const dataIndex = first.findIndex((segment) => segment.toLowerCase() === DATA_DIRECTORY_NAME);
  if (dataIndex >= 0 && files.every((entry) =>
    entry.segments.length > dataIndex + 1 &&
    entry.segments[dataIndex].toLowerCase() === DATA_DIRECTORY_NAME &&
    entry.segments.slice(0, dataIndex).every((segment, index) =>
      segment.toLowerCase() === first[index].toLowerCase()))) {
    return dataIndex + 1;
  }
  return 0;
}

function memberBytes(archive, entry) {
  const offset = entry.localOffset;
  if (offset + ZIP_LOCAL_FIXED_BYTES > archive.length || archive.readUInt32LE(offset) !== LOCAL_SIGNATURE) {
    throw new Error(`The ZIP local header is missing: ${entry.sourceName}`);
  }
  const nameBytes = archive.readUInt16LE(offset + ZIP_LOCAL_NAME_BYTES_OFFSET);
  const extraBytes = archive.readUInt16LE(offset + ZIP_LOCAL_EXTRA_BYTES_OFFSET);
  const dataOffset = offset + ZIP_LOCAL_FIXED_BYTES + nameBytes + extraBytes;
  const dataEnd = dataOffset + entry.compressedBytes;
  if (dataEnd > archive.length) throw new Error(`The ZIP member is truncated: ${entry.sourceName}`);
  const compressed = archive.subarray(dataOffset, dataEnd);
  const output = entry.method === ZIP_STORED_METHOD ? Buffer.from(compressed) : inflateRawSync(compressed, {
    maxOutputLength: entry.uncompressedBytes
  });
  if (output.length !== entry.uncompressedBytes || crc32(output) !== entry.expectedCrc) {
    throw new Error(`The ZIP member failed size or CRC validation: ${entry.sourceName}`);
  }
  return output;
}

function slug(value) {
  return value.toLowerCase().replaceAll(/[^a-z\d]+/gu, "-").replaceAll(/^-|-$/gu, "") || "mod";
}

export function installLocalZip(archivePath, installsRoot, { installedAt = new Date().toISOString() } = {}) {
  const source = path.resolve(archivePath);
  if (path.extname(source).toLowerCase() !== ".zip" || !existsSync(source) || !statSync(source).isFile()) {
    throw new Error("Choose a local ZIP archive. 7z and scripted FOMOD installers are not supported yet.");
  }
  const archive = readFileSync(source);
  const archiveSha256 = createHash("sha256").update(archive).digest("hex");
  const entries = parseEntries(archive);
  if (entries.some((entry) => entry.segments.map((segment) => segment.toLowerCase())
    .join("/").endsWith("fomod/moduleconfig.xml"))) {
    throw new Error(
      "This ZIP requires a scripted FOMOD choice graph. Gate Vortex does not infer FOMOD selections; install it with its supported manager and add the deployed folder or MO2/Wabbajack profile.");
  }
  const prefix = contentPrefix(entries);
  const baseName = path.basename(source, path.extname(source));
  const installId = `${slug(baseName)}-${archiveSha256.slice(0, INSTALL_ID_DIGEST_CHARACTERS)}`;
  const root = path.resolve(installsRoot);
  const destination = path.join(root, installId);
  if (existsSync(destination)) {
    throw new Error(`This exact archive is already installed as ${installId}; OpenNV will not overwrite it.`);
  }
  mkdirSync(root, { recursive: true });
  const staging = path.join(root, `.installing-${installId}-${randomUUID()}`);
  const contentRoot = path.join(staging, "content");
  mkdirSync(contentRoot, { recursive: true });
  let files = 0;
  try {
    for (const entry of entries) {
      const segments = entry.segments.slice(prefix);
      if (segments.length === 0) continue;
      const target = path.join(contentRoot, ...segments);
      const relative = path.relative(contentRoot, target);
      if (!relative || relative.startsWith("..") || path.isAbsolute(relative)) {
        throw new Error(`The ZIP member escapes the install folder: ${entry.sourceName}`);
      }
      if (entry.directory) {
        mkdirSync(target, { recursive: true });
        continue;
      }
      mkdirSync(path.dirname(target), { recursive: true });
      let ancestor = path.dirname(target);
      while (ancestor !== contentRoot) {
        if (lstatSync(ancestor).isSymbolicLink()) throw new Error("The install staging path contains a symbolic link.");
        ancestor = path.dirname(ancestor);
      }
      const handle = openSync(target, "wx", 0o600);
      try {
        writeFileSync(handle, memberBytes(archive, entry));
      } finally {
        closeSync(handle);
      }
      files += 1;
    }
    if (files === 0) throw new Error("The ZIP contains no installable files.");
    const metadata = {
      schema: LOCAL_MOD_INSTALL_SCHEMA,
      installId,
      displayName: baseName,
      archive: { path: source, bytes: archive.length, sha256: archiveSha256 },
      layout: prefix > 0 ? "data-directory-stripped" : "archive-root",
      contentRoot: path.join(destination, "content"),
      installedAt,
      files
    };
    writeFileSync(path.join(staging, "install.json"), `${JSON.stringify(metadata, null, 2)}\n`, { encoding: "utf8", flag: "wx" });
    utimesSync(path.join(staging, "install.json"), new Date(installedAt), new Date(installedAt));
    renameSync(staging, destination);
    return { ...metadata, metadataPath: path.join(destination, "install.json") };
  } catch (error) {
    if (existsSync(staging)) rmSync(staging, { recursive: true, force: true });
    throw error;
  }
}

export function removeLocalInstall(install) {
  const destination = path.dirname(path.resolve(install.metadataPath));
  if (path.basename(destination) !== install.installId || !existsSync(path.join(destination, "install.json"))) {
    throw new Error("Refusing to remove an unverified local install folder.");
  }
  rmSync(destination, { recursive: true, force: false });
}
