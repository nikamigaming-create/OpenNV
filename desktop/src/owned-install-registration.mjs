import path from "node:path";

// Both persisted registration versions describe a live Data folder. Neither
// selects an old renderer, imports a scene cache, or changes campaign identity.
export function registeredOwnedDataRoot(registration, campaign) {
  if (!["opennv-live-install-registration/v1", "opennv-launcher-owned-data-registration/v1"].includes(registration?.schema) ||
      registration?.campaign !== campaign || typeof registration?.dataRoot !== "string" ||
      !registration.dataRoot.trim() || !path.isAbsolute(registration.dataRoot)) return null;
  return path.resolve(registration.dataRoot);
}
