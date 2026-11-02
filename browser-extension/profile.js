const api = globalThis.browser || globalThis.chrome;
api.storage.local.get(["profileId", "profileName"]).then(value => {
  document.querySelector("#id").value = value.profileId || "Connecting…";
  document.querySelector("#name").value = value.profileName || "";
});
document.querySelector("#save").addEventListener("click", async () => {
  await api.storage.local.set({ profileName: document.querySelector("#name").value.trim().slice(0, 80) || "Browser" });
  document.querySelector("#status").textContent = "Saved. The name is updated on the next connection. / Gespeichert. Der Name erscheint nach der nächsten Verbindung.";
});
