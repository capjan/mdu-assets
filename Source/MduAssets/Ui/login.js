// The sign-in page (AUTH-01): posts name and password to /api/v1/session, which sets the session cookie, and goes back to
// the page that sent the browser here.
const $ = (id) => document.getElementById(id);

// Only addresses on this server, so a crafted link cannot send the browser elsewhere after signing in.
function target() {
    const value = new URLSearchParams(location.search).get("return") ?? "/";
    return value.startsWith("/") && !value.startsWith("//") && !value.startsWith("/\\") ? value : "/";
}

function showError(message) {
    const error = $("sign-in-error");
    error.textContent = message;
    error.hidden = false;
}

$("sign-in-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const name = $("sign-in-name").value.trim();
    const password = $("sign-in-password").value;
    if (!name || !password) {
        showError("Enter your user name and password.");
        (name ? $("sign-in-password") : $("sign-in-name")).focus();
        return;
    }

    const button = $("sign-in-button");
    button.disabled = true;
    try {
        const response = await fetch("/api/v1/session", {
            method: "POST",
            headers: { "Content-Type": "application/json", Accept: "application/json" },
            body: JSON.stringify({ name, password, remember: $("sign-in-remember").checked }),
        });
        if (response.ok) {
            location.replace(target());
            return;
        }
        const problem = response.headers.get("Content-Type")?.includes("json") ? await response.json() : null;
        showError(problem?.detail ?? `Signing in failed (${response.status}).`);
        $("sign-in-password").select();
        $("sign-in-password").focus();
    } catch {
        showError("The server cannot be reached. Check the connection and try again.");
    } finally {
        button.disabled = false;
    }
});

$("sign-in-name").focus();
