let antiforgeryToken = null;

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

    return await getAdminJson(
        `/bff/admin/access-keys?skip=${safeSkip}&take=${safeTake}`);
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

export async function createAdminAccessKey(request) {
    if (!request) {
        throw new Error(
            "Access-key settings are required.");
    }

    return await mutateAdminJson(
        "/bff/admin/access-keys",
        "POST",
        request);
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
