import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { deflateRawSync } from "node:zlib";
import { installLocalZip, LOCAL_MOD_INSTALL_SCHEMA, removeLocalInstall } from "../src/local-mod-installer.mjs";
import { synchronizeManagedLayers, updateManagedLayer } from "../src/gate-vortex-layers.mjs";
import { appendSourceRoot, createModStack, rebuildManagedSourceLayers } from "../src/mod-stack-contract.mjs";

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function zip(entries) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const entry of entries) {
    const name = Buffer.from(entry.name, "utf8");
    const content = Buffer.from(entry.content || "", "utf8");
    const compressed = entry.store ? content : deflateRawSync(content);
    const method = entry.store ? 0 : 8;
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6);
    local.writeUInt16LE(method, 8);
    local.writeUInt32LE(crc32(content), 14);
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(content.length, 22);
    local.writeUInt16LE(name.length, 26);
    locals.push(local, name, compressed);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE((3 << 8) | 20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(method, 10);
    central.writeUInt32LE(crc32(content), 16);
    central.writeUInt32LE(compressed.length, 20);
    central.writeUInt32LE(content.length, 24);
    central.writeUInt16LE(name.length, 28);
    central.writeUInt32LE(entry.mode === "symlink" ? 0xa1ff0000 : 0x81a40000, 38);
    central.writeUInt32LE(offset, 42);
    centrals.push(central, name);
    offset += local.length + name.length + compressed.length;
  }
  const centralBytes = Buffer.concat(centrals);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(centralBytes.length, 12);
  eocd.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, centralBytes, eocd]);
}

function withWorkspace(run) {
  const workspace = mkdtempSync(path.join(tmpdir(), "opennv-local-installer-"));
  try {
    run(workspace);
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
}

test("the built-in installer extracts a Data-wrapped ZIP into a private immutable layer", () => {
  withWorkspace((workspace) => {
    const archive = path.join(workspace, "Example Mod.zip");
    writeFileSync(archive, zip([
      { name: "Data/Example.esp", content: "TES4 plugin" },
      { name: "Data/textures/example.dds", content: "DDS bytes", store: true }
    ]));
    const result = installLocalZip(archive, path.join(workspace, "installs"), {
      installedAt: "2026-09-01T12:00:00.000Z"
    });
    assert.equal(result.schema, LOCAL_MOD_INSTALL_SCHEMA);
    assert.equal(result.layout, "data-directory-stripped");
    assert.equal(readFileSync(path.join(result.contentRoot, "Example.esp"), "utf8"), "TES4 plugin");
    assert.equal(readFileSync(path.join(result.contentRoot, "textures", "example.dds"), "utf8"), "DDS bytes");
    const metadata = JSON.parse(readFileSync(result.metadataPath, "utf8"));
    assert.equal(metadata.archive.sha256.length, 64);
    assert.equal(metadata.files, 2);
    assert.throws(() => installLocalZip(archive, path.join(workspace, "installs")), /already installed/u);
  });
});

test("the built-in installer recognizes a Nexus-style wrapper above Data", () => {
  withWorkspace((workspace) => {
    const archive = path.join(workspace, "Wrapped Mod.zip");
    writeFileSync(archive, zip([
      { name: "Wrapped Mod/Data/Wrapped.esp", content: "TES4 plugin" },
      { name: "Wrapped Mod/Data/meshes/wrapped.nif", content: "NIF bytes" }
    ]));
    const result = installLocalZip(archive, path.join(workspace, "installs"));
    assert.equal(result.layout, "data-directory-stripped");
    assert.equal(readFileSync(path.join(result.contentRoot, "Wrapped.esp"), "utf8"), "TES4 plugin");
    assert.equal(readFileSync(path.join(result.contentRoot, "meshes", "wrapped.nif"), "utf8"), "NIF bytes");
    assert.equal(existsSync(path.join(result.contentRoot, "Wrapped Mod")), false);
  });
});

test("the built-in installer rejects traversal, absolute paths, and symbolic links", () => {
  withWorkspace((workspace) => {
    for (const [name, member] of [
      ["traversal", { name: "../outside.esp", content: "bad" }],
      ["absolute", { name: "C:/outside.esp", content: "bad" }],
      ["symlink", { name: "Data/link", content: "target", mode: "symlink" }]
    ]) {
      const archive = path.join(workspace, `${name}.zip`);
      writeFileSync(archive, zip([member]));
      assert.throws(() => installLocalZip(archive, path.join(workspace, "installs")), /escapes|link or special/u);
    }
    assert.equal(existsSync(path.join(workspace, "outside.esp")), false);
  });
});

test("the built-in installer fails closed on corrupt member data", () => {
  withWorkspace((workspace) => {
    const archive = path.join(workspace, "corrupt.zip");
    const bytes = zip([{ name: "Corrupt.esp", content: "TES4 source", store: true }]);
    bytes[30 + Buffer.byteLength("Corrupt.esp")] ^= 0xff;
    writeFileSync(archive, bytes);
    assert.throws(() => installLocalZip(archive, path.join(workspace, "installs")), /CRC validation/u);
  });
});

test("the built-in installer fails closed on scripted FOMOD choices", () => {
  withWorkspace((workspace) => {
    const archive = path.join(workspace, "scripted-fomod.zip");
    writeFileSync(archive, zip([
      { name: "Data/fomod/ModuleConfig.xml", content: "<config />" },
      { name: "Data/optional/choice-a/Chosen.esp", content: "TES4 plugin" }
    ]));
    assert.throws(
      () => installLocalZip(archive, path.join(workspace, "installs")),
      /requires a scripted FOMOD choice graph/u);
  });
});

test("managed Gate Vortex layers enable, disable, reorder, and uninstall without changing owned Data", () => {
  withWorkspace((workspace) => {
    const owned = path.join(workspace, "Data");
    mkdirSync(owned);
    const master = path.join(owned, "FalloutNV.esm");
    writeFileSync(master, "TES4 owned master");
    const metadata = statSync(master);
    let stack = createModStack({
      name: "Managed test stack",
      roots: [{ id: "owned-data", provider: "manual", root: owned }],
      plugins: [{
        index: 0,
        rootId: "owned-data",
        file: "FalloutNV.esm",
        bytes: metadata.size,
        mtimeMs: Math.trunc(metadata.mtimeMs)
      }]
    });
    const installs = path.join(workspace, "installs");
    const installed = [];
    for (const name of ["Low Mod", "High Mod"]) {
      const archive = path.join(workspace, `${name}.zip`);
      writeFileSync(archive, zip([{ name: `Data/${name.replace(" ", "")}.esp`, content: "TES4 plugin" }]));
      const mod = installLocalZip(archive, installs);
      installed.push(mod);
      stack = appendSourceRoot(stack, {
        id: `gate-${mod.installId}`,
        provider: "gate-vortex",
        root: mod.contentRoot
      });
    }
    const ownedBefore = readFileSync(master);
    const catalog = synchronizeManagedLayers(stack);
    assert.deepEqual(catalog.layers.map((layer) => layer.displayName), ["Low Mod", "High Mod"]);

    const disabled = updateManagedLayer(catalog, catalog.layers[1].id, "disable");
    const disabledStack = rebuildManagedSourceLayers(stack, disabled.layers);
    assert.equal(disabledStack.roots.some((root) => root.id === catalog.layers[1].id), false);
    assert.notEqual(disabledStack.stackId, stack.stackId);

    const reordered = updateManagedLayer(catalog, catalog.layers[1].id, "move-up");
    const reorderedStack = rebuildManagedSourceLayers(stack, reordered.layers);
    assert.deepEqual(reorderedStack.roots.slice(1).map((root) => root.id),
      [catalog.layers[1].id, catalog.layers[0].id]);
    assert.notEqual(reorderedStack.stackId, stack.stackId);

    const uninstalled = updateManagedLayer(catalog, catalog.layers[1].id, "uninstall");
    const uninstallStack = rebuildManagedSourceLayers(stack, uninstalled.layers);
    assert.equal(uninstallStack.roots.some((root) => root.id === catalog.layers[1].id), false);
    removeLocalInstall(installed[1]);
    assert.equal(existsSync(path.dirname(installed[1].metadataPath)), false);
    assert.deepEqual(readFileSync(master), ownedBefore);
  });
});

test("external deployed layers cannot be uninstalled by Gate Vortex", () => {
  withWorkspace((workspace) => {
    const owned = path.join(workspace, "Data");
    const external = path.join(workspace, "MO2 Mod");
    mkdirSync(owned);
    mkdirSync(external);
    writeFileSync(path.join(owned, "FalloutNV.esm"), "TES4 owned master");
    writeFileSync(path.join(external, "External.esp"), "TES4 plugin");
    const master = statSync(path.join(owned, "FalloutNV.esm"));
    let stack = createModStack({
      name: "External test stack",
      roots: [{ id: "owned-data", provider: "manual", root: owned }],
      plugins: [{ index: 0, rootId: "owned-data", file: "FalloutNV.esm", bytes: master.size,
        mtimeMs: Math.trunc(master.mtimeMs) }]
    });
    stack = appendSourceRoot(stack, { id: "001-external", provider: "mo2", root: external });
    const catalog = synchronizeManagedLayers(stack);
    assert.throws(
      () => updateManagedLayer(catalog, "001-external", "uninstall"),
      /external MO2, Vortex, Wabbajack, TTW, JAM, and manual folders remain read-only/u);
  });
});

test("New Vegas and Fallout 3 managed catalogs and stack identities cannot cross", () => {
  withWorkspace((workspace) => {
    const createBase = (game, rootId, masterName) => {
      const root = path.join(workspace, game);
      mkdirSync(root);
      const masterPath = path.join(root, masterName);
      writeFileSync(masterPath, `TES4 ${game}`);
      const metadata = statSync(masterPath);
      return createModStack({
        game,
        name: `${game} stack`,
        roots: [{ id: rootId, provider: "manual", root }],
        plugins: [{ index: 0, rootId, file: masterName, bytes: metadata.size,
          mtimeMs: Math.trunc(metadata.mtimeMs) }]
      });
    };
    let newVegas = createBase("fallout-new-vegas", "owned-data", "FalloutNV.esm");
    let fallout3 = createBase("fallout-3", "owned-fallout3-data", "Fallout3.esm");
    for (const [game, stack, id] of [
      ["fallout-new-vegas", newVegas, "nv-layer"],
      ["fallout-3", fallout3, "fo3-layer"]
    ]) {
      const root = path.join(workspace, `${game}-mod`);
      mkdirSync(root);
      writeFileSync(path.join(root, `${id}.esp`), `TES4 ${id}`);
      const updated = appendSourceRoot(stack, { id, provider: "manual", root });
      if (game === "fallout-new-vegas") newVegas = updated;
      else fallout3 = updated;
    }
    const newVegasCatalog = synchronizeManagedLayers(newVegas);
    const fallout3Catalog = synchronizeManagedLayers(fallout3);
    assert.equal(newVegasCatalog.game, "fallout-new-vegas");
    assert.equal(fallout3Catalog.game, "fallout-3");
    assert.notEqual(newVegas.stackId, fallout3.stackId);
    assert.throws(
      () => synchronizeManagedLayers(fallout3, newVegasCatalog),
      /refuses to apply a layer catalog from another game profile/u);
    const disabledFo3 = updateManagedLayer(fallout3Catalog, "fo3-layer", "disable");
    const rebuiltFo3 = rebuildManagedSourceLayers(fallout3, disabledFo3.layers);
    assert.equal(rebuiltFo3.game, "fallout-3");
    assert.deepEqual(rebuiltFo3.roots.map((root) => root.id), ["owned-fallout3-data"]);
    assert.deepEqual(newVegas.roots.map((root) => root.id), ["owned-data", "nv-layer"]);
  });
});
