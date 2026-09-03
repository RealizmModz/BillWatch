async function getAntiforgeryToken() {
    const response = await fetch(
        "/bff/antiforgery",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!response.ok) {
        throw new Error("BillWatch could not initialize a secure account update.");
    }

    const token = await response.json();

    if (!token?.requestToken) {
        throw new Error("BillWatch could not initialize a secure account update.");
    }

    return token.requestToken;
}

async function readError(response, fallback) {
    try {
        const body = await response.json();

        if (typeof body?.title === "string" && body.title.trim()) {
            return body.title;
        }

        if (body?.errors && typeof body.errors === "object") {
            for (const value of Object.values(body.errors)) {
                if (Array.isArray(value) && typeof value[0] === "string") {
                    return value[0];
                }
            }
        }
    }
    catch {
        // Never expose an unexpected raw server response.
    }

    return fallback;
}

async function postJson(path, body, fallback) {
    const requestToken = await getAntiforgeryToken();

    const response = await fetch(
        path,
        {
            method: "POST",
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": requestToken
            },
            body: JSON.stringify(body)
        });

    if (!response.ok) {
        throw new Error(await readError(response, fallback));
    }

    if (response.status === 204) {
        return null;
    }

    const contentType = response.headers.get("content-type") ?? "";

    return contentType.includes("application/json")
        ? await response.json()
        : null;
}

export async function getAccountSecurity() {
    const response = await fetch(
        "/bff/account/security",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!response.ok) {
        throw new Error(
            await readError(
                response,
                "BillWatch could not load account security settings."));
    }

    return await response.json();
}

export function updateProfile(displayName) {
    return postJson(
        "/bff/account/security/profile",
        {
            displayName: displayName || null
        },
        "BillWatch could not update your profile.");
}

export function changePassword(currentPassword, newPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/password",
        {
            currentPassword,
            newPassword,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not change your password.");
}

export function requestEmailChange(currentPassword, newEmail, twoFactorCode) {
    return postJson(
        "/bff/account/security/email",
        {
            currentPassword,
            newEmail,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not start the email change.");
}

export function setupTwoFactor(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/setup",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not create an authenticator key.");
}

export function enableTwoFactor(currentPassword, authenticatorCode) {
    return postJson(
        "/bff/account/security/two-factor/enable",
        {
            currentPassword,
            authenticatorCode
        },
        "BillWatch could not enable two-factor authentication.");
}

export function regenerateRecoveryCodes(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/recovery-codes",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not regenerate recovery codes.");
}

export function disableTwoFactor(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/disable",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not disable two-factor authentication.");
}

export function resetTwoFactor(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/reset",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not reset two-factor authentication.");
}
