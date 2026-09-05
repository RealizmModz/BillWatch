async function getSafeErrorMessage(response) {
    const contentType =
        response.headers.get("content-type") ?? "";

    if (contentType.includes("application/json")) {
        try {
            const payload = await response.clone().json();

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

async function getAntiforgeryToken() {
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

    if (!response.ok) {
        throw new Error(
            await getSafeErrorMessage(response));
    }

    const payload = await response.json();

    if (!payload?.requestToken) {
        throw new Error(
            "BillWatch could not establish a secure request token.");
    }

    return payload.requestToken;
}

export async function deleteBillWatchAccount(
    confirmation,
    currentPassword,
    twoFactorCode) {

    if (confirmation !== "DELETE") {
        throw new Error(
            "Type DELETE to confirm permanent account deletion.");
    }

    if (!currentPassword ||
        !currentPassword.trim()) {
        throw new Error(
            "Enter your current password to confirm permanent account deletion.");
    }

    const requestToken =
        await getAntiforgeryToken();

    const response = await fetch(
        "/bff/account",
        {
            method: "DELETE",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": requestToken
            },
            body: JSON.stringify({
                confirmation,
                currentPassword,
                twoFactorCode:
                    twoFactorCode?.trim() || null
            }),
            cache: "no-store"
        });

    if (!response.ok) {
        throw new Error(
            await getSafeErrorMessage(response));
    }

    return true;
}
