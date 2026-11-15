// Snippets with MDU options (LNK-05 – LNK-08): light and dark pairs, {.image-frame}, zoom, ?w= and a figure caption.
import { element, readSetting, writeSetting } from "./common.js";

export const formats = { markdown: "Markdown", url: "URL", html: "HTML" };

const defaults = { frame: false, zoom: "", width: "" };

/** The switches a user chose last; they are remembered per user name in this browser (LNK-07). */
export function readOptions(user) {
    try {
        return { ...defaults, ...JSON.parse(readSetting(`mdu-assets-snippet-options:${user}`, "{}")) };
    } catch {
        return { ...defaults };
    }
}

export function writeOptions(user, options) {
    writeSetting(`mdu-assets-snippet-options:${user}`, JSON.stringify({ frame: options.frame, zoom: options.zoom, width: options.width }));
}

const escapeAlt = (text) => text.replace(/[\\[\]]/g, (c) => `\\${c}`).replace(/[\r\n]+/g, " ");
const escapeHtml = (text) => text.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" })[c]);

/** The URL with ?w= for a chosen width; widths at or above the original's serve the original anyway. */
const sized = (url, width) => (width ? `${url}?w=${width}` : url);

/**
 * Builds the snippet text. A paired image gives two Markdown lines with #only-light and #only-dark and this image's
 * alt text; attributes go after each image, and a caption wraps the images in an MDU figure (^^^).
 */
export function buildSnippet(asset, format, options = defaults, caption = "") {
    const width = options.width ? Number(options.width) : null;
    const classes = [options.frame ? "image-frame" : null, options.zoom ? `image-zoom-${options.zoom}` : null].filter(Boolean);

    if (format === "url") return sized(asset.url, width);

    if (format === "html") {
        const attributes = [`src="${escapeHtml(sized(asset.url, width))}"`, `alt="${escapeHtml(asset.altText)}"`];
        if (classes.length) attributes.push(`class="${classes.join(" ")}"`);
        if (asset.width && asset.height) {
            const shown = width && width < asset.width ? width : asset.width;
            attributes.push(`width="${shown}"`, `height="${Math.round((asset.height * shown) / asset.width)}"`);
        }
        const image = `<img ${attributes.join(" ")}>`;
        return caption ? `<figure>\n${image}\n<figcaption>${escapeHtml(caption)}</figcaption>\n</figure>` : image;
    }

    const attributes = classes.length ? `{${classes.map((c) => `.${c}`).join(" ")}}` : "";
    const line = (url, fragment = "") => `![${escapeAlt(asset.altText)}](${sized(url, width)}${fragment})${attributes}`;
    let lines;
    if (asset.pair) {
        const light = asset.pair.role === "light" ? asset.url : asset.pair.partnerUrl;
        const dark = asset.pair.role === "light" ? asset.pair.partnerUrl : asset.url;
        lines = [line(light, "#only-light"), line(dark, "#only-dark")];
    } else {
        lines = [line(asset.url)];
    }
    return caption ? `^^^\n${lines.join("\n")}\n^^^ ${caption.replace(/[\r\n]+/g, " ")}` : lines.join("\n");
}

/**
 * Controls for the snippet switches: frame, zoom and width. <paramref name="onChange"/> gets the new options.
 */
export function optionControls(options, widths, onChange) {
    const frame = element("input", { type: "checkbox", checked: options.frame });
    const zoom = element("select", { "aria-label": "Image zoom" }, [
        element("option", { value: "", text: "Zoom: page default", selected: !options.zoom }),
        element("option", { value: "on", text: "Zoom: always", selected: options.zoom === "on" }),
        element("option", { value: "off", text: "Zoom: never", selected: options.zoom === "off" }),
    ]);
    const width = element("select", { "aria-label": "Width" }, [
        element("option", { value: "", text: "Width: original", selected: !options.width }),
        ...widths.map((w) => element("option", { value: String(w), text: `Width: ${w} px`, selected: String(w) === String(options.width) })),
    ]);
    const changed = () => onChange({ frame: frame.checked, zoom: zoom.value, width: width.value });
    for (const control of [frame, zoom, width]) control.addEventListener("change", changed);
    return element("div", { class: "snippet-options" }, [
        element("label", { class: "checkbox" }, [frame, " Frame"]),
        zoom,
        width,
    ]);
}

/** Two uploads named like settings-light.png and settings-dark.png belong together (LNK-06). */
export function findPairs(assets) {
    const byStem = new Map();
    for (const asset of assets) {
        const match = /^(.*?)[-_.](light|dark)\.[a-z0-9]+$/i.exec(asset.fileName);
        if (!match) continue;
        const stem = match[1].toLowerCase();
        const entry = byStem.get(stem) ?? {};
        entry[match[2].toLowerCase()] = asset;
        byStem.set(stem, entry);
    }
    return [...byStem.values()].filter((entry) => entry.light && entry.dark && !entry.light.pair && !entry.dark.pair);
}
