// Project settings, new project, account and admin pages.
import {
    $, announce, clearFormError, copyText, dateFormat, element, flashCopied, formatSize, handleSubmit, numberFormat, openDialog, plural,
    request, send, shortDateFormat, showAudit, showFormError,
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
    $("details-convert").checked = project.convertFormats;
    $("details-max-versions").value = project.maxVersions ?? "";
    // Limits (OPS-01, OPS-02): the upload limit is the owners' choice, the quota the admins'.
    const megabytes = (bytes) => (bytes ? Math.round(bytes / 104857.6) / 10 : "");
    $("details-max-file").value = megabytes(project.maxFileBytes);
    $("details-max-file-hint").textContent = `0 or empty uses the limit of this server, ${formatSize(me.maxUploadBytes)}. A project can only go lower.`;
    $("details-quota").value = megabytes(project.quotaBytes);
    $("details-quota").disabled = !me.isAdmin;
    const share = project.quotaBytes ? ` of ${formatSize(project.quotaBytes)} (${Math.round((project.usedBytes / project.quotaBytes) * 100)} %)` : "";
    $("details-quota-hint").textContent = `The project uses ${formatSize(project.usedBytes)}${share}, counting old versions and the trash. ${me.isAdmin ? "0 or empty means no quota." : "Only admins change the quota."}`;

    // Export (OPS-06) and the activity of the project (OPS-03).
    const exportPath = `${location.origin}/api/v1/projects/${encodeURIComponent(project.slug)}/export`;
    $("export-current").href = exportPath;
    $("export-all").href = `${exportPath}?versions=all`;
    showAudit($("activity-list"), $("activity-more"), `/projects/${encodeURIComponent(project.slug)}/audit`).catch((error) => announce(error.message));
    handleSubmit(details, async () => {
        clearFormError(details);
        const updated = await send(projectPath(project), "PATCH", {
            name: $("details-name").value, description: $("details-description").value, convertFormats: $("details-convert").checked,
            maxVersions: Number($("details-max-versions").value || 0),
            maxFileBytes: Math.round(Number($("details-max-file").value || 0) * 1048576),
            ...(me.isAdmin ? { quotaBytes: Math.round(Number($("details-quota").value || 0) * 1048576) } : {}),
        });
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
        ? `Deleting the project removes its members and folders${project.trashCount ? ` and deletes the ${plural(project.trashCount, "image", "images")} in its trash for good` : ""}. This cannot be undone.`
        : `The project has ${plural(project.assetCount, "image", "images")} in use. Move them to the trash first; the trash is emptied with the project.`;
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
    setUpImport();
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
export function showAccount(me, { forced = false, projects = [] } = {}) {
    document.title = "Account – MDU Assets";
    $("account-summary").textContent = `Signed in as ${me.name}${me.isAdmin ? ", admin" : ""}.`;
    $("start-password-notice").hidden = !forced;
    $("tokens-panel").hidden = forced;
    if (!forced) setUpTokens(projects);
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
    showAudit($("audit-list"), $("audit-more"), "/admin/audit").catch((error) => announce(error.message));
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

// Trash (DEL-01 – DEL-03): restore with the same link, or delete for good (owners).
export async function showTrash(project, { owner }) {
    document.title = `Trash of ${project.name} – MDU Assets`;
    $("trash-back").href = `/?project=${encodeURIComponent(project.slug)}`;
    $("trash-title").textContent = `Trash of ${project.name}`;
    const path = `/projects/${encodeURIComponent(project.slug)}/trash`;

    const render = async () => {
        const trash = await request(path);
        $("trash-summary").textContent = `Images stay here for ${trash.retentionDays} days and are then deleted for good. Their links answer "410 Gone" meanwhile.`;
        $("trash-empty").hidden = trash.items.length > 0;
        $("empty-trash").hidden = !owner || trash.items.length === 0;
        $("trash-list").replaceChildren(...trash.items.map((item) => {
            const restore = element("button", { class: "primary-button", type: "button", text: "Restore", "aria-label": `Restore ${item.asset.fileName}` });
            restore.addEventListener("click", async () => {
                try {
                    await send(`/assets/${encodeURIComponent(item.asset.id)}/restore`, "POST");
                    await render();
                    ($("trash-list").querySelector("button") ?? $("trash-back")).focus();
                    announce(`Restored ${item.asset.fileName}; its link works again.`);
                } catch (error) {
                    announce(error.message);
                }
            });
            const actions = [restore];
            if (owner) {
                const purge = element("button", { class: "danger-button", type: "button", text: "Delete for good", "aria-label": `Delete ${item.asset.fileName} for good` });
                purge.addEventListener("click", async () => {
                    if (!await openDialog({
                        title: `Delete ${item.asset.fileName} for good?`,
                        text: "All its versions are deleted and cannot be restored. Its link keeps answering \"410 Gone\".",
                        confirmLabel: "Delete for good", danger: true,
                    })) return;
                    try {
                        await send(`${path}/${encodeURIComponent(item.asset.id)}`, "DELETE");
                        await render();
                        ($("trash-list").querySelector("button") ?? $("trash-back")).focus();
                        announce(`Deleted ${item.asset.fileName} for good.`);
                    } catch (error) {
                        announce(error.message);
                    }
                });
                actions.push(purge);
            }
            return element("li", { class: "trash-item" }, [
                element("img", { src: `${location.origin}${item.asset.thumbnailUrl}`, alt: "", loading: "lazy" }),
                element("div", { class: "version-text" }, [
                    element("strong", { text: item.asset.fileName }),
                    element("span", { text: `Deleted ${dateFormat.format(new Date(item.deletedAt))}${item.deletedBy ? ` by ${item.deletedBy}` : ""}` }),
                    element("span", { text: `Deleted for good on ${shortDateFormat.format(new Date(item.deletesAt))}` }),
                ]),
                element("div", { class: "row-actions" }, actions),
            ]);
        }));
    };

    $("empty-trash").addEventListener("click", async () => {
        if (!await openDialog({
            title: "Empty the trash?",
            text: "Every image in the trash is deleted for good with all its versions. This cannot be undone.",
            confirmLabel: "Empty trash", danger: true,
        })) return;
        try {
            const result = await send(path, "DELETE");
            await render();
            $("trash-back").focus();
            announce(`Deleted ${plural(result.deleted, "image", "images")} for good.`);
        } catch (error) {
            announce(error.message);
        }
    });
    await render();
}

// Personal API tokens and ready-made integrations (AUTH-06, API-07).
function setUpTokens(projects) {
    const projectName = (slug) => projects.find((p) => p.slug === slug)?.name ?? slug;
    $("token-project").append(...projects.map((p) => element("option", { value: p.slug, text: p.name })));

    const render = async () => {
        const tokens = (await request("/me/tokens")).items;
        $("token-table").hidden = tokens.length === 0;
        $("token-rows").replaceChildren(...tokens.map((token) => {
            const revoke = element("button", { class: "secondary-button", type: "button", text: "Revoke", "aria-label": `Revoke ${token.name}` });
            revoke.addEventListener("click", async () => {
                if (!await openDialog({
                    title: `Revoke ${token.name}?`,
                    text: "Tools that use this token stop working at once. This cannot be undone.",
                    confirmLabel: "Revoke token", danger: true,
                })) return;
                try {
                    await send(`/me/tokens/${token.id}`, "DELETE");
                    await render();
                    $("token-name").focus();
                    announce(`Revoked ${token.name}.`);
                } catch (error) {
                    announce(error.message);
                }
            });
            const expired = token.expiresAt && Date.parse(token.expiresAt) <= Date.now();
            return element("tr", {}, [
                element("td", {}, [token.name, element("span", { class: "badge", text: `${token.prefix}…` })]),
                element("td", { text: `${token.scope === "read" ? "Read only" : "Upload and read"}, ${token.project ? projectName(token.project) : "all projects"}` }),
                element("td", { text: token.expiresAt ? `${expired ? "Expired " : ""}${shortDateFormat.format(new Date(token.expiresAt))}` : "Never" }),
                element("td", { text: token.lastUsedAt ? dateFormat.format(new Date(token.lastUsedAt)) : "Not yet" }),
                element("td", {}, [element("div", { class: "row-actions" }, [revoke])]),
            ]);
        }));
    };

    let secret = "";
    const integrations = () => {
        const project = $("integration-project").value;
        const uploadUrl = `${location.origin}/api/v1/projects/${encodeURIComponent(project)}/assets`;
        $("integration-curl").textContent =
            `curl -sS -H "Authorization: Bearer ${secret}" -H "Content-Type: image/png" --data-binary @screenshot.png "${uploadUrl}?name=screenshot.png" | sed -n 's/.*"markdown":"\\([^"]*\\)".*/\\1/p'`;
        // ShareX custom uploader: multipart upload, the Markdown snippet goes to the clipboard.
        const sharex = {
            Version: "16.0.0",
            Name: `MDU Assets – ${projectName(project)}`,
            DestinationType: "ImageUploader",
            RequestMethod: "POST",
            RequestURL: uploadUrl,
            Headers: { Authorization: `Bearer ${secret}`, Accept: "application/json" },
            Body: "MultipartFormData",
            FileFormName: "file",
            URL: "{json:markdown}",
            ThumbnailURL: "{json:url}",
            ErrorMessage: "{json:detail}",
        };
        const link = $("integration-sharex");
        URL.revokeObjectURL(link.href);
        link.href = URL.createObjectURL(new Blob([JSON.stringify(sharex, null, 2)], { type: "application/json" }));
        link.download = `mdu-assets-${project}.sxcu`;
    };
    $("integration-project").addEventListener("change", integrations);

    const form = $("token-form");
    handleSubmit(form, async () => {
        clearFormError(form);
        const project = $("token-project").value;
        const created = await send("/me/tokens", "POST", {
            name: $("token-name").value, project: project || null, scope: $("token-scope").value, expires: $("token-expires").value,
        });
        secret = created.secret;
        $("token-secret").textContent = secret;
        const choices = project ? projects.filter((p) => p.slug === project) : projects.filter((p) => ["editor", "owner"].includes(p.role));
        $("integration-project").replaceChildren(...choices.map((p) => element("option", { value: p.slug, text: p.name })));
        $("integration-project").closest(".field").hidden = choices.length < 2;
        const uploads = created.token.scope === "write" && choices.length > 0;
        for (const id of ["integration-curl", "integration-sharex"]) $(id).closest(".snippet-output, .form-actions").hidden = !uploads;
        if (uploads) integrations();
        $("token-name").value = "";
        $("token-created").hidden = false;
        $("token-created").focus();
        await render();
        announce(`Created ${created.token.name}. Copy the token now; it is shown only once.`);
    });
    for (const [button, source] of [["token-copy", "token-secret"], ["integration-curl-copy", "integration-curl"]]) {
        $(button).addEventListener("click", async () => {
            const ok = await copyText($(source).textContent);
            flashCopied($(button), ok);
            announce(ok ? "Copied to the clipboard." : "Copying failed. Select the text and copy it.");
        });
    }
    render();
}

// Import of a project export (OPS-06); ids already used here change, and the result lists them (ADR-0018).
function setUpImport() {
    const form = $("import-form");
    handleSubmit(form, async () => {
        clearFormError(form);
        const file = $("import-file").files[0];
        const slug = $("import-slug").value.trim();
        announce(`Importing ${file.name}…`);
        const result = await request(`/projects/import?slug=${encodeURIComponent(slug)}`, {
            method: "POST", headers: { "Content-Type": "application/zip" }, body: file,
        });
        const box = $("import-result");
        const link = element("a", { href: `/?project=${encodeURIComponent(result.project.slug)}`, text: result.project.name });
        const parts = [`Imported ${plural(result.assets, "image", "images")} into `, link, "."];
        if (result.changedIds.length) {
            parts.push(` ${plural(result.changedIds.length, "image has", "images have")} a new id, because this server used the old one; update links to them:`);
            box.replaceChildren(element("p", {}, parts), element("ul", {}, result.changedIds.map((change) =>
                element("li", {}, [element("code", { text: `/i/${change.oldId}/` }), " → ", element("code", { text: `/i/${change.newId}/` })]))));
        } else {
            parts.push(" All image links keep their ids.");
            box.replaceChildren(element("p", {}, parts));
        }
        box.hidden = false;
        box.focus();
        announce(`Imported ${result.project.name}.`);
    });
}
