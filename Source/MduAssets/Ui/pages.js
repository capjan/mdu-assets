// Project settings, new project, account and admin pages.
import {
    $, announce, clearFormError, copyText, dateFormat, element, flashCopied, formatSize, handleSubmit, numberFormat, plural, request, send,
    shortDateFormat, showFormError,
} from "./common.js";

const roles = [["viewer", "Viewer"], ["editor", "Editor"], ["owner", "Owner"]];
const projectPath = (project) => `/projects/${encodeURIComponent(project.slug)}`;

// Project settings: name, description, members and deletion (owners and admins).
export async function showSettings(project, me) {
    document.title = `Settings of ${project.name} – MDU Assets`;
    const back = `/?project=${encodeURIComponent(project.slug)}`;
    $("settings-back").href = back;
    $("settings-title").textContent = `Settings of ${project.name}`;
    $("settings-slug").textContent = `Short name ${project.slug}`;

    const details = $("details-form");
    $("details-name").value = project.name;
    $("details-description").value = project.description;
    handleSubmit(details, async () => {
        clearFormError(details);
        const updated = await send(projectPath(project), "PATCH", { name: $("details-name").value, description: $("details-description").value });
        project.name = updated.name;
        project.description = updated.description;
        $("settings-title").textContent = `Settings of ${updated.name}`;
        document.title = `Settings of ${updated.name} – MDU Assets`;
        for (const link of document.querySelectorAll(".project-link[aria-current=page] span:first-child")) link.textContent = updated.name;
        announce("Saved the name and description.");
    });

    const renderMembers = (members) => {
        const owners = members.filter((m) => m.role === "owner").length;
        $("member-rows").replaceChildren(...members.map((member) => {
            const lastOwner = member.role === "owner" && owners === 1;
            const select = element("select", { "aria-label": `Role of ${member.userName}`, disabled: lastOwner },
                roles.map(([value, label]) => element("option", { value, selected: value === member.role, text: label })));
            select.addEventListener("change", async () => {
                try {
                    renderMembers((await send(`${projectPath(project)}/members/${encodeURIComponent(member.userName)}`, "PUT", { role: select.value })).items);
                    $("member-rows").querySelector(`select[aria-label="Role of ${CSS.escape(member.userName)}"]`)?.focus();
                    announce(`${member.userName} is now ${select.value}.`);
                } catch (error) {
                    select.value = member.role;
                    announce(error.message);
                }
            });
            const remove = element("button", {
                class: "secondary-button", type: "button", disabled: lastOwner, text: "Remove",
                "aria-label": `Remove ${member.userName}`,
                title: lastOwner ? "A project needs at least one owner." : null,
            });
            remove.addEventListener("click", async () => {
                try {
                    renderMembers((await send(`${projectPath(project)}/members/${encodeURIComponent(member.userName)}`, "DELETE")).items);
                    $("member-name").focus();
                    announce(`Removed ${member.userName}.`);
                } catch (error) {
                    announce(error.message);
                }
            });
            const name = element("td", {}, [member.userName]);
            if (member.isLocked) name.append(element("span", { class: "badge badge-warning", text: "Locked" }));
            if (member.userName.toLowerCase() === me.name.toLowerCase()) name.append(element("span", { class: "badge", text: "You" }));
            return element("tr", {}, [name, element("td", {}, [select]), element("td", {}, [element("div", { class: "row-actions" }, [remove])])]);
        }));
    };
    renderMembers((await request(`${projectPath(project)}/members`)).items);

    const memberForm = $("member-form");
    handleSubmit(memberForm, async () => {
        clearFormError(memberForm);
        const name = $("member-name").value.trim();
        renderMembers((await send(`${projectPath(project)}/members/${encodeURIComponent(name)}`, "PUT", { role: $("member-role").value })).items);
        $("member-name").value = "";
        $("member-name").focus();
        announce(`Added ${name} as ${$("member-role").value}.`);
    });

    const empty = project.assetCount === 0;
    $("delete-hint").textContent = empty
        ? "The project has no images. Deleting it removes its members as well and cannot be undone."
        : `The project has ${plural(project.assetCount, "image", "images")}. Only empty projects can be deleted for now; deleting a project with its images arrives with the trash.`;
    const confirm = $("delete-confirm");
    confirm.disabled = !empty;
    $("delete-button").disabled = true;
    confirm.addEventListener("input", () => { $("delete-button").disabled = confirm.value.trim() !== project.slug; });
    const deleteForm = $("delete-form");
    handleSubmit(deleteForm, async () => {
        clearFormError(deleteForm);
        await send(projectPath(project), "DELETE");
        location.href = "/";
    });
}

// New project (admins, or everybody when an admin allows it).
export function showNewProject() {
    document.title = "New project – MDU Assets";
    const name = $("new-project-name");
    const slug = $("new-project-slug");
    let slugEdited = false;
    // The short name follows the name until it is edited by hand.
    name.addEventListener("input", () => {
        if (!slugEdited)
            slug.value = name.value.toLowerCase().normalize("NFKD").replace(/[̀-ͯ]/g, "")
                .replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 64).replace(/-+$/, "");
    });
    slug.addEventListener("input", () => { slugEdited = true; });

    const form = $("new-project-form");
    handleSubmit(form, async () => {
        clearFormError(form);
        const project = await send("/projects", "POST", { slug: slug.value.trim(), name: name.value, description: $("new-project-description").value });
        location.href = `/?project=${encodeURIComponent(project.slug)}`;
    });
    name.focus();
}

// Account: change the password; with a start password this is the only page (AUTH-05).
export function showAccount(me, { forced = false } = {}) {
    document.title = "Account – MDU Assets";
    $("account-summary").textContent = `Signed in as ${me.name}${me.isAdmin ? ", admin" : ""}.`;
    $("start-password-notice").hidden = !forced;
    $("password-user").value = me.name;
    if (forced) {
        // Nothing else works until the password is changed, so the navigation is hidden.
        $("new-project-link").hidden = true;
        $("admin-link").hidden = true;
        document.querySelector(".sidebar").hidden = true;
        document.querySelector(".layout").classList.add("layout-single");
        $("password-current").focus();
    }

    const form = $("password-form");
    handleSubmit(form, async () => {
        clearFormError(form);
        const next = $("password-new").value;
        if (next !== $("password-repeat").value) throw new Error("The new passwords do not match.");
        await send("/me/password", "POST", { currentPassword: $("password-current").value, newPassword: next });
        form.hidden = true;
        $("start-password-notice").hidden = true;
        $("password-done").hidden = false;
        $("password-done").focus();
        announce("Password changed.");
    });
}

// Admin: overview, users and settings (AUTH-04, OPS-04).
export async function showAdmin(me) {
    document.title = "Admin – MDU Assets";
    const overview = await request("/admin/overview");

    const stat = (value, label) => element("li", {}, [element("span", { class: "stat-value", text: value }), element("span", { class: "stat-label", text: label })]);
    $("admin-stats").replaceChildren(
        stat(numberFormat.format(overview.users), overview.users === 1 ? "User" : "Users"),
        stat(numberFormat.format(overview.projects), overview.projects === 1 ? "Project" : "Projects"),
        stat(numberFormat.format(overview.assets), overview.assets === 1 ? "Image" : "Images"),
        stat(formatSize(overview.storageUsed), "Storage used"));

    $("usage-rows").replaceChildren(...overview.projectUsage.map((project) => element("tr", {}, [
        element("td", {}, [element("a", { href: `/?project=${encodeURIComponent(project.slug)}`, text: project.name })]),
        element("td", { text: numberFormat.format(project.assetCount) }),
        element("td", { text: formatSize(project.totalSize) }),
        element("td", { text: project.lastUpload ? dateFormat.format(new Date(project.lastUpload)) : "–" }),
    ])));

    const setting = $("setting-create-projects");
    setting.checked = overview.settings.usersCanCreateProjects;
    setting.addEventListener("change", async () => {
        try {
            const settings = await send("/settings", "PATCH", { usersCanCreateProjects: setting.checked });
            setting.checked = settings.usersCanCreateProjects;
            announce(setting.checked ? "All users may create projects." : "Only admins create projects.");
        } catch (error) {
            setting.checked = !setting.checked;
            announce(error.message);
        }
    });

    const info = [
        ["Web interface", overview.server.uiBaseUrl],
        ["Public links", overview.server.publicBaseUrl],
        ["Largest upload", formatSize(overview.server.maxUploadBytes)],
        ["Largest image", `${numberFormat.format(overview.server.maxPixels)} pixels`],
        ["Data directory", overview.server.dataDirectory],
    ];
    $("server-info").replaceChildren(...info.flatMap(([term, value]) => [element("dt", { text: term }), element("dd", { text: value })]));

    const showStartPassword = (user, password, reset) => {
        $("start-password-text").textContent = reset
            ? `New start password for ${user.name}. It is shown only now; ${user.name} has to change it at the next sign-in.`
            : `Created ${user.name}. Give them this start password; it is shown only now and has to be changed at the first sign-in.`;
        $("start-password-value").textContent = password;
        $("start-password").hidden = false;
        $("start-password").focus();
    };
    $("start-password-copy").addEventListener("click", async () => {
        const ok = await copyText($("start-password-value").textContent);
        flashCopied($("start-password-copy"), ok);
        announce(ok ? "Copied to the clipboard." : "Copying failed. Select the text and copy it.");
    });

    const renderUsers = (users) => {
        $("user-rows").replaceChildren(...users.map((user) => {
            const name = element("td", {}, [user.name]);
            const isMe = user.name.toLowerCase() === me.name.toLowerCase();
            if (isMe) name.append(element("span", { class: "badge", text: "You" }));
            const status = [user.isAdmin ? "Admin" : "User"];
            if (user.isLocked) status.push("locked");
            if (user.mustChangePassword) status.push("start password");

            const update = async (changes, message) => {
                try {
                    await send(`/users/${encodeURIComponent(user.name)}`, "PATCH", changes);
                    renderUsers((await request("/users")).items);
                    announce(message);
                } catch (error) {
                    announce(error.message);
                    showFormError($("user-form"), error.message);
                }
            };
            const adminButton = element("button", {
                class: "secondary-button", type: "button", text: user.isAdmin ? "Remove admin" : "Make admin",
                "aria-label": `${user.isAdmin ? "Remove admin role from" : "Make admin:"} ${user.name}`,
            });
            adminButton.addEventListener("click", () => update({ isAdmin: !user.isAdmin },
                user.isAdmin ? `${user.name} is no admin any more.` : `${user.name} is an admin now.`));
            const lockButton = element("button", {
                class: "secondary-button", type: "button", text: user.isLocked ? "Unlock" : "Lock",
                "aria-label": `${user.isLocked ? "Unlock" : "Lock"} ${user.name}`,
            });
            lockButton.addEventListener("click", () => update({ isLocked: !user.isLocked },
                user.isLocked ? `Unlocked ${user.name}.` : `Locked ${user.name}.`));
            const resetButton = element("button", {
                class: "secondary-button", type: "button", text: "Reset password", "aria-label": `Reset the password of ${user.name}`,
            });
            resetButton.addEventListener("click", async () => {
                try {
                    const result = await send(`/users/${encodeURIComponent(user.name)}/password-reset`, "POST");
                    renderUsers((await request("/users")).items);
                    showStartPassword(result.user, result.startPassword, true);
                } catch (error) {
                    announce(error.message);
                }
            });
            // Admins do not demote, lock or reset themselves here: the page would lose its own access. Another admin can.
            const actions = isMe ? [] : [adminButton, lockButton, resetButton];
            return element("tr", {}, [
                name,
                element("td", { text: status.join(", ") }),
                element("td", { text: shortDateFormat.format(new Date(user.createdAt)) }),
                element("td", {}, [element("div", { class: "row-actions" }, actions)]),
            ]);
        }));
    };
    renderUsers((await request("/users")).items);

    const form = $("user-form");
    handleSubmit(form, async () => {
        clearFormError(form);
        const result = await send("/users", "POST", { name: $("user-new-name").value.trim(), isAdmin: $("user-new-admin").checked });
        $("user-new-name").value = "";
        $("user-new-admin").checked = false;
        renderUsers((await request("/users")).items);
        showStartPassword(result.user, result.startPassword, false);
    });
}
