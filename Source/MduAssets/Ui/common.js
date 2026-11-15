// Helpers shared by all pages of the web interface.

// Absolute URLs from the origin: a page opened as https://user:password@host/ would otherwise make every
// relative request carry the credentials, which fetch() refuses.
export const api = `${location.origin}/api/v1`;

export const $ = (id) => document.getElementById(id);

export function readSetting(key, fallback) {
    try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; }
}

export function writeSetting(key, value) {
    try { localStorage.setItem(key, value); } catch { }
}

// Whether uploads ask for a conversion without loss of quality (UP-11); one choice per browser for uploads and new versions.
const losslessKey = "mdu-assets-upload-lossless";

export function bindLosslessToggle(input) {
    input.checked = readSetting(losslessKey, "false") === "true";
    input.onchange = () => writeSetting(losslessKey, String(input.checked));
}

export const losslessQuery = () => readSetting(losslessKey, "false") === "true" ? "lossless=true" : "";

export function announce(message) {
    const status = $("status");
    status.textContent = "";
    requestAnimationFrame(() => { status.textContent = message; });
}

export function element(tag, properties = {}, children = []) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(properties)) {
        if (key === "class") node.className = value;
        else if (key === "text") node.textContent = value;
        else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
        else if (value !== undefined && value !== null && value !== false) node.setAttribute(key, value === true ? "" : value);
    }
    node.append(...children);
    return node;
}

export const numberFormat = new Intl.NumberFormat("en");
export const dateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });
export const shortDateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium" });

export function formatSize(bytes) {
    if (bytes < 1024) return `${numberFormat.format(bytes)} B`;
    if (bytes < 1024 * 1024) return `${numberFormat.format(Math.round(bytes / 102.4) / 10)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${numberFormat.format(Math.round(bytes / 104857.6) / 10)} MB`;
    return `${numberFormat.format(Math.round(bytes / 107374182.4) / 10)} GB`;
}

export const plural = (count, one, many) => `${numberFormat.format(count)} ${count === 1 ? one : many}`;

/** An API error with the problem details the server sent. */
export class ApiError extends Error {
    constructor(message, status, type) {
        super(message);
        this.status = status;
        this.type = type;
    }
}

/** The sign-in page, coming back to the current page afterwards. */
export function signInAgain() {
    location.href = `/login?return=${encodeURIComponent(location.pathname + location.search)}`;
}

export async function request(path, options = {}) {
    const response = await fetch(api + path, { ...options, headers: { Accept: "application/json", ...options.headers } });
    // The session ended, for example after a password change elsewhere or a long pause: sign in again.
    if (response.status === 401) {
        signInAgain();
        throw new ApiError("Your session has ended. Sign in again.", 401);
    }
    const body = response.headers.get("Content-Type")?.includes("json") ? await response.json() : null;
    if (!response.ok) throw new ApiError(body?.detail || body?.title || `${response.status} ${response.statusText}`, response.status, body?.type);
    return body;
}

export const send = (path, method, body) =>
    request(path, { method, headers: { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });

/** Runs a form's action with its submit button disabled and reports errors in the live region and next to the form. */
export function handleSubmit(form, action) {
    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const button = form.querySelector("[type=submit]");
        button.disabled = true;
        try {
            await action();
        } catch (error) {
            announce(error.message);
            showFormError(form, error.message);
        } finally {
            button.disabled = false;
        }
    });
}

export function showFormError(form, message) {
    let error = form.querySelector(".form-error");
    if (!error) {
        error = element("p", { class: "form-error", role: "alert" });
        form.append(error);
    }
    error.textContent = message;
    error.hidden = !message;
}

export const clearFormError = (form) => showFormError(form, "");

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

/** Shows on a copy button whether copying worked; the button's last child holds its label. */
export function flashCopied(button, ok) {
    button.classList.toggle("is-copied", ok);
    button.classList.toggle("is-failed", !ok);
    button.lastChild.textContent = ok ? "Copied" : "Copy failed";
    clearTimeout(button.resetTimer);
    button.resetTimer = setTimeout(() => {
        button.classList.remove("is-copied", "is-failed");
        button.lastChild.textContent = "Copy";
    }, 1600);
}

let dialogCount = 0;

/**
 * Shows a modal dialog with the given fields and resolves to true when confirmed, false when cancelled.
 * Escape and the Cancel button close it; focus returns to the element that opened it.
 */
export function openDialog({ title, text = "", fields = [], confirmLabel, danger = false }) {
    return new Promise((resolve) => {
        const opener = document.activeElement;
        const titleId = `dialog-title-${++dialogCount}`;
        const confirm = element("button", { class: danger ? "danger-button" : "primary-button", type: "submit", value: "confirm", text: confirmLabel });
        const cancel = element("button", { class: "secondary-button", type: "button", text: "Cancel" });
        const form = element("form", { class: "form", method: "dialog" }, [
            element("h2", { id: titleId, text: title }),
            ...(text ? [element("p", { class: "form-hint", text })] : []),
            ...fields,
            element("div", { class: "form-actions" }, [confirm, cancel]),
        ]);
        const dialog = element("dialog", { class: "dialog", "aria-labelledby": titleId }, [form]);
        cancel.addEventListener("click", () => dialog.close("cancel"));
        dialog.addEventListener("close", () => {
            dialog.remove();
            opener?.focus?.();
            resolve(dialog.returnValue === "confirm");
        });
        document.body.append(dialog);
        dialog.showModal();
        // Destructive dialogs start on Cancel, so a quick Enter does not delete anything.
        (form.querySelector("input, select") ?? (danger ? cancel : confirm)).focus();
    });
}

/**
 * Says which images were requested in the last 30 days, with counts and referring pages (DEL-01), so nobody moves an image
 * to the trash that documentation still shows. Empty when none of them was requested lately.
 */
export async function recentUse(assets) {
    const cutoff = Date.now() - 30 * 24 * 60 * 60 * 1000;
    const recent = assets.filter((asset) => asset.lastRequestAt && Date.parse(asset.lastRequestAt) > cutoff);
    if (recent.length === 0) return "";
    const lines = await Promise.all(recent.slice(0, 5).map(async (asset) => {
        let where = "";
        try {
            const referrers = (await request(`/assets/${encodeURIComponent(asset.id)}/referrers`)).items;
            const shown = referrers.slice(0, 3).map((r) => `${r.origin.replace(/^https?:\/\//, "")}${r.path === "/" ? "" : r.path}`);
            if (shown.length) where = ` on ${shown.join(", ")}${referrers.length > 3 ? ` and ${referrers.length - 3} more pages` : ""}`;
        } catch { }
        return `${asset.fileName} was requested ${plural(asset.requests, "time", "times")}, last on ${dateFormat.format(new Date(asset.lastRequestAt))}${where}.`;
    }));
    return `Still in use: ${lines.join(" ")}${recent.length > 5 ? ` ${plural(recent.length - 5, "more image was", "more images were")} requested lately.` : ""}`;
}

const auditActions = {
    "sign-in": "Signed in", "sign-in.failed": "Failed sign-in",
    "asset.upload": "Uploaded", "asset.update": "Changed", "asset.move": "Moved images", "asset.pair": "Paired", "asset.unpair": "Unpaired",
    "asset.version": "New version", "asset.version.restore": "Restored a version", "asset.version.delete": "Deleted a version",
    "asset.trash": "Moved to the trash", "asset.restore": "Restored", "asset.purge": "Deleted for good", "trash.empty": "Emptied the trash",
    "asset.tags": "Changed tags", "tag.rename": "Renamed a tag", "tag.delete": "Deleted a tag",
    // Entries from the time before tags replaced folders.
    "folder.create": "Created a folder", "folder.rename": "Renamed a folder", "folder.move": "Moved a folder", "folder.delete": "Deleted a folder",
    "project.create": "Created the project", "project.import": "Imported a project", "project.update": "Changed the project", "project.delete": "Deleted a project",
    "member.set": "Set a member", "member.remove": "Removed a member", "password.change": "Changed the password",
    "token.create": "Created a token", "token.delete": "Revoked a token", "user.create": "Created a user", "user.update": "Changed a user",
    "user.password-reset": "Reset a password", "settings.update": "Changed settings",
};

/** Shows an audit log (OPS-03) in a list, newest first, with a button for older entries. */
export function showAudit(list, more, path) {
    let before = null;
    const load = async () => {
        const page = await request(`${path}${before ? `?before=${before}` : ""}`);
        list.append(...page.items.map((entry) => element("li", { class: "audit-entry" }, [
            element("span", { class: "audit-time", text: dateFormat.format(new Date(entry.at)) }),
            element("span", { class: "audit-text", text: [
                entry.user ?? "Someone", auditActions[entry.action] ?? entry.action,
                entry.target ? `– ${entry.target}` : "", entry.project ? `in ${entry.project}` : "",
            ].filter(Boolean).join(" ") }),
            ...(entry.details || entry.ip ? [element("span", { class: "audit-details", text: [entry.details, entry.ip].filter(Boolean).join(" · ") })] : []),
        ])));
        if (!list.children.length) list.append(element("li", { class: "form-hint", text: "Nothing recorded yet." }));
        before = page.next;
        more.hidden = !page.next;
    };
    more.addEventListener("click", () => load().catch((error) => announce(error.message)));
    return load();
}

/**
 * The account menu behind the user name in the header (AUTH-01): Account, Admin and Sign out. Follows the menu button
 * pattern: Enter, Space or Down open it on the first entry, Up on the last; arrows, Home and End move; Escape closes it and
 * returns to the button; Tab or a click elsewhere closes it.
 */
export function setUpUserMenu() {
    const button = $("user-menu-button");
    const list = $("user-menu-list");
    const items = () => [...list.querySelectorAll("[role=menuitem]")].filter((item) => !item.closest("li").hidden);
    const open = (focus = "first") => {
        list.hidden = false;
        button.setAttribute("aria-expanded", "true");
        const entries = items();
        (focus === "last" ? entries.at(-1) : entries[0])?.focus();
    };
    const close = (returnFocus = true) => {
        if (list.hidden) return;
        list.hidden = true;
        button.setAttribute("aria-expanded", "false");
        if (returnFocus) button.focus();
    };
    button.addEventListener("click", () => (list.hidden ? open() : close()));
    button.addEventListener("keydown", (event) => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            open(event.key === "ArrowUp" ? "last" : "first");
        }
    });
    list.addEventListener("keydown", (event) => {
        const entries = items();
        const index = entries.indexOf(document.activeElement);
        const next = { ArrowDown: index + 1, ArrowUp: index - 1, Home: 0, End: entries.length - 1 }[event.key];
        if (next !== undefined) {
            event.preventDefault();
            entries[(next + entries.length) % entries.length].focus();
        } else if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            close();
        } else if (event.key === "Tab") {
            close(false);
        }
    });
    document.addEventListener("pointerdown", (event) => {
        if (!list.hidden && !event.target.closest(".user-menu")) close(false);
    });
    $("sign-out").addEventListener("click", async () => {
        try {
            await fetch(`${api}/session`, { method: "DELETE" });
        } finally {
            location.href = "/login";
        }
    });
}
