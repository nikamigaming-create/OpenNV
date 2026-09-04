const api = window.openNevada;
if (!api) throw new Error("Open Nevada launcher bridge did not load.");

let state = await api.getState();
const topLevelCampaignIds = state.campaigns
  .filter((campaign) => campaign.id !== "ttw")
  .map((campaign) => campaign.id);
const expectedTopLevelCampaignIds = ["fallout1", "fallout2", "newvegas", "fallout3"];
if (state.schema !== "opennv-launcher-state/v4" ||
    topLevelCampaignIds.length !== expectedTopLevelCampaignIds.length ||
    topLevelCampaignIds.some((id, index) => id !== expectedTopLevelCampaignIds[index])) {
  throw new Error("Open Nevada launcher state is stale; restart the launcher.");
}
let selectedGameId = "fallout1";
let selectedPresentation = "hex-tactical";

const campaignContainer = document.querySelector("#campaigns");
const statusElement = document.querySelector("#runtime-status");
const selectionTitle = document.querySelector("#selection-title");
const selectionDetail = document.querySelector("#selection-detail");
const jamRow = document.querySelector("#jam-toggle-row");
const jamToggle = document.querySelector("#jam-toggle");
const presentationRow = document.querySelector("#presentation-row");
const presentationPicker = document.querySelector("#presentation");
const editionRow = document.querySelector("#edition-row");
const edition = document.querySelector("#edition");
const fo1ProfileButton = document.querySelector("#choose-fo1-profile");
const fo2ProfileButton = document.querySelector("#choose-fo2-profile");
const newVegasDataButton = document.querySelector("#choose-newvegas-data");
const fallout3DataButton = document.querySelector("#choose-fallout3-data");
const ttwProfileButton = document.querySelector("#choose-ttw-profile");
const jamProfileButton = document.querySelector("#choose-jam-profile");
const launchButton = document.querySelector("#launch");
const toast = document.querySelector("#toast");

function selectedRouteId() {
  if (edition.value === "ttw-fo3" || edition.value === "ttw-fnv") return edition.value;
  return selectedGameId;
}

function selectedCampaign() {
  const campaignId = selectedRouteId().startsWith("ttw-") ? "ttw" : selectedGameId;
  return state.campaigns.find((campaign) => campaign.id === campaignId) ?? state.campaigns[0];
}

function selectedGame() {
  return state.campaigns.find((campaign) => campaign.id === selectedGameId) ?? state.campaigns[0];
}

function showToast(message, kind = "info") {
  toast.textContent = message;
  toast.dataset.kind = kind;
  toast.classList.add("show");
  window.clearTimeout(showToast.timer);
  const visibilityMilliseconds = state.desktopLauncher?.toastVisibilityMilliseconds;
  if (Number.isFinite(visibilityMilliseconds)) {
    showToast.timer = window.setTimeout(
      () => toast.classList.remove("show"),
      visibilityMilliseconds
    );
  }
}

function statusLabel(runtime) {
  if (runtime.status === "connected") return "Runtime connected";
  if (runtime.status === "ready") return "Godot runtime ready";
  if (runtime.status === "experimental") return "Experimental Godot runtime";
  if (runtime.status === "development-slice") return "Godot development slice";
  if (runtime.status === "portable-shell") return "Cross-platform shell";
  return "Godot runtime not selected";
}

function escapeHtml(value) {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#39;");
}

function campaignStatus(campaign) {
  if (campaign.ready) return "Ready";
  const profile = state.profiles?.[campaign.id === "newvegas" ? "newVegas" : campaign.id];
  if (profile?.manifestDetected && !profile?.validated) return "Profile changed";
  if (profile?.validated && !profile?.runtimeReady) return "Runtime pending";
  if (profile && !profile?.ready) return "Setup needed";
  return "Runtime pending";
}

function modProfileLabel(kind) {
  const profile = state.profiles?.[kind];
  if (!profile?.manifestDetected) return `Set up ${kind.toUpperCase()}`;
  if (!profile.validated) return `${kind.toUpperCase()} profile changed`;
  if (!profile.runtimeReady) return `${kind.toUpperCase()} registered · runtime pending`;
  return `${kind.toUpperCase()} profile ready`;
}

function renderCampaigns() {
  campaignContainer.innerHTML = state.campaigns
    .filter((campaign) => campaign.id !== "ttw")
    .map((campaign) => `
    <button class="campaign-card route-${campaign.id} ${campaign.id === selectedGameId ? "selected" : ""}" type="button" data-campaign="${campaign.id}">
      <strong class="card-title">${campaign.title}</strong>
      <span class="card-detail">${campaign.launcherSummary}</span>
      <span class="card-footer"><i class="ready-dot ${campaign.ready ? "ready" : ""}"></i>${campaignStatus(campaign)}</span>
    </button>
  `).join("");
  campaignContainer.querySelectorAll("[data-campaign]").forEach((element) => {
    element.addEventListener("click", () => {
      selectedGameId = element.dataset.campaign;
      edition.innerHTML = "";
      jamToggle.checked = false;
      selectedPresentation = selectedGame().defaultPresentation || "flat";
      render();
    });
  });
}

function renderLayers() {
  document.querySelector("#extender-layers").innerHTML = state.extenderLayers.map((layer) => `
    <div class="layer-row"><span class="status-pill ${layer.status}">${layer.status.replace("-", " ")}</span><div><strong>${layer.title}</strong><p>${layer.detail}</p></div></div>
  `).join("");
  document.querySelector("#mods").innerHTML = state.mods.map((mod) => `
    <div class="module-row"><div><strong>${mod.title}</strong><p>${mod.detail}</p></div><span class="module-status">${mod.status.replaceAll("-", " ")}</span></div>
  `).join("");
  const managedState = state.managedLayers?.[selectedGameId];
  const managed = managedState?.layers || [];
  const container = document.querySelector("#managed-mod-layers");
  container.innerHTML = managed.length === 0
    ? `<div class="layer-row"><div><strong>No managed mod layers</strong><p>${escapeHtml(managedState?.message || "Install a local ZIP or add a deployed mod folder.")}</p></div></div>`
    : managed.map((layer, index) => `
      <div class="layer-row">
        <span class="status-pill ${layer.enabled ? "ready" : "not-installed"}">${layer.enabled ? "enabled" : "disabled"}</span>
        <div><strong>${index + 1}. ${escapeHtml(layer.displayName)}</strong><p>${escapeHtml(layer.provider)} · ${layer.plugins} plugins · low-to-high priority</p>
          <button class="quiet-button" type="button" data-layer-action="${layer.enabled ? "disable" : "enable"}" data-layer-id="${escapeHtml(layer.id)}">${layer.enabled ? "Disable" : "Enable"}</button>
          <button class="quiet-button" type="button" data-layer-action="move-up" data-layer-id="${escapeHtml(layer.id)}" ${index === 0 ? "disabled" : ""}>Lower priority</button>
          <button class="quiet-button" type="button" data-layer-action="move-down" data-layer-id="${escapeHtml(layer.id)}" ${index === managed.length - 1 ? "disabled" : ""}>Higher priority</button>
          ${layer.removable ? `<button class="quiet-button" type="button" data-layer-action="uninstall" data-layer-id="${escapeHtml(layer.id)}">Uninstall</button>` : ""}
        </div>
      </div>`).join("");
  container.querySelectorAll("[data-layer-action]").forEach((element) => {
    element.addEventListener("click", async () => {
      const result = await api.manageModLayer({
        game: selectedGameId,
        layerId: element.dataset.layerId,
        action: element.dataset.layerAction
      });
      showToast(result.message, result.ok ? "success" : "warning");
      state = await api.getState();
      render();
    });
  });
}

function render() {
  const campaign = selectedCampaign();
  const game = selectedGame();
  const editionEligible = selectedGameId === "newvegas" || selectedGameId === "fallout3";
  if (editionEligible && edition.options.length === 0) {
    const ttw = state.profiles?.ttw;
    const ttwOpeningId = selectedGameId === "fallout3" ? "ttw-fo3" : "ttw-fnv";
    const ttwOpening = ttw?.openings?.[ttwOpeningId];
    const ttwStatus = !ttw?.manifestDetected
      ? "setup needed"
      : !ttw.validated
        ? "profile changed"
        : !ttwOpening?.interactiveReady
          ? "runtime pending"
          : "ready";
    edition.innerHTML = selectedGameId === "newvegas"
      ? `<option value="newvegas">Original New Vegas</option><option value="ttw-fnv">TTW · New Vegas opening · ${ttwStatus}</option>`
      : `<option value="fallout3">Original Fallout 3</option><option value="ttw-fo3">TTW · Fallout 3 opening · ${ttwStatus}</option>`;
  }
  if (!editionEligible) edition.innerHTML = "";
  editionRow.classList.toggle("hidden", !editionEligible);
  const jamAvailable = Boolean(campaign.jam && campaign.jamReady);
  statusElement.textContent = statusLabel(state.runtime);
  statusElement.dataset.status = state.runtime.status;
  selectionTitle.textContent = campaign.id === "ttw" ? `${game.title} — TTW` : game.title;
  const selectedTtwOpening = state.profiles?.ttw?.openings?.[selectedRouteId()];
  selectionDetail.textContent = campaign.id === "ttw" && !selectedTtwOpening?.interactiveReady
    ? selectedTtwOpening?.blocker || campaign.readiness
    : campaign.ready
    ? campaign.launcherDetail
    : `${campaign.launcherDetail} · ${campaign.readiness}`;
  jamRow.classList.toggle("hidden", !campaign.jam);
  jamToggle.disabled = !jamAvailable;
  const jamProfile = state.profiles?.jam;
  document.querySelector("#jam-label").textContent = jamAvailable
    ? "JAM"
    : !jamProfile?.manifestDetected
      ? "JAM · setup needed"
      : !jamProfile.validated
        ? "JAM · profile changed"
        : !jamProfile.runtimeReady
          ? "JAM · registered, runtime pending"
          : "JAM · runtime update needed";
  if (!jamAvailable) jamToggle.checked = false;
  presentationRow.classList.remove("hidden");
  const available = new Set(campaign.presentations || []);
  if (!campaign.ready) available.clear();
  if (!campaign.openXr || !state.runtime.openXrLaunchable)
    available.delete("openxr");
  if (!available.has(selectedPresentation))
    selectedPresentation = available.has(campaign.defaultPresentation)
      ? campaign.defaultPresentation
      : [...available][0] || campaign.defaultPresentation || "first-person";
  const modes = [
    ["first-person", "FPS"],
    ["hex-tactical", "Hex"],
    ["openxr", "VR"]
  ];
  presentationPicker.innerHTML = modes.map(([id, label]) => `
    <button class="mode-button ${selectedPresentation === id && available.has(id) ? "selected" : ""}" type="button" data-presentation="${id}" ${available.has(id) ? "" : "disabled"}>${label}</button>
  `).join("");
  presentationPicker.querySelectorAll("[data-presentation]").forEach((element) => {
    element.addEventListener("click", () => {
      selectedPresentation = element.dataset.presentation;
      render();
    });
  });
  fo1ProfileButton.classList.toggle("hidden", selectedGameId !== "fallout1");
  fo1ProfileButton.textContent = state.profiles?.fallout1?.ready
    ? "Fallout 1 set up"
    : "Set up Fallout 1";
  fo2ProfileButton.classList.toggle("hidden", selectedGameId !== "fallout2");
  fo2ProfileButton.textContent = state.profiles?.fallout2?.validated
    ? "Fallout 2 installed"
    : "Set up Fallout 2";
  newVegasDataButton.classList.toggle("hidden", selectedGameId !== "newvegas");
  newVegasDataButton.textContent = state.profiles?.newVegas?.ready
    ? "New Vegas set up"
    : "Set up New Vegas";
  fallout3DataButton.classList.toggle("hidden", selectedGameId !== "fallout3");
  fallout3DataButton.textContent = state.profiles?.fallout3?.ready
    ? "Fallout 3 set up"
    : "Set up Fallout 3";
  ttwProfileButton.classList.toggle("hidden", campaign.id !== "ttw");
  ttwProfileButton.textContent = modProfileLabel("ttw");
  ttwProfileButton.title = state.profiles?.ttw?.message || "Choose a local TTW profile manifest.";
  jamProfileButton.classList.toggle("hidden", !campaign.jam);
  jamProfileButton.textContent = modProfileLabel("jam");
  jamProfileButton.title = state.profiles?.jam?.message || "Choose a local JAM profile manifest.";
  const routeLaunchable = Boolean(
    state.runtime.canLaunch && campaign.ready && available.has(selectedPresentation) &&
    (campaign.id !== "ttw" || selectedTtwOpening?.interactiveReady));
  launchButton.disabled = !routeLaunchable;
  launchButton.textContent = routeLaunchable ? `Play ${campaign.title}` : "Not ready";
  launchButton.title = routeLaunchable ? "Launch this path" : (campaign.readiness || state.runtime.label);
  document.querySelector("#platform-label").textContent = `${state.runtime.platform.toUpperCase()} / ${state.runtime.source.toUpperCase()}`;
  renderCampaigns();
  renderLayers();
}

launchButton.addEventListener("click", async () => {
  const result = await api.launch({
    campaign: selectedRouteId(),
    enableJam: jamToggle.checked,
    enableVr: selectedPresentation === "openxr",
    presentation: selectedPresentation
  });
  showToast(result.message, result.ok ? "success" : "warning");
});

edition.addEventListener("change", () => {
  jamToggle.checked = false;
  selectedPresentation = selectedCampaign().defaultPresentation || "first-person";
  render();
});

document.querySelector("#choose-runtime").addEventListener("click", async () => {
  const result = await api.chooseRuntime();
  showToast(result.message, result.ok ? "success" : "warning");
  if (result.ok) {
    state = await api.getState();
    render();
  }
});

fo1ProfileButton.addEventListener("click", async () => {
  const result = await api.chooseFo1Profile();
  showToast(result.message, result.ok ? "success" : "warning");
  state = await api.getState();
  render();
});

fo2ProfileButton.addEventListener("click", async () => {
  const result = await api.chooseFo2Profile();
  showToast(result.message, result.ok ? "success" : "warning");
  state = await api.getState();
  render();
});

newVegasDataButton.addEventListener("click", async () => {
  const result = await api.chooseNewVegasData();
  showToast(result.message, result.ok ? "success" : "warning");
  state = await api.getState();
  render();
});

fallout3DataButton.addEventListener("click", async () => {
  const result = await api.chooseFallout3Data();
  showToast(result.message, result.ok ? "success" : "warning");
  state = await api.getState();
  render();
});

ttwProfileButton.addEventListener("click", async () => {
  const result = await api.chooseTtwProfile();
  showToast(result.message, result.ok ? "success" : "warning");
  state = await api.getState();
  render();
});

jamProfileButton.addEventListener("click", async () => {
  const result = await api.chooseJamProfile();
  showToast(result.message, result.ok ? "success" : "warning");
  state = await api.getState();
  render();
});

document.querySelector("#open-mod-docs").addEventListener("click", async () => {
  try {
    await api.openExternal("https://github.com/nikamigaming-create/OpenNV/blob/main/docs/mods.md");
  } catch {
    showToast("The support policy is available in docs/mods.md in the OpenNV release.", "info");
  }
});

document.querySelector("#add-mod-source-root").addEventListener("click", async () => {
  const result = await api.addModSourceRoot(selectedGameId);
  showToast(result.message, result.ok ? "success" : "warning");
  if (result.ok) {
    state = await api.getState();
    render();
  }
});

document.querySelector("#install-local-mod-archive").addEventListener("click", async () => {
  const result = await api.installLocalModArchive(selectedGameId);
  showToast(result.message, result.ok ? "success" : "warning");
  if (result.ok) {
    state = await api.getState();
    render();
  }
});

render();
