// Restores the theme before the first paint, like MDU's theme.js: data-theme is the chosen mode (auto, light, dark),
// data-color-scheme the visible one.
(() => {
    const root = document.documentElement;
    try {
        const saved = localStorage.getItem("mdu-theme");
        if (["auto", "light", "dark"].includes(saved)) {
            root.dataset.theme = saved;
        }
    } catch { }
    const system = matchMedia("(prefers-color-scheme: dark)");
    const update = () => {
        root.dataset.colorScheme = root.dataset.theme === "auto" ? (system.matches ? "dark" : "light") : root.dataset.theme;
    };
    new MutationObserver(update).observe(root, { attributes: true, attributeFilter: ["data-theme"] });
    system.addEventListener("change", update);
    update();
})();
