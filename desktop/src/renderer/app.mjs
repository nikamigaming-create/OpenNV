const api = window.openNevada;
if (!api) throw new Error("Open Nevada launcher bridge did not load.");

let state = await api.getState();
let selectedGameId = "fallout1";
let selectedPresentation = "hex-tactical";

const campaignContainer = document.querySelector("#campaigns");
const statusElement = document.querySelector("#runtime-status");
const selectionTitle = document.querySelector("#selection-title");
const selectionDetail = document.querySelector("#selection-detail");
const jamRow = document.querySelector("#jam-toggle-row");
const jamToggle = document.querySelector("#jam-toggle");
const vrRow = document.querySelector("#vr-toggle-row");
const vrToggle = document.querySelector("#vr-toggle");
const classicPresentationRow = document.querySelector("#classic-presentation-row");
const classicPresentation = document.querySelector("#classic-presentation");
const editionRow = document.querySelector("#edition-row");
const edition = document.querySelector("#edition");
const fo1ProfileButton = document.querySelector("#choose-fo1-profile");
const fo2ProfileButton = document.querySelector("#choose-fo2-profile");
const ttwProfileButton = document.querySelector("#choose-ttw-profile");
const jamProfileButton = document.querySelector("#choose-jam-profile");
const launchButton = document.querySelector("#launch");
const toast = document.querySelector("#toast");

function selectedCampaign() {
  const routeId = edition.value === "ttw" ? "ttw" : selectedGameId;
  return state.campaigns.find((campaign) => campaign.id === routeId) ?? state.campaigns[0];
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
      vrToggle.checked = false;
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
}

function render() {
  const campaign = selectedCampaign();
  const game = selectedGame();
  const classicSelected = selectedGameId === "fallout1" || selectedGameId === "fallout2";
  const editionEligible = selectedGameId === "newvegas" || selectedGameId === "fallout3";
  if (editionEligible && edition.options.length === 0) {
    const ttw = state.profiles?.ttw;
    const ttwRoute = state.campaigns.find((candidate) => candidate.id === "ttw");
    const ttwStatus = !ttw?.manifestDetected
      ? "setup needed"
      : !ttw.validated
        ? "profile changed"
        : !ttw.runtimeReady
          ? "runtime pending"
          : ttwRoute?.ready
            ? "ready"
            : "runtime update needed";
    edition.innerHTML = selectedGameId === "newvegas"
      ? `<option value="newvegas">Original New Vegas</option><option value="ttw">TTW · ${ttwStatus}</option>`
      : `<option value="fallout3">Original Fallout 3</option><option value="ttw">TTW · ${ttwStatus}</option>`;
  }
  if (!editionEligible) edition.innerHTML = "";
  editionRow.classList.toggle("hidden", !editionEligible);
  const jamAvailable = Boolean(campaign.jam && campaign.jamReady);
  const vrAvailable = Boolean(campaign.openXr && campaign.ready && state.runtime.openXrLaunchable);
  statusElement.textContent = statusLabel(state.runtime);
  statusElement.dataset.status = state.runtime.status;
  selectionTitle.textContent = campaign.id === "ttw" ? `${game.title} — TTW` : game.title;
  selectionDetail.textContent = campaign.ready
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
  vrRow.classList.toggle("hidden", classicSelected);
  vrToggle.disabled = !vrAvailable;
  document.querySelector("#vr-label").textContent = vrAvailable ? "VR" : "VR · in progress";
  if (!vrAvailable) vrToggle.checked = false;
  classicPresentationRow.classList.toggle("hidden", !classicSelected);
  if (classicSelected) {
    const available = new Set(game.presentations || []);
    const modes = [
      ["hex-tactical", "Hex"],
      ["first-person", "FPS"],
      ["openxr", "VR"]
    ];
    classicPresentation.innerHTML = modes.map(([id, label]) => `
      <button class="mode-button ${selectedPresentation === id && available.has(id) ? "selected" : ""}" type="button" data-presentation="${id}" ${available.has(id) ? "" : "disabled"}>${label}</button>
    `).join("");
    classicPresentation.querySelectorAll("[data-presentation]").forEach((element) => {
      element.addEventListener("click", () => {
        selectedPresentation = element.dataset.presentation;
        render();
      });
    });
  }
  fo1ProfileButton.classList.toggle("hidden", selectedGameId !== "fallout1");
  fo1ProfileButton.textContent = state.profiles?.fallout1?.ready
    ? "Fallout 1 set up"
    : "Set up Fallout 1";
  fo2ProfileButton.classList.toggle("hidden", selectedGameId !== "fallout2");
  fo2ProfileButton.textContent = state.profiles?.fallout2?.validated
    ? "Fallout 2 installed"
    : "Set up Fallout 2";
  ttwProfileButton.classList.toggle("hidden", campaign.id !== "ttw");
  ttwProfileButton.textContent = modProfileLabel("ttw");
  ttwProfileButton.title = state.profiles?.ttw?.message || "Choose a local TTW profile manifest.";
  jamProfileButton.classList.toggle("hidden", !campaign.jam);
  jamProfileButton.textContent = modProfileLabel("jam");
  jamProfileButton.title = state.profiles?.jam?.message || "Choose a local JAM profile manifest.";
  const routeLaunchable = Boolean(state.runtime.canLaunch && campaign.ready);
  launchButton.disabled = !routeLaunchable;
  launchButton.textContent = routeLaunchable ? `Play ${campaign.title}` : "Not ready";
  launchButton.title = routeLaunchable ? "Launch this path" : (campaign.readiness || state.runtime.label);
  document.querySelector("#platform-label").textContent = `${state.runtime.platform.toUpperCase()} / ${state.runtime.source.toUpperCase()}`;
  renderCampaigns();
  renderLayers();
}

launchButton.addEventListener("click", async () => {
  const classicSelected = selectedGameId === "fallout1" || selectedGameId === "fallout2";
  const result = await api.launch({
    campaign: selectedCampaign().id,
    enableJam: jamToggle.checked,
    enableVr: vrToggle.checked,
    presentation: classicSelected ? selectedPresentation : "flat"
  });
  showToast(result.message, result.ok ? "success" : "warning");
});

edition.addEventListener("change", () => {
  jamToggle.checked = false;
  vrToggle.checked = false;
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

render();
