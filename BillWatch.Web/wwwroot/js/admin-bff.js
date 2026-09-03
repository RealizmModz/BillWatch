let antiforgeryToken = null;
let accessKeyLabelObserver = null;

async function readSafeError(response) {
    const contentType =
        response.headers.get("content-type") ?? "";

    if (contentType.includes("application/json")) {
        try {
            const payload = await response.clone().json();

            if (typeof payload?.detail === "string" &&
                payload.detail.trim()) {
                return payload.detail.trim();
            }

            if (typeof payload?.message === "string" &&
                payload.message.trim()) {
                return payload.message.trim();
            }

            if (typeof payload?.title === "string" &&
                payload.title.trim()) {
                return payload.title.trim();
            }
        } catch {
        }
    }

    return `BillWatch request failed with status ${response.status}.`;
}

async function handleAdminResponse(response) {
    if (response.status === 401) {
        window.location.assign("/login");
        throw new Error("BillWatch session expired.");
    }

    if (response.status === 403) {
        return {
            accessDenied: true,
            value: null
        };
    }

    if (!response.ok) {
        throw new Error(
            await readSafeError(response));
    }

    if (response.status === 204) {
        return {
            accessDenied: false,
            value: null
        };
    }

    const contentType =
        response.headers.get("content-type") ?? "";

    return {
        accessDenied: false,
        value: contentType.includes("application/json")
            ? await response.json()
            : null
    };
}

async function getAdminJson(url) {
    const response = await fetch(
        url,
        {
            method: "GET",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json"
            },
            cache: "no-store"
        });

    return await handleAdminResponse(response);
}

async function getAntiforgeryToken() {
    if (antiforgeryToken) {
        return antiforgeryToken;
    }

    const response = await fetch(
        "/bff/antiforgery",
        {
            method: "GET",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json"
            },
            cache: "no-store"
        });

    if (response.status === 401) {
        window.location.assign("/login");
        throw new Error("BillWatch session expired.");
    }

    if (!response.ok) {
        throw new Error(
            await readSafeError(response));
    }

    const payload = await response.json();

    if (!payload?.requestToken) {
        throw new Error(
            "BillWatch could not establish a secure request token.");
    }

    antiforgeryToken = payload.requestToken;
    return antiforgeryToken;
}

async function mutateAdminJson(
    url,
    method,
    body = null) {

    const requestToken =
        await getAntiforgeryToken();

    const headers = {
        "Accept": "application/json",
        "X-CSRF-TOKEN": requestToken
    };

    if (body !== null) {
        headers["Content-Type"] =
            "application/json";
    }

    const response = await fetch(
        url,
        {
            method,
            credentials: "same-origin",
            headers,
            body: body === null
                ? null
                : JSON.stringify(body),
            cache: "no-store"
        });

    return await handleAdminResponse(response);
}

function requireIdentifier(
    value,
    label) {

    if (typeof value !== "string" ||
        !value.trim()) {
        throw new Error(`${label} is required.`);
    }

    return encodeURIComponent(
        value.trim());
}

function ensureAccessKeyLabelField() {
    const builder = document.querySelector(".admin-key-builder");

    if (!builder || document.getElementById("key-label")) {
        return;
    }

    const field = document.createElement("div");
    field.className = "admin-field";
    field.innerHTML = `
        <label for="key-label">Lifetime key label</label>
        <input id="key-label"
               type="text"
               maxlength="120"
               autocomplete="off"
               placeholder="Example: Adam personal lifetime key" />
        <small>Optional. Stored only for lifetime access keys.</small>`;

    const action = builder.querySelector(".admin-key-builder-action");

    if (action) {
        builder.insertBefore(field, action);
    } else {
        builder.appendChild(field);
    }
}

function startAccessKeyLabelUi() {
    ensureAccessKeyLabelField();

    if (accessKeyLabelObserver) {
        return;
    }

    accessKeyLabelObserver = new MutationObserver(() => {
        ensureAccessKeyLabelField();
    });

    accessKeyLabelObserver.observe(
        document.body,
        {
            childList: true,
            subtree: true
        });
}

export async function getAdminUsers(
    skip = 0,
    take = 50) {

    const safeSkip = Math.max(
        Number(skip) || 0,
        0);

    const safeTake = Math.min(
        Math.max(
            Number(take) || 50,
            1),
        100);

    return await getAdminJson(
        `/bff/admin/users?skip=${safeSkip}&take=${safeTake}`);
}

export async function getAdminAccessKeys(
    skip = 0,
    take = 50) {

    const safeSkip = Math.max(
        Number(skip) || 0,
        0);

    const safeTake = Math.min(
        Math.max(
            Number(take) || 50,
            1),
        100);

    const result = await getAdminJson(
        `/bff/admin/access-keys?skip=${safeSkip}&take=${safeTake}`);

    if (!result?.accessDenied && Array.isArray(result?.value?.items)) {
        for (const item of result.value.items) {
            if (typeof item?.label === "string" && item.label.trim()) {
                item.purpose = `${item.purpose} · ${item.label.trim()}`;
            }
        }
    }

    return result;
}

export async function getAdminAuditLog(
    skip = 0,
    take = 50) {

    const safeSkip = Math.max(
        Number(skip) || 0,
        0);

    const safeTake = Math.min(
        Math.max(
            Number(take) || 50,
            1),
        100);

    return await getAdminJson(
        `/bff/admin/audit-log?skip=${safeSkip}&take=${safeTake}`);
}

export async function assignAdminRole(
    userId,
    roleName) {

    return await mutateAdminJson(
        `/bff/admin/users/${requireIdentifier(userId, "User ID")}/roles/${requireIdentifier(roleName, "Role")}`,
        "POST");
}

export async function removeAdminRole(
    userId,
    roleName) {

    return await mutateAdminJson(
        `/bff/admin/users/${requireIdentifier(userId, "User ID")}/roles/${requireIdentifier(roleName, "Role")}`,
        "DELETE");
}

export async function grantAdminEntitlement(
    userId,
    request) {

    if (!request) {
        throw new Error(
            "Subscription grant settings are required.");
    }

    return await mutateAdminJson(
        `/bff/admin/users/${requireIdentifier(userId, "User ID")}/entitlements`,
        "POST",
        request);
}

export async function setAdminProgram(
    userId,
    programName,
    request) {

    if (!request) {
        throw new Error(
            "Program membership settings are required.");
    }

    return await mutateAdminJson(
        `/bff/admin/users/${requireIdentifier(userId, "User ID")}/programs/${requireIdentifier(programName, "Program")}`,
        "PUT",
        request);
}

export async function createAdminAccessKey(request) {
    if (!request) {
        throw new Error(
            "Access-key settings are required.");
    }

    const payload = {
        ...request
    };

    if (payload.grantsLifetimeAccess) {
        const labelInput = document.getElementById("key-label");
        const label = labelInput?.value?.trim() ?? "";

        if (label.length > 120) {
            throw new Error(
                "Lifetime key labels must be 120 characters or fewer.");
        }

        payload.label = label || null;
    } else {
        payload.label = null;
    }

    const result = await mutateAdminJson(
        "/bff/admin/access-keys",
        "POST",
        payload);

    if (!result?.accessDenied) {
        const labelInput = document.getElementById("key-label");

        if (labelInput) {
            labelInput.value = "";
        }
    }

    return result;
}

export async function revokeAdminAccessKey(accessKeyId) {
    if (!accessKeyId) {
        throw new Error(
            "Access-key ID is required.");
    }

    return await mutateAdminJson(
        `/bff/admin/access-keys/${encodeURIComponent(accessKeyId)}/revoke`,
        "POST");
}

export async function copyTextToClipboard(value) {
    if (typeof value !== "string" ||
        !value.trim()) {
        return false;
    }

    if (!navigator.clipboard?.writeText) {
        return false;
    }

    await navigator.clipboard.writeText(value);
    return true;
}

startAccessKeyLabelUi();
