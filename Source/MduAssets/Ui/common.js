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

export async function request(path, options = {}) {
    const response = await fetch(api + path, { ...options, headers: { Accept: "application/json", ...options.headers } });
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
