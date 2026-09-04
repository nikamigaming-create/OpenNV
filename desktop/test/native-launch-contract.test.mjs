import test from "node:test";
import assert from "node:assert/strict";
import { createRuntimeArguments, validateLaunchRequest } from "../src/contract.mjs";
import { createLaunchInvocation } from "../src/native-launch-contract.mjs";

const STACK_ID = "e".repeat(64);
const RUNTIME_COMMAND = {
  executable: "D:\\OpenNV\\OpenNV.exe",
  prefixArguments: []
};

function nativeProfiles({ ttwDerived = false } = {}) {
  return {
    newVegasProfile: {
      ready: true,
      dataRoot: ttwDerived ? "D:\\TTW\\Stock Game\\Data" : "D:\\Games\\Fallout New Vegas\\Data",
      stackId: STACK_ID,
      savePath: `D:\\Profiles\\NewVegas\\stacks\\${STACK_ID}\\courier-v1.json`
    },
    modStack: {
      validated: true,
      stackId: STACK_ID,
      path: ttwDerived
        ? "D:\\Profiles\\NewVegas\\ttw-native-mod-stack.json"
        : "D:\\Profiles\\NewVegas\\mod-stack.json",
      sha256: "f".repeat(64),
      orderSource: { kind: ttwDerived ? "ttw-profile" : "fnv-profile" }
    }
  };
}

for (const [label, profiles] of [
  ["ordinary New Vegas", nativeProfiles()],
  ["TTW-derived New Vegas", nativeProfiles({ ttwDerived: true })]
]) {
  test(`${label} launch hands only the native source stack to the runtime`, () => {
    const runtimeArguments = createRuntimeArguments(
      validateLaunchRequest({ campaign: "newvegas" }),
      profiles);
    const invocation = createLaunchInvocation(RUNTIME_COMMAND, runtimeArguments);
    assert.equal(invocation.executable, RUNTIME_COMMAND.executable);
    assert.deepEqual(invocation.arguments, runtimeArguments);
    assert.equal(invocation.arguments.includes("--source-stack"), true);
    assert.equal(invocation.arguments.includes("--source-stack-sha256"), true);
    assert.equal(invocation.arguments.includes("--stack-id"), true);
    assert.equal(invocation.arguments.includes("--campaign"), true);
    assert.equal(invocation.arguments.includes("--source-root"), false);
    assert.equal(invocation.arguments.includes("--mod-stack"), false);
    assert.equal(invocation.arguments.includes("--cache-root"), false);
    assert.equal(invocation.arguments.includes("--reuse-cache"), false);
    assert.equal(invocation.arguments.some((value) => value.endsWith(".py")), false);
  });
}

test("native New Vegas invocation rejects Python and prepared-cache arguments", () => {
  const runtimeArguments = createRuntimeArguments(
    validateLaunchRequest({ campaign: "newvegas" }),
    nativeProfiles());
  assert.throws(
    () => createLaunchInvocation(
      { executable: "C:\\Python313\\python.exe", prefixArguments: [] },
      runtimeArguments),
    /cannot invoke prepared-cache tooling/u);
  assert.throws(
    () => createLaunchInvocation(RUNTIME_COMMAND, [...runtimeArguments, "--cache-root", "D:\\Cache"]),
    /cannot invoke prepared-cache tooling/u);
  assert.throws(
    () => createLaunchInvocation(RUNTIME_COMMAND, [...runtimeArguments, "prepare_legal_assets.py"]),
    /cannot invoke prepared-cache tooling/u);
});

for (const [label, sourceArguments] of [
  ["Fallout 1", ["--fo1-owned-profile", "D:\\Profiles\\fallout1-owned.json"]],
  ["Fallout 2", ["--fo2-owned-profile", "D:\\Profiles\\fallout2-owned.json"]]
]) {
  test(`${label} native invocation rejects Python and legacy prepared inputs`, () => {
    assert.deepEqual(
      createLaunchInvocation(RUNTIME_COMMAND, sourceArguments).arguments,
      sourceArguments);
    assert.throws(
      () => createLaunchInvocation(
        { executable: "C:\\Python313\\python.exe", prefixArguments: [] },
        sourceArguments),
      /cannot invoke prepared-cache tooling/u);
    for (const forbidden of ["--cache-root", "--reuse-cache", "--data-root",
      "--fo1-hex-scene", "--fo1-character-start", "--fo2-temple-cache"]) {
      assert.throws(
        () => createLaunchInvocation(RUNTIME_COMMAND, [...sourceArguments, forbidden, "D:\\Legacy"]),
        /cannot invoke prepared-cache tooling/u);
    }
  });
}

test("the invocation guard does not rewrite legacy non-native evidence routes", () => {
  const legacy = ["--xr-mode", "off", "--", "--cache-root", "D:\\LegacyEvidence"];
  assert.deepEqual(createLaunchInvocation(RUNTIME_COMMAND, legacy).arguments, legacy);
});

test("all four active standalone launch routes remain prepared-cache free", () => {
  const cases = [
    [
      { campaign: "fallout1", presentation: "hex-tactical" },
      { fallout1Profile: {
        ready: true,
        path: "D:\\Profiles\\Fallout1\\owned-profile.json",
        savePath: "D:\\Saves\\Fallout1\\vault-dweller.json"
      } }
    ],
    [
      { campaign: "fallout2", presentation: "hex-tactical" },
      { fallout2Profile: {
        ready: true,
        path: "D:\\Profiles\\Fallout2\\owned-profile.json",
        savePath: "D:\\Saves\\Fallout2\\chosen.json"
      } }
    ],
    [{ campaign: "newvegas" }, nativeProfiles()],
    [
      { campaign: "fallout3" },
      { fallout3Profile: {
        ready: true,
        dataRoot: "D:\\Games\\Fallout 3\\Data",
        stackPath: "D:\\Profiles\\Fallout3\\mod-stack.json",
        stackId: "a".repeat(64),
        stackSha256: "b".repeat(64),
        savePath: "D:\\Saves\\Fallout3\\lone-wanderer.json"
      } }
    ]
  ];
  const forbidden = /(?:cache|prepare|python|\.py$|hex-scene|character-start-cache)/iu;
  for (const [request, profiles] of cases) {
    const runtimeArguments = createRuntimeArguments(validateLaunchRequest(request), profiles);
    const invocation = createLaunchInvocation(RUNTIME_COMMAND, runtimeArguments);
    assert.equal(invocation.arguments.some((value) => forbidden.test(value)), false);
  }
});
