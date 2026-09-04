import path from "node:path";

const NATIVE_SOURCE_ARGUMENTS = new Set([
  "--source-stack",
  "--fo1-owned-profile",
  "--fo2-owned-profile"
]);
const FORBIDDEN_NATIVE_ARGUMENTS = new Set([
  "--source-root",
  "--mod-stack",
  "--mod-stack-sha256",
  "--mod-stack-id",
  "--cache-root",
  "--reuse-cache",
  "--prepare-cache",
  "--data-root",
  "--fo1-hex-scene",
  "--fo1-character-start",
  "--fo1-character-start-sha256",
  "--fo2-temple-cache",
  "--fo2-character-start-cache"
]);

function isPythonExecutable(executable) {
  const name = path.basename(executable).toLowerCase();
  return /^(?:python(?:\d+(?:\.\d+)*)?|py)(?:\.exe)?$/u.test(name);
}

export function createLaunchInvocation(command, runtimeArguments) {
  if (!command || typeof command.executable !== "string" || command.executable.length === 0 ||
      !Array.isArray(command.prefixArguments) || !Array.isArray(runtimeArguments) ||
      [...command.prefixArguments, ...runtimeArguments].some((value) => typeof value !== "string")) {
    throw new Error("The local runtime invocation is invalid.");
  }
  const args = [...command.prefixArguments, ...runtimeArguments];
  if (args.some((value) => NATIVE_SOURCE_ARGUMENTS.has(value))) {
    const forbidden = args.find((value) =>
      FORBIDDEN_NATIVE_ARGUMENTS.has(value) ||
      value.toLowerCase().endsWith(".py") ||
      value.toLowerCase().includes("prepare_legal_assets"));
    if (forbidden || isPythonExecutable(command.executable)) {
      throw new Error(
        `The native owned-data route cannot invoke prepared-cache tooling: ${forbidden || command.executable}`);
    }
  }
  return { executable: command.executable, arguments: args };
}
