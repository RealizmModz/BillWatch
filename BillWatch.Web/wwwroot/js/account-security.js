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

async function getJson(path, fallback) {
    const response = await fetch(
        path,
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!response.ok) {
        throw new Error(await readError(response, fallback));
    }

    return await response.json();
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

export function getAccountSecurity() {
    return getJson(
        "/bff/account/security",
        "BillWatch could not load account security settings.");
}

export function getExternalIdentityStatus() {
    return getJson(
        "/bff/account/external",
        "BillWatch could not load linked sign-in methods.");
}

function createStatusGlyph(text) {
    const glyph = document.createElement("span");
    glyph.setAttribute("aria-hidden", "true");
    glyph.textContent = text;
    return glyph;
}

function setExternalProviderLinkState(provider, isLinked) {
    const link = document.querySelector(
        `a[data-external-provider="${provider}"], a[href="/auth/external/${provider}/link"]`);

    if (!(link instanceof HTMLAnchorElement)) {
        return;
    }

    link.dataset.externalProvider = provider;

    if (isLinked) {
        link.dataset.externalLinked = "true";
        link.setAttribute("aria-disabled", "true");
        link.removeAttribute("href");
        link.replaceChildren(
            document.createTextNode("Linked "),
            createStatusGlyph("✓"));
        return;
    }

    if (link.dataset.externalLinked !== "true") {
        return;
    }

    const displayName =
        provider === "google"
            ? "Google"
            : provider === "apple"
                ? "Apple"
                : "Microsoft";

    link.dataset.externalLinked = "false";
    link.removeAttribute("aria-disabled");
    link.setAttribute("href", `/auth/external/${provider}/link`);
    link.replaceChildren(
        document.createTextNode(`Link ${displayName} `),
        createStatusGlyph("→"));
}

export async function refreshExternalIdentityStatusUi() {
    const status = await getExternalIdentityStatus();
    const linkedProviders = new Set(
        Array.isArray(status?.linkedProviders)
            ? status.linkedProviders
                .map(item => item?.provider)
                .filter(provider => typeof provider === "string")
                .map(provider => provider.toLowerCase())
            : []);

    for (const provider of ["google", "apple", "microsoft"]) {
        setExternalProviderLinkState(
            provider,
            linkedProviders.has(provider));
    }

    return status;
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

export async function linkExternalIdentity(provider, currentPassword, twoFactorCode) {
    const result = await postJson(
        "/bff/account/external/link",
        {
            provider,
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "BillWatch could not link this sign-in method. Start the provider link again and try again.");

    await refreshExternalIdentityStatusUi();
    return result;
}

export function clearExternalLinkQuery() {
    const url = new URL(window.location.href);
    url.searchParams.delete("externalLink");
    url.searchParams.delete("externalError");

    const nextUrl = url.pathname + url.search + url.hash;
    window.history.replaceState(window.history.state, "", nextUrl);
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

void refreshExternalIdentityStatusUi().catch(() => {
    // Fail closed: if status cannot be loaded, keep the existing Link actions.
});
