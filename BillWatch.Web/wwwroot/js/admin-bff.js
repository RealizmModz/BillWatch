let antiforgeryToken = null;
let oneTimeKeyEnhancementTimer = null;

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

function maskOneTimeAccessKey(value) {
    const normalized = value.trim();
    const parts = normalized.split("-");

    if (parts.length > 1) {
        return parts
            .map((part, index) =>
                index === 0
                    ? part
                    : "•".repeat(part.length))
            .join("-");
    }

    return "•".repeat(
        Math.max(normalized.length, 8));
}

function enhanceOneTimeAccessKeyCard() {
    const card = document.querySelector(
        ".admin-secret-card");

    if (!(card instanceof HTMLElement)) {
        return false;
    }

    const code = card.querySelector("code");
    const actions = card.querySelector(
        ".admin-secret-actions");

    if (!(code instanceof HTMLElement) ||
        !(actions instanceof HTMLElement)) {
        return false;
    }

    if (card.dataset.oneTimeKeyEnhanced === "true") {
        card.scrollIntoView({
            behavior: "smooth",
            block: "center"
        });

        return true;
    }

    const plaintext =
        code.textContent?.trim() ?? "";

    if (!plaintext) {
        return false;
    }

    const masked =
        maskOneTimeAccessKey(plaintext);

    let isRevealed = false;

    code.textContent = masked;
    code.setAttribute(
        "aria-label",
        "One-time access key hidden");

    const revealButton =
        document.createElement("button");

    revealButton.type = "button";
    revealButton.className =
        "admin-secondary-button";
    revealButton.textContent =
        "Reveal key";
    revealButton.setAttribute(
        "aria-pressed",
        "false");

    revealButton.addEventListener(
        "click",
        () => {
            isRevealed = !isRevealed;

            code.textContent = isRevealed
                ? plaintext
                : masked;

            code.setAttribute(
                "aria-label",
                isRevealed
                    ? "One-time access key revealed"
                    : "One-time access key hidden");

            revealButton.textContent = isRevealed
                ? "Hide key"
                : "Reveal key";

            revealButton.setAttribute(
                "aria-pressed",
                isRevealed
                    ? "true"
                    : "false");
        });

    actions.insertBefore(
        revealButton,
        actions.firstChild);

    const copyButton =
        Array.from(
            actions.querySelectorAll("button"))
            .find(button =>
                button.textContent?.trim() ===
                    "Copy key");

    if (copyButton instanceof HTMLButtonElement) {
        copyButton.setAttribute(
            "aria-label",
            "Copy one-time access key");
    }

    card.dataset.oneTimeKeyEnhanced = "true";

    requestAnimationFrame(
        () => card.scrollIntoView({
            behavior: "smooth",
            block: "center"
        }));

    return true;
}

function scheduleOneTimeAccessKeyEnhancement() {
    if (oneTimeKeyEnhancementTimer !== null) {
        window.clearTimeout(
            oneTimeKeyEnhancementTimer);

        oneTimeKeyEnhancementTimer = null;
    }

    let attempts = 0;

    const attemptEnhancement = () => {
        oneTimeKeyEnhancementTimer = null;

        if (enhanceOneTimeAccessKeyCard()) {
            return;
        }

        attempts++;

        if (attempts >= 60) {
            return;
        }

        oneTimeKeyEnhancementTimer =
            window.setTimeout(
                attemptEnhancement,
                100);
    };

    oneTimeKeyEnhancementTimer =
        window.setTimeout(
            attemptEnhancement,
            0);
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

    const result = await mutateAdminJson(
        "/bff/admin/access-keys",
        "POST",
        request);

    if (!result?.accessDenied &&
        typeof result?.value?.plaintextKey === "string" &&
        result.value.plaintextKey.trim()) {
        scheduleOneTimeAccessKeyEnhancement();
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
