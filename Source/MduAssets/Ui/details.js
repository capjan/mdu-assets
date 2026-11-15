// Details of one image (ORG-06): large preview, snippets, description, tags, folder and metadata.
import {
    $, announce, clearFormError, copyText, dateFormat, element, flashCopied, folderOptions, formatSize, handleSubmit,
    numberFormat, openDialog, plural, readSetting, recentUse, request, send, writeSetting,
} from "./common.js";
import { buildSnippet, formats, optionControls, readOptions, writeOptions } from "./snippets.js";

const typeNames = {
    "image/png": "PNG", "image/jpeg": "JPEG", "image/gif": "GIF", "image/webp": "WebP",
    "image/avif": "AVIF", "image/bmp": "BMP", "image/svg+xml": "SVG",
};

const projectLink = (project, folderId) =>
    `/?project=${encodeURIComponent(project.slug)}${folderId ? `&folder=${folderId}` : ""}`;

/** Set while a details page is open: files dropped or pasted there replace the image (VER-01). */
export let replaceWithFile = null;

export async function showDetails(project, id, { editable: canEdit, onFolderChange, me }) {
    const [asset, folders, facets] = await Promise.all([
        request(`/assets/${encodeURIComponent(id)}`),
        request(`/projects/${encodeURIComponent(project.slug)}/folders`).then((list) => list.items),
        request(`/projects/${encodeURIComponent(project.slug)}/facets`),
    ]);
    if (asset.project !== project.slug) {
        location.replace(`/?project=${encodeURIComponent(asset.project)}&asset=${encodeURIComponent(asset.id)}`);
        return;
    }

    let current = asset;
    // Images in the trash are read-only until they are restored.
    const editable = canEdit && !asset.deletedAt;
    $("asset-back").href = projectLink(project, asset.folderId);
    $("asset-back").textContent = `Back to ${project.name}`;

    // Preview on a checkerboard, light or dark background; the choice is remembered.
    const preview = $("asset-preview");
    const setBackground = (background) => {
        preview.dataset.background = background;
        for (const button of document.querySelectorAll(".preview-switch button"))
            button.setAttribute("aria-pressed", String(button.dataset.background === background));
        writeSetting("mdu-assets-preview-background", background);
    };
    for (const button of document.querySelectorAll(".preview-switch button"))
        button.addEventListener("click", () => setBackground(button.dataset.background));
    setBackground(readSetting("mdu-assets-preview-background", "checker"));

    // Snippets with the MDU switches the user chose last and an optional caption (LNK-06, LNK-07).
    let options = readOptions(me.name);
    const caption = $("asset-caption");
    const renderSnippets = () => {
        $("asset-snippets").replaceChildren(...Object.entries(formats).map(([format, label]) => {
            const text = () => buildSnippet(current, format, options, caption.value.trim());
            const copy = element("button", { class: "copy-button", type: "button", "aria-label": `Copy ${label}` }, [element("span", { text: "Copy" })]);
            copy.addEventListener("click", async () => {
                const ok = await copyText(text());
                flashCopied(copy, ok);
                announce(ok ? `Copied the ${label} snippet.` : "Copying failed. Select the text and copy it.");
            });
            return element("div", { class: "snippet-row" }, [
                element("span", { class: "snippet-label", text: label }),
                element("code", { class: "snippet-code", tabindex: "0", text: text(), "aria-label": `${label} snippet` }),
                copy,
            ]);
        }));
    };
    $("asset-snippet-options").replaceChildren(optionControls(options, me.widths, (changed) => {
        options = changed;
        writeOptions(me.name, options);
        renderSnippets();
    }));
    caption.addEventListener("input", () => renderSnippets());

    // The light and dark partner (LNK-06): shown with its preview, or a form to choose one.
    const projectAssets = editable
        ? request(`/projects/${encodeURIComponent(project.slug)}/assets?limit=500`).then((list) => list.items)
        : Promise.resolve([]);
    const renderPair = async () => {
        const panel = $("asset-pair");
        if (current.pair) {
            const partner = await request(`/assets/${encodeURIComponent(current.pair.id)}`);
            const other = current.pair.role === "light" ? "dark" : "light";
            const unpair = element("button", { class: "secondary-button", type: "button", text: "Unpair", hidden: !editable });
            unpair.addEventListener("click", async () => {
                try {
                    render(await send(`/assets/${encodeURIComponent(current.id)}/pair`, "DELETE"));
                    announce("The pair is dissolved.");
                    $("asset-pair").querySelector("select")?.focus();
                } catch (error) {
                    announce(error.message);
                }
            });
            panel.replaceChildren(
                element("div", { class: "pair-partner" }, [
                    element("img", { src: `${location.origin}${partner.thumbnailUrl}`, alt: "" }),
                    element("p", {}, [
                        `This is the ${current.pair.role} variant. The ${other} variant is `,
                        element("a", { href: `/?project=${encodeURIComponent(project.slug)}&asset=${encodeURIComponent(partner.id)}`, text: partner.fileName }),
                        ".",
                    ]),
                ]),
                unpair);
            return;
        }

        if (!editable) {
            panel.replaceChildren(element("p", { class: "form-hint", text: "This image has no light or dark partner." }));
            return;
        }
        const role = element("select", {}, [
            element("option", { value: "light", text: "This is the light variant" }),
            element("option", { value: "dark", text: "This is the dark variant" }),
        ]);
        const partner = element("select", {}, [element("option", { value: "", text: "Choose the other variant" })]);
        const pairButton = element("button", { class: "primary-button", type: "submit", text: "Pair" });
        const form = element("form", { class: "form" }, [
            element("label", { class: "field" }, [element("span", { text: "This image" }), role]),
            element("label", { class: "field" }, [element("span", { text: "Partner" }), partner]),
            element("div", { class: "form-actions" }, [pairButton]),
        ]);
        if (/[-_.]dark\.[a-z0-9]+$/i.test(current.fileName)) role.value = "dark";
        panel.replaceChildren(form);
        const candidates = (await projectAssets).filter((a) => a.id !== current.id);
        partner.append(...candidates.map((a) => element("option", { value: a.id, text: a.fileName })));
        // Suggest the image whose name differs only in -light/-dark.
        const counterpart = current.fileName.replace(/([-_.])(light|dark)(\.[a-z0-9]+)$/i, (_, separator, which, extension) =>
            `${separator}${which.toLowerCase() === "light" ? "dark" : "light"}${extension}`);
        partner.value = candidates.find((a) => a.fileName.toLowerCase() === counterpart.toLowerCase())?.id ?? "";
        handleSubmit(form, async () => {
            if (!partner.value) throw new Error("Choose the other variant first.");
            render(await send(`/assets/${encodeURIComponent(current.id)}/pair`, "PUT", { partnerId: partner.value, role: role.value }));
            announce("Paired. The Markdown snippet now has both lines.");
            $("asset-pair").querySelector("button")?.focus();
        });
    };

    const render = (asset) => {
        current = asset;
        document.title = `${asset.fileName} – ${project.name} – MDU Assets`;
        $("asset-title").textContent = asset.fileName;
        $("asset-summary").textContent = `${asset.width && asset.height ? `${asset.width} × ${asset.height} · ` : ""}${formatSize(asset.size)}`;
        // The version link changes with each version, so a replaced image shows at once despite browser caches.
        preview.replaceChildren(element("img", { src: asset.versionUrl, alt: asset.altText }));

        renderSnippets();
        renderPair();
        $("asset-stats").textContent = asset.requests
            ? `${plural(asset.requests, "request", "requests")}, the last on ${dateFormat.format(new Date(asset.lastRequestAt))}.`
            : "Not requested yet.";

        const path = folders.find((folder) => folder.id === asset.folderId);
        const meta = [
            ["Type", typeNames[asset.contentType] ?? asset.contentType],
            ...(asset.originalContentType
                ? [["Uploaded as", `${typeNames[asset.originalContentType] ?? asset.originalContentType}, ${formatSize(asset.originalSize)} (kept, not public)`]]
                : []),
            ["Dimensions", asset.width && asset.height ? `${asset.width} × ${asset.height} pixels` : "–"],
            ["Size", formatSize(asset.size)],
            ["Folder", path?.name ?? "Not in a folder"],
            ["Uploaded", dateFormat.format(new Date(asset.createdAt))],
            ["Uploaded by", asset.uploadedBy],
            ["Version", String(asset.version)],
            ["SHA-256", asset.sha256],
            ["Public link", element("a", { href: asset.url, text: asset.url, target: "_blank", rel: "noopener" })],
        ];
        $("asset-meta").replaceChildren(...meta.flatMap(([term, value]) => [element("dt", { text: term }), element("dd", {}, [value])]));
    };
    render(asset);

    // Description, tags and folder; viewers see them read-only.
    const form = $("asset-form");
    const fields = { fileName: $("asset-file-name"), altText: $("asset-alt"), description: $("asset-description"), folder: $("asset-folder") };
    fields.fileName.value = asset.fileName;
    fields.altText.value = asset.altText;
    fields.description.value = asset.description;
    fields.folder.replaceChildren(...folderOptions(folders, { noneLabel: "Not in a folder", selected: asset.folderId ?? "" }));
    $("asset-tag-suggestions").replaceChildren(...facets.tags.map((tag) => element("option", { value: tag.name })));

    let tags = [...asset.tags];
    const tagInput = $("asset-tag-input");
    const renderTags = () => {
        $("asset-tags").replaceChildren(...tags.map((tag) => {
            const remove = element("button", { class: "tag-remove", type: "button", "aria-label": `Remove tag ${tag}`, text: "×", hidden: !editable });
            remove.addEventListener("click", () => {
                tags = tags.filter((t) => t !== tag);
                renderTags();
                tagInput.focus();
                announce(`Removed tag ${tag}.`);
            });
            return element("li", { class: "tag" }, [element("span", { text: tag }), remove]);
        }));
    };
    const addTags = () => {
        const added = tagInput.value.split(",").map((t) => t.trim().replace(/\s+/g, " ")).filter(Boolean);
        for (const tag of added) if (!tags.some((t) => t.toLowerCase() === tag.toLowerCase())) tags.push(tag);
        tagInput.value = "";
        renderTags();
        if (added.length) announce(`Added ${added.join(", ")}.`);
    };
    tagInput.addEventListener("keydown", (event) => {
        if (!editable) return;
        if (event.key === "Enter" || event.key === ",") {
            event.preventDefault();
            addTags();
        } else if (event.key === "Backspace" && tagInput.value === "" && tags.length) {
            announce(`Removed tag ${tags.pop()}.`);
            renderTags();
        }
    });
    // Picking a suggestion from the list adds it right away.
    tagInput.addEventListener("input", (event) => {
        if (event.inputType === "insertReplacementText") addTags();
    });
    renderTags();

    for (const field of [fields.fileName, fields.altText, fields.description, tagInput]) field.readOnly = !editable;
    fields.folder.disabled = !editable;
    $("asset-save").hidden = !editable;
    $("asset-tag-hint").hidden = !editable;

    handleSubmit(form, async () => {
        clearFormError(form);
        if (tagInput.value.trim()) addTags();
        // One request, so text, tags and folder are saved together or not at all.
        const folderChanged = (fields.folder.value || null) !== (current.folderId === null ? null : String(current.folderId));
        const updated = await send(`/assets/${encodeURIComponent(current.id)}`, "PATCH", {
            fileName: fields.fileName.value, altText: fields.altText.value, description: fields.description.value, tags,
            folder: fields.folder.value || "none",
        });
        if (folderChanged) await onFolderChange?.();
        fields.fileName.value = updated.fileName;
        tags = [...updated.tags];
        renderTags();
        render(updated);
        $("asset-back").href = projectLink(project, updated.folderId);
        announce(`Saved ${updated.fileName}.`);
    });

    setUpLifecycle(project, { canEdit, editable, owner: project.role === "owner", get: () => current, render });
}

// Versions, replacing and the trash (VER-01 – VER-04, DEL-01 – DEL-03).
function setUpLifecycle(project, { canEdit, editable, owner, get, render }) {
    const asset = get();
    const projectPage = `/?project=${encodeURIComponent(project.slug)}`;

    // An image in the trash: say so, offer to restore it, hide everything that changes it.
    $("asset-trashed").hidden = !asset.deletedAt;
    if (asset.deletedAt) {
        $("asset-trashed-text").textContent = `This image is in the trash since ${dateFormat.format(new Date(asset.deletedAt))}. Its link answers "410 Gone" until it is restored.`;
        $("asset-restore").hidden = !canEdit;
        $("asset-restore").addEventListener("click", async () => {
            try {
                await send(`/assets/${encodeURIComponent(asset.id)}/restore`, "POST");
                location.reload();
            } catch (error) {
                announce(error.message);
            }
        });
    }
    $("asset-actions").hidden = !editable;
    $("asset-replace-hint").hidden = !editable;

    const renderVersions = async () => {
        const versions = (await request(`/assets/${encodeURIComponent(asset.id)}/versions`)).items;
        $("asset-versions").replaceChildren(...versions.map((version) => {
            const actions = [element("a", { class: "secondary-button", href: version.url, target: "_blank", rel: "noopener", text: "Open", "aria-label": `Open version ${version.number}` })];
            if (editable && !version.isCurrent) {
                const restore = element("button", { class: "secondary-button", type: "button", text: "Make current", "aria-label": `Make version ${version.number} current` });
                restore.addEventListener("click", async () => {
                    try {
                        render(await send(`/assets/${encodeURIComponent(asset.id)}/versions/${version.number}/restore`, "POST"));
                        await renderVersions();
                        $("asset-versions").querySelector("a")?.focus();
                        announce(`Version ${version.number} is current again, as a new version.`);
                    } catch (error) {
                        announce(error.message);
                    }
                });
                actions.push(restore);
            }
            if (owner && !version.isCurrent) {
                const remove = element("button", { class: "secondary-button", type: "button", text: "Delete", "aria-label": `Delete version ${version.number}` });
                remove.addEventListener("click", async () => {
                    if (!await openDialog({
                        title: `Delete version ${version.number}?`,
                        text: "Its version link stops working. This cannot be undone.",
                        confirmLabel: "Delete version", danger: true,
                    })) return;
                    try {
                        await send(`/assets/${encodeURIComponent(asset.id)}/versions/${version.number}`, "DELETE");
                        await renderVersions();
                        $("asset-versions").querySelector("a")?.focus();
                        announce(`Deleted version ${version.number}.`);
                    } catch (error) {
                        announce(error.message);
                    }
                });
                actions.push(remove);
            }
            const details = [
                version.width && version.height ? `${version.width} × ${version.height}` : null,
                formatSize(version.size),
                typeNames[version.contentType] ?? version.contentType,
                version.originalContentType ? `from ${typeNames[version.originalContentType] ?? version.originalContentType}` : null,
            ].filter(Boolean).join(" · ");
            return element("li", { class: `version${version.isCurrent ? " is-current" : ""}` }, [
                element("img", { src: `${version.url}?w=320`, alt: "", loading: "lazy" }),
                element("div", { class: "version-text" }, [
                    element("strong", { text: `Version ${version.number}${version.isCurrent ? " (current)" : ""}` }),
                    element("span", { text: `${dateFormat.format(new Date(version.createdAt))} by ${version.createdBy}` }),
                    element("span", { text: details }),
                ]),
                element("div", { class: "row-actions" }, actions),
            ]);
        }));
    };
    renderVersions();

    // Pages that showed the image (STA-04).
    request(`/assets/${encodeURIComponent(asset.id)}/referrers`).then((list) => {
        $("asset-referrers-wrapper").hidden = list.items.length === 0;
        $("asset-referrers").replaceChildren(...list.items.map((r) => element("tr", {}, [
            element("td", {}, [element("span", { class: "referrer", text: `${r.origin}${r.path === "/" ? "" : r.path}` })]),
            element("td", { text: numberFormat.format(r.count) }),
            element("td", { text: dateFormat.format(new Date(r.lastRequestAt)) }),
        ])));
    }).catch(() => { });

    // A new version from the file picker, a drop or a paste on this page.
    const replace = async (file) => {
        if (!editable || !file) return;
        const button = $("asset-replace");
        button.disabled = true;
        announce(`Uploading ${file.name || "the image"} as a new version…`);
        try {
            const result = await request(`/assets/${encodeURIComponent(asset.id)}/versions`, {
                method: "POST", headers: { "Content-Type": file.type || "application/octet-stream" }, body: file,
            });
            render(result.asset);
            await renderVersions();
            const hint = $("asset-format-hint");
            hint.hidden = !result.formatChanged;
            if (result.formatChanged) {
                const from = typeNames[result.previousContentType] ?? result.previousContentType;
                const to = typeNames[result.asset.contentType] ?? result.asset.contentType;
                hint.textContent = `The new version is ${to} instead of ${from}, so the file name is now ${result.asset.fileName}. Links with the old name keep working; copy the new snippet for new pages.`;
                hint.focus();
            }
            announce(`Version ${result.asset.version} is now behind the same link.`);
        } catch (error) {
            announce(error.message);
            await openDialog({ title: "The new version was not saved", text: error.message, confirmLabel: "OK" });
        } finally {
            button.disabled = false;
        }
    };
    replaceWithFile = editable ? replace : null;
    $("asset-replace").addEventListener("click", () => $("asset-replace-input").click());
    $("asset-replace-input").addEventListener("change", (event) => {
        replace(event.target.files[0]);
        event.target.value = "";
    });

    $("asset-trash").addEventListener("click", async () => {
        const current = get();
        const usage = await recentUse([current]);
        if (!await openDialog({
            title: `Move ${current.fileName} to the trash?`,
            text: `${usage ? `${usage} ` : ""}Its link answers "410 Gone" while it is in the trash. You can restore it with the same link from the trash.`,
            confirmLabel: "Move to trash", danger: true,
        })) return;
        try {
            await send(`/assets/${encodeURIComponent(current.id)}`, "DELETE");
            location.href = projectPage;
        } catch (error) {
            announce(error.message);
        }
    });
}
