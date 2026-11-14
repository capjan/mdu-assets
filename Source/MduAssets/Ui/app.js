// MDU Asset Server web interface. Plain JavaScript on the JSON API, no build step (ADR-0007).
// This module runs the project page and picks the page to show; the other pages live in pages.js.
import {
    $, api, announce, copyText, element, flashCopied, formatSize, numberFormat, dateFormat, plural, readSetting, request, writeSetting,
} from "./common.js";
import { showAccount, showAdmin, showNewProject, showSettings } from "./pages.js";

const params = new URLSearchParams(location.search);
const state = {
    me: null,
    page: params.get("page") || "project",
    project: params.get("project"),
    projects: [],
    assets: [],
    next: null,
    view: readSetting("mdu-assets-view", "grid"),
    sort: { key: "createdAt", direction: "descending" },
    format: readSetting("mdu-assets-snippet-format", "markdown"),
    cards: new Map(),
};

const formatDimensions = (asset) => asset.width && asset.height ? `${asset.width} × ${asset.height}` : "–";

/** Editors and owners change images; viewers only look and copy. */
const canEdit = () => ["editor", "owner"].includes(currentProject()?.role);

// Theme switch, cycling auto → light → dark like MDU.
const themes = ["auto", "light", "dark"];
const themeNames = { auto: "Auto", light: "Light", dark: "Dark" };
const themeToggle = $("theme-toggle");

function updateThemeLabel() {
    const current = document.documentElement.dataset.theme;
    const next = themes[(themes.indexOf(current) + 1) % themes.length];
    themeToggle.title = `Theme: ${themeNames[current]} (click for ${themeNames[next]})`;
    themeToggle.setAttribute("aria-label", `Theme: ${themeNames[current]}. Click to switch to ${themeNames[next]}.`);
}

themeToggle.addEventListener("click", () => {
    const root = document.documentElement;
    const next = themes[(themes.indexOf(root.dataset.theme) + 1) % themes.length];
    root.dataset.theme = next;
    writeSetting("mdu-theme", next);
    updateThemeLabel();
});
updateThemeLabel();

// Pages: ?project=<slug> shows images, &page=settings its settings; ?page=new-project, account or admin.
function showPage(name) {
    for (const page of document.querySelectorAll("[id^=page-]")) page.hidden = page.id !== `page-${name}`;
    $("account-link").setAttribute("aria-current", name === "account" ? "page" : "false");
    $("admin-link").setAttribute("aria-current", name === "admin" ? "page" : "false");
}

async function load() {
    const me = await request("/me");
    state.me = me;
    $("user-name").textContent = me.name;
    $("admin-link").hidden = !me.isAdmin;
    $("new-project-link").hidden = !me.canCreateProjects;

    // A start password blocks everything else until it is changed (AUTH-05).
    if (me.mustChangePassword) {
        showPage("account");
        showAccount(me, { forced: true });
        return;
    }

    state.projects = (await request("/projects")).items;
    const project = state.projects.find((p) => p.slug === state.project) ?? (state.project ? null : state.projects[0]);
    state.project = project?.slug ?? state.project;
    renderProjects();

    switch (state.page) {
        case "account":
            showPage("account");
            showAccount(me);
            return;
        case "admin":
            if (me.isAdmin) {
                showPage("admin");
                await showAdmin(me);
                return;
            }
            break;
        case "new-project":
            if (me.canCreateProjects) {
                showPage("new-project");
                showNewProject();
                return;
            }
            break;
        case "settings":
            if (project?.role === "owner") {
                showPage("settings");
                await showSettings(project, me);
                return;
            }
            break;
    }

    showPage("project");
    if (!project) {
        $("project-title").textContent = state.project ? "Project not found" : "No projects yet";
        document.title = "MDU Assets";
        $("project-toolbar").hidden = true;
        $("empty-state").hidden = false;
        $("empty-state").textContent = state.project
            ? `There is no project '${state.project}', or you are not a member of it.`
            : me.canCreateProjects ? "Create a project to start uploading images." : "Ask an admin or a project owner to add you to a project.";
        return;
    }
    await loadAssets();
}

// The list comes in pages, newest first; "Show more images" loads the next one.
const assetsPath = (after) => `/projects/${encodeURIComponent(state.project)}/assets?limit=200${after ? `&after=${encodeURIComponent(after)}` : ""}`;

async function loadAssets() {
    const list = await request(assetsPath());
    state.assets = list.items;
    state.next = list.next;
    renderAssets();
}

$("load-more").addEventListener("click", async () => {
    const button = $("load-more");
    button.disabled = true;
    try {
        const list = await request(assetsPath(state.next));
        const known = new Set(state.assets.map((a) => a.id));
        state.assets.push(...list.items.filter((a) => !known.has(a.id)));
        state.next = list.next;
        renderAssets();
        announce(`Showing ${numberFormat.format(state.assets.length)} images.`);
    } catch (error) {
        announce(error.message);
    } finally {
        button.disabled = false;
    }
});

function currentProject() {
    return state.projects.find((p) => p.slug === state.project);
}

function renderProjects() {
    const project = currentProject();
    const name = project?.name ?? state.project ?? "";
    $("project-title").textContent = name;
    $("drop-target").textContent = name;
    document.title = name ? `${name} – MDU Assets` : "MDU Assets";
    // Counts come from the project, so they include images that are not loaded yet.
    $("project-summary").textContent = !project || project.assetCount === 0 ? ""
        : `${plural(project.assetCount, "image", "images")} · ${formatSize(project.totalSize)}`;
    $("project-description").textContent = project?.description ?? "";
    $("project-description").hidden = !project?.description;

    const editable = canEdit();
    $("upload-button").hidden = !editable;
    $("toolbar-hint").hidden = !editable;
    $("viewer-notice").hidden = !project || editable;
    $("empty-state").textContent = editable ? "No images yet. Drop, paste or upload one." : "No images yet.";
    const settings = $("settings-link");
    settings.hidden = project?.role !== "owner";
    settings.href = project ? `/?project=${encodeURIComponent(project.slug)}&page=settings` : "#";

    $("project-list").replaceChildren(...state.projects.map((p) =>
        element("li", {}, [element("a", {
            class: "project-link",
            href: `/?project=${encodeURIComponent(p.slug)}`,
            "aria-label": `${p.name}, ${plural(p.assetCount, "image", "images")}`,
            "aria-current": p.slug === state.project && ["project", "settings"].includes(state.page) ? "page" : null,
        }, [element("span", { text: p.name }), element("span", { class: "project-count", text: numberFormat.format(p.assetCount) })])])));
}

function sortedAssets() {
    const { key, direction } = state.sort;
    const value = (asset) => key === "pixels" ? (asset.width ?? 0) * (asset.height ?? 0)
        : key === "fileName" ? asset.fileName.toLowerCase() : asset[key];
    const sorted = [...state.assets].sort((a, b) => value(a) < value(b) ? -1 : value(a) > value(b) ? 1 : 0);
    return direction === "descending" ? sorted.reverse() : sorted;
}

function renderAssets() {
    const assets = sortedAssets();
    $("empty-state").hidden = assets.length > 0;
    $("load-more").hidden = !state.next;

    const grid = state.view === "grid";
    $("asset-grid").hidden = !grid;
    $("asset-table-wrapper").hidden = grid || assets.length === 0;
    $("view-grid").setAttribute("aria-pressed", String(grid));
    $("view-list").setAttribute("aria-pressed", String(!grid));

    if (grid) {
        $("asset-grid").replaceChildren(...assets.map((asset) => element("li", {}, [element("button", {
            class: "asset-card",
            type: "button",
            "aria-label": `${asset.fileName}, ${formatDimensions(asset)}, ${formatSize(asset.size)}. Show snippets.`,
            onclick: () => showSnippet(asset),
        }, [
            element("span", { class: "asset-thumb" }, [element("img", { src: asset.url, alt: "", loading: "lazy", decoding: "async" })]),
            element("span", { class: "asset-meta" }, [
                element("span", { class: "asset-name", text: asset.fileName, title: asset.fileName }),
                element("span", { class: "asset-detail", text: `${formatDimensions(asset)} · ${formatSize(asset.size)}` }),
            ]),
        ])])));
    } else {
        for (const header of document.querySelectorAll("#asset-table th")) {
            const key = header.querySelector("button").dataset.sort;
            header.setAttribute("aria-sort", key === state.sort.key ? state.sort.direction : "none");
        }
        $("asset-rows").replaceChildren(...assets.map((asset) => element("tr", {}, [
            element("td", {}, [element("button", { class: "row-button", type: "button", onclick: () => showSnippet(asset) }, [
                element("img", { src: asset.url, alt: "", loading: "lazy", decoding: "async" }),
                element("span", { text: asset.fileName }),
            ])]),
            element("td", { text: formatDimensions(asset) }),
            element("td", { text: formatSize(asset.size) }),
            element("td", { text: dateFormat.format(new Date(asset.createdAt)) }),
        ])));
    }
}

$("view-grid").addEventListener("click", () => { state.view = "grid"; writeSetting("mdu-assets-view", "grid"); renderAssets(); });
$("view-list").addEventListener("click", () => { state.view = "list"; writeSetting("mdu-assets-view", "list"); renderAssets(); });

for (const button of document.querySelectorAll("#asset-table th button")) {
    button.addEventListener("click", () => {
        const key = button.dataset.sort;
        state.sort = state.sort.key === key
            ? { key, direction: state.sort.direction === "ascending" ? "descending" : "ascending" }
            : { key, direction: key === "fileName" ? "ascending" : "descending" };
        renderAssets();
        document.querySelector(`#asset-table th button[data-sort="${key}"]`).focus();
    });
}

// Snippets
const formats = {
    markdown: { label: "Markdown", text: (asset) => asset.markdown },
    url: { label: "URL", text: (asset) => asset.url },
    html: { label: "HTML", text: (asset) => asset.html },
};

function snippetText(asset) {
    return (formats[state.format] ?? formats.markdown).text(asset);
}

const copyIcon = () => {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 20 20");
    svg.setAttribute("aria-hidden", "true");
    svg.setAttribute("focusable", "false");
    svg.innerHTML = '<rect x="7" y="7" width="10" height="10" rx="2"></rect><path d="M13 7V4.5A1.5 1.5 0 0 0 11.5 3h-7A1.5 1.5 0 0 0 3 4.5v7A1.5 1.5 0 0 0 4.5 13H7"></path>';
    return svg;
};

/** A card for one upload or selected image; returns an object to update it while the upload runs. */
function createCard(title) {
    const preview = element("span", { class: "snippet-preview" });
    const name = element("strong", { text: title });
    const stateText = element("span", { class: "snippet-state" });
    const progress = element("progress", { max: "100", value: "0", "aria-label": `Upload of ${title}` });
    const body = element("div", { class: "snippet-body" }, [element("div", { class: "snippet-title" }, [name, stateText]), progress]);
    const item = element("li", { class: "snippet-card" }, [preview, body]);
    $("snippet-list").prepend(item);
    $("snippets").hidden = false;

    const card = { item, asset: null, uploading: true };
    item.card = card;
    card.progress = (percent) => {
        progress.value = percent;
        stateText.textContent = `Uploading ${percent} %`;
    };
    card.fail = (message) => {
        card.uploading = false;
        progress.remove();
        stateText.textContent = message;
        stateText.classList.add("is-error");
    };
    card.fill = (asset) => {
        card.asset = asset;
        card.uploading = false;
        // A card cleared away while its upload ran stays gone.
        if (!card.item.isConnected) return;
        state.cards.set(asset.id, card);
        progress.remove();
        stateText.textContent = `${formatDimensions(asset)} · ${formatSize(asset.size)}`;
        stateText.classList.remove("is-error");
        name.textContent = asset.fileName;
        preview.replaceChildren(element("img", { src: asset.url, alt: "" }));

        const readOnly = !canEdit();
        const fileName = element("input", { type: "text", value: asset.fileName, spellcheck: "false", autocomplete: "off", readonly: readOnly });
        const altText = element("input", { type: "text", value: asset.altText, autocomplete: "off", readonly: readOnly });
        // Enter saves, and leaving the field afterwards fires "change" too. Saves of a card run one after another,
        // so the second one finds nothing changed and sends no request.
        const save = () => (card.saving = (card.saving ?? Promise.resolve()).then(saveChanges));
        const saveChanges = async () => {
            const changes = {};
            if (fileName.value.trim() !== card.asset.fileName) changes.fileName = fileName.value.trim();
            if (altText.value.trim() !== card.asset.altText) changes.altText = altText.value.trim();
            if (Object.keys(changes).length === 0) return;
            // The card is rebuilt after saving; keyboard users stay in the field they were in.
            const field = document.activeElement === fileName ? 0 : document.activeElement === altText ? 1 : -1;
            try {
                const updated = await request(`/assets/${card.asset.id}`, {
                    method: "PATCH",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(changes),
                });
                card.fill(updated);
                replaceAsset(updated);
                if (field >= 0) card.item.querySelectorAll(".field input")[field]?.focus();
                announce(`Saved ${updated.fileName}.`);
            } catch (error) {
                stateText.textContent = error.message;
                stateText.classList.add("is-error");
                announce(error.message);
            }
        };
        for (const input of readOnly ? [] : [fileName, altText]) {
            input.addEventListener("change", save);
            input.addEventListener("keydown", (event) => { if (event.key === "Enter") { event.preventDefault(); save(); } });
        }

        const format = element("select", { class: "snippet-format", "aria-label": "Snippet format" },
            Object.entries(formats).map(([key, value]) => element("option", { value: key, selected: key === state.format, text: value.label })));
        const code = element("code", { class: "snippet-code", tabindex: "0", text: snippetText(asset) });
        const copy = element("button", { class: "copy-button", type: "button" }, [copyIcon(), element("span", { text: "Copy" })]);
        copy.setAttribute("aria-label", `Copy snippet of ${asset.fileName}`);
        copy.addEventListener("click", async () => {
            const ok = await copyText(snippetText(card.asset));
            flashCopied(copy, ok);
            announce(ok ? "Copied to the clipboard." : "Copying failed. Select the text and copy it.");
        });
        format.addEventListener("change", () => {
            state.format = format.value;
            writeSetting("mdu-assets-snippet-format", state.format);
            for (const other of state.cards.values()) other.refresh?.();
        });
        card.refresh = () => {
            format.value = state.format;
            code.textContent = snippetText(card.asset);
        };
        card.copyButton = copy;

        body.replaceChildren(
            element("div", { class: "snippet-title" }, [name, stateText]),
            element("div", { class: "snippet-fields" }, [
                element("label", { class: "field" }, [element("span", { text: "File name" }), fileName]),
                element("label", { class: "field" }, [element("span", { text: "Alt text" }), altText]),
            ]),
            element("div", { class: "snippet-output" }, [format, code, copy]),
        );
        updateCopyAll();
    };
    return card;
}

function showSnippet(asset) {
    const existing = state.cards.get(asset.id);
    if (existing) {
        $("snippet-list").prepend(existing.item);
        existing.copyButton?.focus();
    } else {
        const card = createCard(asset.fileName);
        card.fill(asset);
        card.copyButton.focus();
    }
    $("snippets").scrollIntoView({ block: "nearest" });
}

function replaceAsset(asset) {
    const index = state.assets.findIndex((a) => a.id === asset.id);
    if (index >= 0) state.assets[index] = asset; else state.assets.unshift(asset);
    renderAssets();
}

function updateCopyAll() {
    $("copy-all").hidden = state.cards.size < 2;
}

$("copy-all").addEventListener("click", async () => {
    const cards = [...$("snippet-list").children].map((item) => item.card).filter((card) => card?.asset && state.cards.has(card.asset.id));
    const ok = await copyText(cards.map((card) => snippetText(card.asset)).join("\n"));
    announce(ok ? `Copied ${cards.length} snippets.` : "Copying failed.");
});

// Clear keeps cards whose upload is still running; their snippets appear when the upload finishes.
$("clear-snippets").addEventListener("click", () => {
    state.cards.clear();
    for (const item of [...$("snippet-list").children]) if (!item.card?.uploading) item.remove();
    $("snippets").hidden = $("snippet-list").children.length === 0;
    updateCopyAll();
    $("upload-button").focus();
});

// Uploads
const accepted = /^image\/(png|jpeg|gif|webp|avif|bmp|svg\+xml)$/;

function pastedName(file) {
    // Screenshots from the clipboard arrive as "image.png"; give them a name that says when they were taken.
    const now = new Date();
    const pad = (n) => String(n).padStart(2, "0");
    const stamp = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
    const extension = file.type.split("/")[1]?.replace("svg+xml", "svg").replace("jpeg", "jpg") || "png";
    return `screenshot-${stamp}.${extension}`;
}

function upload(file, name, alt) {
    const card = createCard(name);
    return new Promise((resolve) => {
        const xhr = new XMLHttpRequest();
        xhr.open("POST", `${api}/projects/${encodeURIComponent(state.project)}/assets?name=${encodeURIComponent(name)}${alt ? `&alt=${encodeURIComponent(alt)}` : ""}`);
        xhr.setRequestHeader("Content-Type", file.type || "application/octet-stream");
        xhr.setRequestHeader("Accept", "application/json");
        xhr.responseType = "json";
        xhr.upload.addEventListener("progress", (event) => {
            if (event.lengthComputable) card.progress(Math.round((event.loaded / event.total) * 100));
        });
        xhr.addEventListener("load", () => {
            if (xhr.status === 201) {
                card.fill(xhr.response);
                replaceAsset(xhr.response);
                resolve(xhr.response);
            } else {
                card.fail(xhr.response?.detail || xhr.response?.title || `Upload failed (${xhr.status}).`);
                resolve(null);
            }
        });
        xhr.addEventListener("error", () => { card.fail("Upload failed: the server could not be reached."); resolve(null); });
        xhr.send(file);
    });
}

async function uploadFiles(files, { pasted = false } = {}) {
    // Only on the images page, and only for editors and owners; the server checks the role again.
    if ($("page-project").hidden) return;
    if (!canEdit()) {
        announce("Uploading needs the editor role in this project. Ask an owner for it.");
        $("viewer-notice").scrollIntoView({ block: "nearest" });
        return;
    }
    const images = [...files].filter((file) => accepted.test(file.type) || /\.svg$/i.test(file.name));
    const skipped = files.length - images.length;
    if (images.length === 0) {
        announce(skipped > 0 ? "Only images can be uploaded." : "");
        return;
    }

    announce(`Uploading ${images.length} ${images.length === 1 ? "image" : "images"}…`);
    const results = (await Promise.all(images.map((file) =>
        pasted || !file.name
            ? upload(file, pastedName(file), "Screenshot")
            : upload(file, file.name)))).filter(Boolean);

    if (results.length === 0) {
        announce("The upload failed.");
        return;
    }
    // Copy right away where the browser allows it; otherwise the Copy button is one click away.
    const copied = await copyText(results.map(snippetText).join("\n"));
    const names = results.length === 1 ? results[0].fileName : `${results.length} images`;
    announce(copied ? `Uploaded ${names}. The snippet is on the clipboard.` : `Uploaded ${names}. Press Copy to copy the snippet.`);
    const buttons = results.map((asset) => state.cards.get(asset.id)?.copyButton).filter(Boolean);
    if (copied) for (const button of buttons) flashCopied(button, true);
    buttons[0]?.focus({ preventScroll: true });
    refreshProjectCounts();
}

async function refreshProjectCounts() {
    try {
        state.projects = (await request("/projects")).items;
        renderProjects();
    } catch { }
}

$("upload-button").addEventListener("click", () => $("file-input").click());
$("file-input").addEventListener("change", (event) => {
    uploadFiles(event.target.files);
    event.target.value = "";
});

document.addEventListener("paste", (event) => {
    // Text pasted into a field stays text.
    if (event.target.closest?.("input, textarea, [contenteditable]")) return;
    const files = [...(event.clipboardData?.files ?? [])];
    if (files.length === 0) return;
    event.preventDefault();
    uploadFiles(files, { pasted: true });
});

const dropZone = $("drop-zone");
let dragDepth = 0;
const hasFiles = (event) => [...(event.dataTransfer?.types ?? [])].includes("Files");

document.addEventListener("dragenter", (event) => {
    if (!hasFiles(event) || $("page-project").hidden || !canEdit()) return;
    event.preventDefault();
    dragDepth++;
    dropZone.hidden = false;
});
document.addEventListener("dragover", (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
});
document.addEventListener("dragleave", (event) => {
    if (!hasFiles(event)) return;
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) dropZone.hidden = true;
});
document.addEventListener("drop", (event) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    dragDepth = 0;
    dropZone.hidden = true;
    uploadFiles(event.dataTransfer.files);
});

load().catch((error) => {
    showPage("project");
    $("project-toolbar").hidden = true;
    $("empty-state").hidden = false;
    $("empty-state").textContent = `Could not load the page: ${error.message}`;
});
