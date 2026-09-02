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
        } catch {
        }
    }

    return `BillWatch request failed with status ${response.status}.`;
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

    return {
        accessDenied: false,
        value: await response.json()
    };
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
