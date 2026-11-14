// Details of one image (ORG-06): large preview, snippets, description, tags, folder and metadata.
import {
    $, announce, clearFormError, copyText, dateFormat, element, flashCopied, folderOptions, formatSize, handleSubmit,
    readSetting, request, send, writeSetting,
} from "./common.js";

const snippets = [
    ["Markdown", (asset) => asset.markdown],
    ["URL", (asset) => asset.url],
    ["HTML", (asset) => asset.html],
];

const typeNames = {
    "image/png": "PNG", "image/jpeg": "JPEG", "image/gif": "GIF", "image/webp": "WebP",
    "image/avif": "AVIF", "image/bmp": "BMP", "image/svg+xml": "SVG",
};

const projectLink = (project, folderId) =>
    `/?project=${encodeURIComponent(project.slug)}${folderId ? `&folder=${folderId}` : ""}`;

export async function showDetails(project, id, { editable, onFolderChange }) {
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

    const render = (asset) => {
        current = asset;
        document.title = `${asset.fileName} – ${project.name} – MDU Assets`;
        $("asset-title").textContent = asset.fileName;
        $("asset-summary").textContent = `${asset.width && asset.height ? `${asset.width} × ${asset.height} · ` : ""}${formatSize(asset.size)}`;
        preview.replaceChildren(element("img", { src: asset.url, alt: asset.altText }));

        $("asset-snippets").replaceChildren(...snippets.map(([label, text]) => {
            const copy = element("button", { class: "copy-button", type: "button", "aria-label": `Copy ${label}` }, [element("span", { text: "Copy" })]);
            copy.addEventListener("click", async () => {
                const ok = await copyText(text(current));
                flashCopied(copy, ok);
                announce(ok ? `Copied the ${label} snippet.` : "Copying failed. Select the text and copy it.");
            });
            return element("div", { class: "snippet-row" }, [
                element("span", { class: "snippet-label", text: label }),
                element("code", { class: "snippet-code", tabindex: "0", text: text(asset), "aria-label": `${label} snippet` }),
                copy,
            ]);
        }));

        const path = folders.find((folder) => folder.id === asset.folderId);
        const meta = [
            ["Type", typeNames[asset.contentType] ?? asset.contentType],
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
}
