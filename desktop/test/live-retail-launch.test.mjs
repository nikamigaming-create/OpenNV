import assert from "node:assert/strict";
import test from "node:test";
import { createRuntimeArguments, validateLaunchRequest } from "../src/contract.mjs";
import { createLaunchInvocation } from "../src/native-launch-contract.mjs";

const command = { executable: "C:\\Godot\\godot.exe", prefixArguments: [] };

test("New Vegas launches from the selected live Data folder only", () => {
  const args = createRuntimeArguments(
    validateLaunchRequest({ campaign: "newvegas" }),
    {
      newVegasProfile: {
        ready: true,
        dataRoot: "D:\\Games\\Fallout New Vegas\\Data",
        savePath: "D:\\Saves\\courier.json"
      }
    });
  assert.deepEqual(args, [
    "--xr-mode", "off", "--",
    "--data-root", "D:\\Games\\Fallout New Vegas\\Data",
    "--campaign", "fallout-new-vegas",
    "--opening-menu",
    "--save-path", "D:\\Saves\\courier.json"
  ]);
  assert.equal(args.includes("--source-stack"), false);
});

test("all standalone launch contracts pass a live installation root", () => {
  const cases = [
    [
      { campaign: "fallout1", presentation: "hex-tactical" },
      { fallout1Profile: { ready: true, dataRoot: "D:\\Games\\Fallout", savePath: "D:\\Saves\\fo1.json" } }
    ],
    [
      { campaign: "fallout2", presentation: "hex-tactical" },
      { fallout2Profile: { ready: true, dataRoot: "D:\\Games\\Fallout 2", savePath: "D:\\Saves\\fo2.json" } }
    ],
    [
      { campaign: "fallout3" },
      { fallout3Profile: { ready: true, dataRoot: "D:\\Games\\Fallout 3\\Data", savePath: "D:\\Saves\\fo3.json" } }
    ],
    [
      { campaign: "newvegas" },
      { newVegasProfile: { ready: true, dataRoot: "D:\\Games\\Fallout New Vegas\\Data", savePath: "D:\\Saves\\fnv.json" } }
    ]
  ];
  for (const [request, profiles] of cases) {
    const args = createRuntimeArguments(validateLaunchRequest(request), profiles);
    assert.equal(args.includes("--data-root"), true);
    assert.equal(args.includes("--source-stack"), false);
  }
});
