// MDU Asset Server web interface. Plain JavaScript on the JSON API, no build step (ADR-0007).
// This module runs the project page and picks the page to show; the other pages live in pages.js.
import {
    $, api, announce, copyText, element, flashCopied, folderOptions, folderPath, folderSubtree, folderTree, formatSize, numberFormat,
    dateFormat, openDialog, plural, readSetting, request, send, writeSetting,
} from "./common.js";
import { showDetails } from "./details.js";
import { showAccount, showAdmin, showNewProject, showSettings } from "./pages.js";

const params = new URLSearchParams(location.search);
const state = {
    me: null,
    page: params.get("page") || "project",
    project: params.get("project"),
    asset: params.get("asset"),
    // "none" for images in no folder, a folder id, or null for all images of the project.
    folder: params.get("folder"),
    filters: {
        q: params.get("q") ?? "",
        tag: params.get("tag") ?? "",
        type: params.get("type") ?? "",
        uploader: params.get("uploader") ?? "",
        from: params.get("from") ?? "",
        to: params.get("to") ?? "",
        subfolders: params.get("subfolders") === "true",
    },
    folders: [],
    selected: new Set(),
    focusId: null,
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

    if (project && state.asset) {
        showPage("asset");
        await loadFolders();
        await showDetails(project, state.asset, { editable: canEdit(), onFolderChange: loadFolders });
        return;
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
        $("folder-bar").hidden = true;
        $("search-form").hidden = true;
        return;
    }
    await Promise.all([loadFolders(), loadFacets()]);
    renderFilters();
    await loadAssets();
}

// Folders (ORG-01, ORG-02): a tree in the sidebar, the path above the images, actions for editors.
const projectPath = () => `/projects/${encodeURIComponent(state.project)}`;
const folderId = () => (state.folder && state.folder !== "none" ? Number(state.folder) : null);

/** A link to the project page that keeps the current search and filters. */
function pageLink({ folder = state.folder, filters = state.filters } = {}) {
    const query = new URLSearchParams({ project: state.project });
    if (folder) query.set("folder", folder);
    for (const [key, value] of Object.entries(filters))
        if (value && !(key === "subfolders" && !folder)) query.set(key, String(value));
    return `/?${query}`;
}

async function loadFolders() {
    state.folders = (await request(`${projectPath()}/folders`)).items;
    if (folderId() && !state.folders.some((f) => f.id === folderId())) state.folder = null;
    renderFolders();
}

function renderFolders() {
    const editable = canEdit();
    const link = (folder, label, count, depth = 0) => {
        const value = folder === null ? null : String(folder);
        const current = (state.folder ?? null) === value;
        const anchor = element("a", {
            class: "folder-link",
            href: pageLink({ folder: value }),
            "aria-current": current && !state.asset ? "page" : null,
        }, [element("span", { class: "folder-name", text: label }), ...(count === null ? [] : [element("span", { class: "project-count", text: numberFormat.format(count) })])]);
        // Set through the CSS object model: the content security policy blocks style attributes.
        anchor.style.setProperty("--depth", String(depth));
        if (editable && value !== null) makeDropTarget(anchor, value === "none" ? null : Number(value), label);
        return element("li", {}, [anchor]);
    };
    $("folder-tree").replaceChildren(
        link(null, "All images", null),
        ...folderTree(state.folders).map((f) => link(f.id, f.name, f.assetCount, f.depth + 1)),
        link("none", "Not in a folder", null));
    $("folder-nav").hidden = false;

    // The path: project, then each folder down to the current one.
    const id = folderId();
    const crumbs = [element("a", { href: pageLink({ folder: null }), text: "All images" })];
    if (state.folder === "none") crumbs.push(element("span", { "aria-current": "page", text: "Not in a folder" }));
    for (const folder of id ? folderPath(state.folders, id) : []) {
        crumbs.push(folder.id === id
            ? element("span", { "aria-current": "page", text: folder.name })
            : element("a", { href: pageLink({ folder: String(folder.id) }), text: folder.name }));
    }
    $("folder-path").replaceChildren(element("ol", {}, crumbs.map((crumb) => element("li", {}, [crumb]))));

    // On narrow screens the sidebar is hidden; a select offers the same folders.
    const select = $("folder-select");
    select.replaceChildren(
        element("option", { value: "", text: "All images", selected: !state.folder }),
        ...folderOptions(state.folders, { noneLabel: "Not in a folder", selected: id ?? "" }).slice(1),
        element("option", { value: "none", text: "Not in a folder", selected: state.folder === "none" }));
    select.onchange = () => { location.href = pageLink({ folder: select.value || null }); };

    $("folder-actions").hidden = !editable;
    for (const name of ["rename-folder", "move-folder", "delete-folder"]) $(name).hidden = !id;
    $("filter-subfolders").closest(".checkbox").hidden = !id;
}

function folderNameField(value = "") {
    const input = element("input", { type: "text", required: true, maxlength: "100", autocomplete: "off", value });
    return [element("label", { class: "field" }, [element("span", { text: "Name" }), input]), input];
}

async function runFolderAction(action, message) {
    try {
        await action();
        await loadFolders();
        await loadAssets();
        announce(message);
    } catch (error) {
        announce(error.message);
        await openDialog({ title: "That did not work", text: error.message, confirmLabel: "OK" });
    }
}

$("new-folder").addEventListener("click", async () => {
    const [field, input] = folderNameField();
    const parent = folderId();
    const parentName = parent ? state.folders.find((f) => f.id === parent)?.name : null;
    if (!await openDialog({ title: "New folder", text: parentName ? `Inside ${parentName}.` : "At the top level of the project.", fields: [field], confirmLabel: "Create folder" })) return;
    await runFolderAction(async () => {
        const folder = await send(`${projectPath()}/folders`, "POST", { name: input.value, parentId: parent });
        history.replaceState(null, "", pageLink({ folder: String(folder.id) }));
        state.folder = String(folder.id);
    }, `Created the folder ${input.value.trim()}.`);
});

$("rename-folder").addEventListener("click", async () => {
    const folder = state.folders.find((f) => f.id === folderId());
    const [field, input] = folderNameField(folder.name);
    if (!await openDialog({ title: `Rename ${folder.name}`, text: "Image links stay the same.", fields: [field], confirmLabel: "Rename" })) return;
    await runFolderAction(() => send(`${projectPath()}/folders/${folder.id}`, "PATCH", { name: input.value }), `Renamed the folder to ${input.value.trim()}.`);
});

$("move-folder").addEventListener("click", async () => {
    const folder = state.folders.find((f) => f.id === folderId());
    const select = element("select", {}, folderOptions(state.folders, {
        noneLabel: "Top level", selected: folder.parentId ?? "", exclude: folderSubtree(state.folders, folder.id),
    }));
    const field = element("label", { class: "field" }, [element("span", { text: "Move into" }), select]);
    if (!await openDialog({ title: `Move ${folder.name}`, text: "Its images and subfolders move along; links stay the same.", fields: [field], confirmLabel: "Move" })) return;
    await runFolderAction(() => send(`${projectPath()}/folders/${folder.id}/move`, "POST", { parentId: select.value ? Number(select.value) : null }),
        `Moved the folder ${folder.name}.`);
});

$("delete-folder").addEventListener("click", async () => {
    const folder = state.folders.find((f) => f.id === folderId());
    const parent = state.folders.find((f) => f.id === folder.parentId);
    const target = parent ? `the folder ${parent.name}` : "the top level";
    if (!await openDialog({
        title: `Delete ${folder.name}?`,
        text: `Its images and subfolders move to ${target}. Nothing is deleted from the project, and links stay the same.`,
        confirmLabel: "Delete folder", danger: true,
    })) return;
    await runFolderAction(async () => {
        await send(`${projectPath()}/folders/${folder.id}`, "DELETE");
        state.folder = folder.parentId ? String(folder.parentId) : null;
        history.replaceState(null, "", pageLink());
    }, `Deleted the folder ${folder.name}.`);
});

// Moving images: drag them onto a folder in the sidebar, or select them and use "Move to folder".
const dragType = "application/x-mdu-assets";

async function moveAssets(ids, target, targetName) {
    try {
        const result = await send(`${projectPath()}/assets/move`, "POST", { ids, folderId: target });
        state.selected.clear();
        await loadFolders();
        await loadAssets();
        announce(`Moved ${plural(result.moved, "image", "images")} to ${targetName}.`);
    } catch (error) {
        announce(error.message);
    }
}

function makeDropTarget(anchor, target, name) {
    anchor.addEventListener("dragover", (event) => {
        if (![...event.dataTransfer.types].includes(dragType)) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        anchor.classList.add("is-drop-target");
    });
    anchor.addEventListener("dragleave", () => anchor.classList.remove("is-drop-target"));
    anchor.addEventListener("drop", (event) => {
        if (![...event.dataTransfer.types].includes(dragType)) return;
        event.preventDefault();
        event.stopPropagation();
        anchor.classList.remove("is-drop-target");
        moveAssets(JSON.parse(event.dataTransfer.getData(dragType)), target, name);
    });
}

function dragAssets(event, asset) {
    const ids = state.selected.has(asset.id) ? [...state.selected] : [asset.id];
    event.dataTransfer.setData(dragType, JSON.stringify(ids));
    event.dataTransfer.setData("text/uri-list", asset.url);
    event.dataTransfer.effectAllowed = "copyMove";
}

$("move-selected").addEventListener("click", async () => {
    const select = element("select", {}, folderOptions(state.folders, { noneLabel: "Not in a folder", selected: folderId() ?? "" }));
    const field = element("label", { class: "field" }, [element("span", { text: "Folder" }), select]);
    const count = state.selected.size;
    if (!await openDialog({ title: `Move ${plural(count, "image", "images")}`, text: "Links stay the same.", fields: [field], confirmLabel: "Move" })) return;
    const target = select.value ? Number(select.value) : null;
    await moveAssets([...state.selected], target, target ? select.selectedOptions[0].textContent.trim() : "no folder");
});

$("clear-selection").addEventListener("click", () => {
    state.selected.clear();
    renderAssets();
    announce("Selection cleared.");
});

function toggleSelected(asset) {
    if (state.selected.has(asset.id)) state.selected.delete(asset.id); else state.selected.add(asset.id);
    renderAssets();
    announce(`${state.selected.has(asset.id) ? "Selected" : "Deselected"} ${asset.fileName}. ${plural(state.selected.size, "image", "images")} selected.`);
}

// Search and filters (ORG-04) live in the address, so a filtered view can be shared or bookmarked.
async function loadFacets() {
    state.facets = await request(`${projectPath()}/facets`);
}

const typeLabels = { "image/png": "PNG", "image/jpeg": "JPEG", "image/gif": "GIF", "image/webp": "WebP", "image/avif": "AVIF", "image/bmp": "BMP", "image/svg+xml": "SVG" };
const typeKey = (contentType) => ({ "image/svg+xml": "svg", "image/jpeg": "jpeg" })[contentType] ?? contentType.replace("image/", "");

function renderFilters() {
    const { filters, facets } = state;
    const options = (select, first, values) => select.replaceChildren(
        element("option", { value: "", text: first }),
        ...values.map(([value, label]) => element("option", { value, text: label })));
    options($("filter-tag"), "Any tag", facets.tags.map((t) => [t.name, `${t.name} (${numberFormat.format(t.count)})`]));
    options($("filter-type"), "Any type", facets.types.map((t) => [typeKey(t), typeLabels[t] ?? t]));
    options($("filter-uploader"), "Anyone", facets.uploaders.map((u) => [u, u]));
    $("search-input").value = filters.q;
    $("filter-tag").value = filters.tag;
    $("filter-type").value = filters.type;
    $("filter-uploader").value = filters.uploader;
    $("filter-from").value = filters.from;
    $("filter-to").value = filters.to;
    $("filter-subfolders").checked = filters.subfolders;
    const active = ["tag", "type", "uploader", "from", "to"].filter((key) => filters[key]).length + (filters.subfolders && folderId() ? 1 : 0);
    $("filter-count").textContent = active ? ` (${active})` : "";
    if (active) $("filters").open = true;
}

const filtered = () => Boolean(state.filters.q || state.filters.tag || state.filters.type || state.filters.uploader || state.filters.from || state.filters.to);

async function applyFilters() {
    state.filters = {
        q: $("search-input").value.trim(),
        tag: $("filter-tag").value,
        type: $("filter-type").value,
        uploader: $("filter-uploader").value,
        from: $("filter-from").value,
        to: $("filter-to").value,
        subfolders: $("filter-subfolders").checked,
    };
    history.replaceState(null, "", pageLink());
    renderFilters();
    state.selected.clear();
    await loadAssets();
    announce(state.assets.length || !filtered() ? `${plural(state.assets.length, "image", "images")}${state.next ? " or more" : ""} found.` : "No images match.");
}

/** A public link or an asset id pasted into the search opens that image (LNK-09). */
const looksLikeLink = (text) => /\/i\/[A-Za-z0-9]{10}(?:[/?#]|$)/.test(text);

async function openLink(text) {
    try {
        const asset = await request(`/lookup?url=${encodeURIComponent(text)}`);
        location.href = `/?project=${encodeURIComponent(asset.project)}&asset=${encodeURIComponent(asset.id)}`;
    } catch (error) {
        announce(error.status === 404 ? "That image does not exist, or you cannot see its project." : error.message);
        await openDialog({
            title: "Image not found",
            text: error.status === 404 ? "That image does not exist, or you are not a member of its project." : error.message,
            confirmLabel: "OK",
        });
    }
}

$("search-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const text = $("search-input").value.trim();
    if (looksLikeLink(text)) openLink(text); else applyFilters();
});
$("search-input").addEventListener("paste", (event) => {
    const text = event.clipboardData?.getData("text")?.trim() ?? "";
    if (looksLikeLink(text)) {
        event.preventDefault();
        $("search-input").value = text;
        openLink(text);
    }
});
for (const id of ["filter-tag", "filter-type", "filter-uploader", "filter-from", "filter-to", "filter-subfolders"])
    $(id).addEventListener("change", () => applyFilters());
$("clear-filters").addEventListener("click", () => {
    for (const id of ["search-input", "filter-tag", "filter-type", "filter-uploader", "filter-from", "filter-to"]) $(id).value = "";
    $("filter-subfolders").checked = false;
    applyFilters();
    $("search-input").focus();
});

// The list comes in pages, newest first; "Show more images" loads the next one.
function assetsPath(after) {
    const query = new URLSearchParams({ limit: "200" });
    if (after) query.set("after", after);
    if (state.folder) query.set("folder", state.folder);
    if (state.folder && state.folder !== "none" && state.filters.subfolders) query.set("subfolders", "true");
    for (const key of ["q", "tag", "type", "uploader", "from", "to"]) if (state.filters[key]) query.set(key, state.filters[key]);
    return `${projectPath()}/assets?${query}`;
}

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

const detailsLink = (asset) => `/?project=${encodeURIComponent(state.project)}&asset=${encodeURIComponent(asset.id)}`;

function renderAssets() {
    const assets = sortedAssets();
    const editable = canEdit();
    $("empty-state").hidden = assets.length > 0;
    if (!assets.length)
        $("empty-state").textContent = filtered() ? "No images match the search and filters."
            : state.folder ? "This folder has no images." : editable ? "No images yet. Drop, paste or upload one." : "No images yet.";
    $("load-more").hidden = !state.next;

    // Selected images that are no longer listed (after a search or move) drop out of the selection.
    for (const id of state.selected) if (!assets.some((a) => a.id === id)) state.selected.delete(id);
    $("selection-bar").hidden = state.selected.size === 0;
    $("selection-count").textContent = `${plural(state.selected.size, "image", "images")} selected`;
    $("move-selected").hidden = !editable;

    const grid = state.view === "grid";
    $("asset-grid").hidden = !grid;
    $("keyboard-hint").hidden = !grid || assets.length === 0;
    $("asset-table-wrapper").hidden = grid || assets.length === 0;
    $("view-grid").setAttribute("aria-pressed", String(grid));
    $("view-list").setAttribute("aria-pressed", String(!grid));

    // One image is in the tab order; the arrow keys move between images (roving tabindex).
    if (!assets.some((a) => a.id === state.focusId)) state.focusId = assets[0]?.id ?? null;
    const hadFocus = document.activeElement?.closest?.("#asset-grid, #asset-rows") !== null;

    const checkbox = (asset) => {
        const box = element("input", {
            class: "asset-select", type: "checkbox", tabindex: "-1", checked: state.selected.has(asset.id), "aria-label": `Select ${asset.fileName}`,
        });
        box.addEventListener("change", () => toggleSelected(asset));
        return box;
    };
    const draggable = (link, asset) => {
        if (!editable) return link;
        link.setAttribute("draggable", "true");
        link.addEventListener("dragstart", (event) => dragAssets(event, asset));
        return link;
    };

    if (grid) {
        $("asset-grid").replaceChildren(...assets.map((asset) => {
            const selected = state.selected.has(asset.id);
            const link = draggable(element("a", {
                class: "asset-card",
                href: detailsLink(asset),
                tabindex: asset.id === state.focusId ? "0" : "-1",
                "data-id": asset.id,
                "aria-label": `${asset.fileName}, ${formatDimensions(asset)}, ${formatSize(asset.size)}${selected ? ", selected" : ""}`,
            }, [
                element("span", { class: "asset-thumb" }, [element("img", { src: asset.url, alt: "", loading: "lazy", decoding: "async", draggable: "false" })]),
                element("span", { class: "asset-meta" }, [
                    element("span", { class: "asset-name", text: asset.fileName, title: asset.fileName }),
                    element("span", { class: "asset-detail", text: `${formatDimensions(asset)} · ${formatSize(asset.size)}` }),
                ]),
            ]), asset);
            return element("li", { class: `asset-item${selected ? " is-selected" : ""}` }, [link, checkbox(asset)]);
        }));
        if (hadFocus) $("asset-grid").querySelector(`[data-id="${state.focusId}"]`)?.focus({ preventScroll: true });
    } else {
        for (const header of document.querySelectorAll("#asset-table th[aria-sort]")) {
            const key = header.querySelector("button").dataset.sort;
            header.setAttribute("aria-sort", key === state.sort.key ? state.sort.direction : "none");
        }
        $("asset-rows").replaceChildren(...assets.map((asset) => {
            const box = checkbox(asset);
            box.removeAttribute("tabindex");
            const link = draggable(element("a", { class: "row-button", href: detailsLink(asset), "data-id": asset.id }, [
                element("img", { src: asset.url, alt: "", loading: "lazy", decoding: "async", draggable: "false" }),
                element("span", { text: asset.fileName }),
            ]), asset);
            return element("tr", { class: state.selected.has(asset.id) ? "is-selected" : "" }, [
                element("td", { class: "select-column" }, [box]),
                element("td", {}, [link]),
                element("td", { text: formatDimensions(asset) }),
                element("td", { text: formatSize(asset.size) }),
                element("td", { text: dateFormat.format(new Date(asset.createdAt)) }),
            ]);
        }));
    }
}

// Keyboard in the grid (ORG-07): arrows move, Enter opens (a link), C copies the Markdown, Space selects.
$("asset-grid").addEventListener("keydown", async (event) => {
    const link = event.target.closest?.(".asset-card");
    if (!link || event.altKey || event.ctrlKey || event.metaKey) return;
    const cards = [...$("asset-grid").querySelectorAll(".asset-card")];
    const index = cards.indexOf(link);
    const asset = state.assets.find((a) => a.id === link.dataset.id);

    const columns = Math.max(1, cards.filter((card) => card.offsetTop === cards[0].offsetTop).length);
    const target = {
        ArrowRight: index + 1, ArrowLeft: index - 1, ArrowDown: index + columns, ArrowUp: index - columns,
        Home: 0, End: cards.length - 1,
    }[event.key];
    if (target !== undefined) {
        event.preventDefault();
        const next = cards[Math.min(cards.length - 1, Math.max(0, target))];
        link.tabIndex = -1;
        next.tabIndex = 0;
        state.focusId = next.dataset.id;
        next.focus();
        return;
    }
    if (event.key === " ") {
        event.preventDefault();
        toggleSelected(asset);
    } else if (event.key.toLowerCase() === "c" && !event.shiftKey) {
        event.preventDefault();
        const ok = await copyText(asset.markdown);
        announce(ok ? `Copied the Markdown snippet of ${asset.fileName}.` : "Copying failed. Open the image to copy its snippet.");
    }
});
$("asset-grid").addEventListener("focusin", (event) => {
    const link = event.target.closest?.(".asset-card");
    if (link) state.focusId = link.dataset.id;
});

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
        // Uploads land in the folder that is open.
        const folder = folderId() ? `&folder=${folderId()}` : "";
        xhr.open("POST", `${api}${projectPath()}/assets?name=${encodeURIComponent(name)}${alt ? `&alt=${encodeURIComponent(alt)}` : ""}${folder}`);
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
        await loadFolders();
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
// A dragged image that is dropped next to a folder must not make the browser open its link.
document.addEventListener("dragover", (event) => {
    if ([...(event.dataTransfer?.types ?? [])].includes(dragType)) event.preventDefault();
});
document.addEventListener("drop", (event) => {
    if ([...(event.dataTransfer?.types ?? [])].includes(dragType)) { event.preventDefault(); return; }
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
