export function createLaunchInvocation(command, runtimeArguments) {
  if (!command || typeof command.executable !== "string" || command.executable.length === 0 ||
      !Array.isArray(command.prefixArguments) || !Array.isArray(runtimeArguments) ||
      [...command.prefixArguments, ...runtimeArguments].some((value) => typeof value !== "string")) {
    throw new Error("The local runtime invocation is invalid.");
  }
  return {
    executable: command.executable,
    arguments: [...command.prefixArguments, ...runtimeArguments]
  };
}
