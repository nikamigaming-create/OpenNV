import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { createFo1OwnedProfile, validateFo1OwnedProfile } from "../src/fo1-owned-profile.mjs";

function dat1(filename, payload) {
  const folder = Buffer.from(".", "ascii");
  const name = Buffer.from(filename, "ascii");
  const directoryBytes = 4 + 12 + 1 + folder.length + 4 + 12 + 1 + name.length + 16;
  const directory = Buffer.alloc(directoryBytes);
  let offset = 0;
  const uint32 = (value) => { directory.writeUInt32BE(value, offset); offset += 4; };
  uint32(1);
  uint32(1);
  uint32(2);
  uint32(3);
  directory[offset++] = folder.length;
  folder.copy(directory, offset);
  offset += folder.length;
  uint32(1);
  uint32(0);
  uint32(0);
  uint32(0);
  directory[offset++] = name.length;
  name.copy(directory, offset);
  offset += name.length;
  uint32(0x20);
  uint32(directoryBytes);
  uint32(payload.length);
  uint32(payload.length);
  assert.equal(offset, directoryBytes);
  return Buffer.concat([directory, payload]);
}

test("Fallout 1 install registration emits only a sealed native source profile", (context) => {
  const root = mkdtempSync(path.join(os.tmpdir(), "opennv-fo1-profile-"));
  context.after(() => rmSync(root, { recursive: true, force: true }));
  writeFileSync(path.join(root, "MASTER.DAT"), dat1("master.bin", Buffer.from("master")));
  writeFileSync(path.join(root, "CRITTER.DAT"), dat1("critter.bin", Buffer.from("critter")));
  mkdirSync(path.join(root, "DATA", "art", "tiles"), { recursive: true });
  writeFileSync(path.join(root, "DATA", "art", "tiles", "grid000.frm"), Buffer.from("loose"));

  const profile = validateFo1OwnedProfile(createFo1OwnedProfile(root));
  assert.equal(profile.runtimeCompatibility.fullMapObjectGraph, true);
  assert.equal(profile.schema, "opennv-fo1-owned-profile/v1");
  assert.equal(profile.install.loose.count, 1);
  assert.deepEqual(profile.install.overlayOrderHighToLow,
    ["loose:data", "critter.dat", "master.dat"]);
  assert.deepEqual(profile.generatedCaches, []);
  assert.equal(JSON.stringify(profile).includes("hexScene"), false);
  assert.equal(JSON.stringify(profile).includes("characterStart"), false);

  writeFileSync(path.join(root, "MASTER.DAT"), Buffer.concat([
    dat1("master.bin", Buffer.from("master")), Buffer.from("drift")
  ]));
  assert.throws(() => validateFo1OwnedProfile(profile), /archive changed/u);
});
