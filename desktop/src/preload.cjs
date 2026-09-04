const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("openNevada", {
  getState: () => ipcRenderer.invoke("opennv:get-state"),
  chooseRuntime: () => ipcRenderer.invoke("opennv:choose-runtime"),
  chooseFo1Profile: () => ipcRenderer.invoke("opennv:choose-fo1-profile"),
  chooseFo2Profile: () => ipcRenderer.invoke("opennv:choose-fo2-profile"),
  chooseNewVegasData: () => ipcRenderer.invoke("opennv:choose-newvegas-data"),
  chooseFallout3Data: () => ipcRenderer.invoke("opennv:choose-fallout3-data"),
  chooseTtwProfile: () => ipcRenderer.invoke("opennv:choose-ttw-profile"),
  chooseJamProfile: () => ipcRenderer.invoke("opennv:choose-jam-profile"),
  addModSourceRoot: (game) => ipcRenderer.invoke("opennv:add-mod-source-root", game),
  installLocalModArchive: (game) => ipcRenderer.invoke("opennv:install-local-mod-archive", game),
  manageModLayer: (request) => ipcRenderer.invoke("opennv:manage-mod-layer", request),
  launch: (request) => ipcRenderer.invoke("opennv:launch", request),
  openExternal: (url) => ipcRenderer.invoke("opennv:open-external", url)
});
