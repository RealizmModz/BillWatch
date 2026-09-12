async function safeFetch(path, options, fallback) {
    try {
        return await fetch(path, options);
    }
    catch {
        throw new Error(fallback);
    }
}

async function getAntiforgeryToken() {
    const fallback = "BillWatch could not initialize a secure account update.";
    const response = await safeFetch(
        "/bff/antiforgery",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        },
        fallback);

    if (!response.ok) {
        throw new Error(fallback);
    }

    try {
        const token = await response.json();

        if (token?.requestToken) {
            return token.requestToken;
        }
    }
    catch {
        // Fall through to the safe generic message.
    }

    throw new Error(fallback);
}

function mapExpectedError(body, status, fallback) {
    const title = typeof body?.title === "string"
        ? body.title.trim().toLowerCase()
        : "";
    const errorCodes = body?.errors && typeof body.errors === "object"
        ? Object.keys(body.errors)
        : [];

    if (title.includes("current password") && title.includes("incorrect")) {
        return "Your current password is incorrect.";
    }

    if (title.includes("authenticator code") &&
        (title.includes("invalid") || title.includes("required"))) {
        return "That authenticator code isn’t valid. Try the current code from your authenticator app.";
    }

    if (title.includes("already in use")) {
        return "That email address is already in use.";
    }

    if (title.includes("already your account email")) {
        return "That is already your account email address.";
    }

    if (title.includes("email delivery") || status === 503) {
        return "Email delivery is temporarily unavailable. Please try again later.";
    }

    if (errorCodes.some(code => code.startsWith("Password", 0))) {
        return "Your new password doesn’t meet BillWatch’s password requirements.";
    }

    if (status === 429) {
        return "Too many attempts. Please wait a little while and try again.";
    }

    if (status === 401 || status === 403) {
        return "We couldn’t verify those credentials. Check your password and authenticator code.";
    }

    return fallback;
}

async function readSafeError(response, fallback) {
    try {
        return mapExpectedError(await response.json(), response.status, fallback);
    }
    catch {
        return response.status === 429
            ? "Too many attempts. Please wait a little while and try again."
            : fallback;
    }
}

async function getJson(path, fallback) {
    const response = await safeFetch(
        path,
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        },
        fallback);

    if (!response.ok) {
        throw new Error(await readSafeError(response, fallback));
    }

    try {
        return await response.json();
    }
    catch {
        throw new Error(fallback);
    }
}

async function postJson(path, body, fallback) {
    const requestToken = await getAntiforgeryToken();
    const response = await safeFetch(
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
        },
        fallback);

    if (!response.ok) {
        throw new Error(await readSafeError(response, fallback));
    }

    if (response.status === 204) {
        return null;
    }

    const contentType = response.headers.get("content-type") ?? "";

    if (!contentType.includes("application/json")) {
        return null;
    }

    try {
        return await response.json();
    }
    catch {
        throw new Error(fallback);
    }
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

const settingsToastObservers = new WeakMap();

function restoreSettingsToastSource() {
    const sourceToast = document.querySelector(".settings-page > .settings-toast");

    if (sourceToast instanceof HTMLElement) {
        sourceToast.removeAttribute("aria-hidden");
        delete sourceToast.dataset.billwatchToastPortaled;
    }
}

function syncSettingsToastPortal(dialog) {
    if (!(dialog instanceof HTMLDialogElement) || !dialog.open) {
        return;
    }

    const sourceToast = document.querySelector(".settings-page > .settings-toast");
    const existingPortal = dialog.querySelector(":scope > .settings-toast-portal");

    if (!(sourceToast instanceof HTMLElement)) {
        if (existingPortal instanceof HTMLElement) {
            existingPortal.remove();
        }

        return;
    }

    sourceToast.setAttribute("aria-hidden", "true");
    sourceToast.dataset.billwatchToastPortaled = "true";

    const portal = sourceToast.cloneNode(true);

    if (!(portal instanceof HTMLElement)) {
        return;
    }

    portal.classList.add("settings-toast-portal");
    portal.removeAttribute("aria-hidden");
    delete portal.dataset.billwatchToastPortaled;

    const portalCloseButton = portal.querySelector(".settings-toast-close");

    if (portalCloseButton instanceof HTMLButtonElement) {
        portalCloseButton.addEventListener("click", () => {
            const sourceCloseButton = sourceToast.querySelector(".settings-toast-close");

            if (sourceCloseButton instanceof HTMLButtonElement) {
                sourceCloseButton.click();
            }
        });
    }

    if (existingPortal instanceof HTMLElement) {
        existingPortal.replaceWith(portal);
    }
    else {
        dialog.appendChild(portal);
    }
}

function startSettingsToastPortal(dialog) {
    const settingsPage = document.querySelector(".settings-page");

    if (!(settingsPage instanceof HTMLElement)) {
        return;
    }

    const existingObserver = settingsToastObservers.get(dialog);

    if (existingObserver instanceof MutationObserver) {
        existingObserver.disconnect();
    }

    syncSettingsToastPortal(dialog);

    const observer = new MutationObserver(() => syncSettingsToastPortal(dialog));
    observer.observe(settingsPage, {
        childList: true,
        subtree: true,
        characterData: true
    });

    settingsToastObservers.set(dialog, observer);
}

function stopSettingsToastPortal(dialog) {
    const observer = settingsToastObservers.get(dialog);

    if (observer instanceof MutationObserver) {
        observer.disconnect();
        settingsToastObservers.delete(dialog);
    }

    const portal = dialog.querySelector(":scope > .settings-toast-portal");

    if (portal instanceof HTMLElement) {
        portal.remove();
    }

    restoreSettingsToastSource();
}

export function openSettingsDialog(id) {
    const dialog = document.getElementById(id);

    if (!(dialog instanceof HTMLDialogElement)) {
        return;
    }

    if (dialog.dataset.billwatchCancelBound !== "true") {
        dialog.addEventListener("cancel", event => {
            event.preventDefault();
            const cancelButton = dialog.querySelector("[data-dialog-cancel]");

            if (cancelButton instanceof HTMLElement) {
                cancelButton.click();
            }
        });

        dialog.dataset.billwatchCancelBound = "true";
    }

    if (!dialog.open) {
        dialog.showModal();
    }

    startSettingsToastPortal(dialog);
}

export function closeSettingsDialog(id) {
    const dialog = document.getElementById(id);

    if (dialog instanceof HTMLDialogElement) {
        stopSettingsToastPortal(dialog);

        if (dialog.open) {
            dialog.close();
        }
    }
}

function createStatusGlyph(text) {
    const glyph = document.createElement("span");
    glyph.setAttribute("aria-hidden", "true");
    glyph.textContent = text;
    return glyph;
}

function getExternalProviderDisplayName(provider) {
    return provider === "google"
        ? "Google"
        : provider === "apple"
            ? "Apple"
            : "Microsoft";
}

function normalizeExternalLinkSecondFactor(twoFactorCredential, explicitRecoveryCode) {
    const recoveryCode = typeof explicitRecoveryCode === "string"
        ? explicitRecoveryCode.trim()
        : "";

    if (recoveryCode) {
        return {
            twoFactorCode: null,
            twoFactorRecoveryCode: recoveryCode
        };
    }

    const credential = typeof twoFactorCredential === "string"
        ? twoFactorCredential.trim()
        : "";

    if (!credential) {
        return {
            twoFactorCode: null,
            twoFactorRecoveryCode: null
        };
    }

    const compactAuthenticatorCode = credential.replace(/[\s-]/g, "");

    if (/^\d{6}$/.test(compactAuthenticatorCode)) {
        return {
            twoFactorCode: credential,
            twoFactorRecoveryCode: null
        };
    }

    return {
        twoFactorCode: null,
        twoFactorRecoveryCode: credential
    };
}

function exposeExternalLinkRecoveryCodeFallback() {
    for (const panel of document.querySelectorAll(".authenticator-setup")) {
        const heading = panel.querySelector(".panel-kicker");

        if (heading?.textContent?.trim() !== "Confirm account link") {
            continue;
        }

        for (const label of panel.querySelectorAll("label.settings-field")) {
            const caption = label.querySelector("span");
            const input = label.querySelector("input");

            if (!(input instanceof HTMLInputElement) ||
                caption?.textContent?.trim() !== "Current authenticator code") {
                continue;
            }

            caption.textContent = "Authenticator code or recovery code";
            input.inputMode = "text";
            input.placeholder = "123456 or recovery code";
            input.removeAttribute("maxlength");
        }
    }
}

async function beginExternalIdentityUnlink(provider) {
    const displayName = getExternalProviderDisplayName(provider);

    if (!window.confirm(`Remove ${displayName} as a BillWatch sign-in method?`)) {
        return;
    }

    const currentPassword = window.prompt("Enter your current BillWatch password to continue.");

    if (!currentPassword) {
        return;
    }

    let twoFactorCode = null;
    let twoFactorRecoveryCode = null;

    try {
        const security = await getAccountSecurity();

        if (security?.twoFactorEnabled === true) {
            twoFactorCode = window.prompt(
                "Enter your current BillWatch authenticator code, or leave this blank to use a recovery code.");

            if (!twoFactorCode) {
                twoFactorRecoveryCode = window.prompt("Enter one unused BillWatch recovery code.");

                if (!twoFactorRecoveryCode) {
                    return;
                }
            }
        }

        await unlinkExternalIdentity(
            provider,
            currentPassword,
            twoFactorCode,
            twoFactorRecoveryCode);
        window.alert(`${displayName} was removed from your BillWatch sign-in methods.`);
    }
    catch {
        window.alert("BillWatch could not remove this sign-in method. Check your credentials and try again.");
    }
}

function setExternalProviderLinkState(provider, isLinked) {
    const link = document.querySelector(
        `a[data-external-provider="${provider}"], a[href="/auth/external/${provider}/link"]`);

    if (!(link instanceof HTMLAnchorElement)) {
        return;
    }

    const displayName = getExternalProviderDisplayName(provider);
    link.dataset.externalProvider = provider;

    if (isLinked) {
        link.dataset.externalLinked = "true";
        link.removeAttribute("aria-disabled");
        link.setAttribute("href", "#");
        link.replaceChildren(
            document.createTextNode(`Remove ${displayName} `),
            createStatusGlyph("×"));
        link.onclick = event => {
            event.preventDefault();
            void beginExternalIdentityUnlink(provider);
        };
        return;
    }

    link.dataset.externalLinked = "false";
    link.removeAttribute("aria-disabled");
    link.setAttribute("href", `/auth/external/${provider}/link`);
    link.onclick = null;
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

    exposeExternalLinkRecoveryCodeFallback();
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
        "We couldn’t update your password. Please try again.");
}

export function requestEmailChange(currentPassword, newEmail, twoFactorCode) {
    return postJson(
        "/bff/account/security/email",
        {
            currentPassword,
            newEmail,
            twoFactorCode: twoFactorCode || null
        },
        "We couldn’t start the email change. Please try again.");
}

export function resendVerificationEmail() {
    return postJson(
        "/bff/account/security/email/verification",
        {},
        "We couldn’t send the verification email. Please try again.");
}

export async function linkExternalIdentity(
    provider,
    currentPassword,
    twoFactorCode,
    twoFactorRecoveryCode) {
    const secondFactor = normalizeExternalLinkSecondFactor(
        twoFactorCode,
        twoFactorRecoveryCode);

    const result = await postJson(
        "/bff/account/external/link",
        {
            provider,
            currentPassword,
            twoFactorCode: secondFactor.twoFactorCode,
            twoFactorRecoveryCode: secondFactor.twoFactorRecoveryCode
        },
        "BillWatch could not link this sign-in method. Start the provider link again and try again.");

    await refreshExternalIdentityStatusUi();
    return result;
}

export async function unlinkExternalIdentity(
    provider,
    currentPassword,
    twoFactorCode,
    twoFactorRecoveryCode) {
    const result = await postJson(
        "/bff/account/external/unlink",
        {
            provider,
            currentPassword,
            twoFactorCode: twoFactorCode || null,
            twoFactorRecoveryCode: twoFactorRecoveryCode || null
        },
        "BillWatch could not remove this sign-in method.");

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
        "We couldn’t start authenticator setup. Please try again.");
}

export function enableTwoFactor(currentPassword, authenticatorCode) {
    return postJson(
        "/bff/account/security/two-factor/enable",
        {
            currentPassword,
            authenticatorCode
        },
        "We couldn’t enable two-factor authentication. Please try again.");
}

export function regenerateRecoveryCodes(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/recovery-codes",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "We couldn’t generate new recovery codes. Please try again.");
}

export function disableTwoFactor(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/disable",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "We couldn’t disable two-factor authentication. Please try again.");
}

export function resetTwoFactor(currentPassword, twoFactorCode) {
    return postJson(
        "/bff/account/security/two-factor/reset",
        {
            currentPassword,
            twoFactorCode: twoFactorCode || null
        },
        "We couldn’t replace your authenticator. Please try again.");
}

void refreshExternalIdentityStatusUi().catch(() => {
    // Fail closed: if status cannot be loaded, keep the existing Link actions.
});
