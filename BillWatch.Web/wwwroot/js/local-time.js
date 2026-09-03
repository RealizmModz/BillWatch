const storageKey = "billwatch.timestamp-display-mode";
const localMode = "Local12Hour";
const utcMode = "Utc";

let observer = null;

function normalizeMode(value) {
    return value === utcMode
        ? utcMode
        : localMode;
}

function getStoredMode() {
    return normalizeMode(
        window.localStorage.getItem(storageKey));
}

function setStoredMode(mode) {
    window.localStorage.setItem(
        storageKey,
        normalizeMode(mode));
}

function parseDate(value) {
    if (!value) {
        return null;
    }

    const date = new Date(value);

    return Number.isNaN(date.getTime())
        ? null
        : date;
}

function pad(value) {
    return String(value).padStart(2, "0");
}

function formatUtc(date, includeTime = true) {
    const dateText =
        `${pad(date.getUTCMonth() + 1)}/${pad(date.getUTCDate())}/${date.getUTCFullYear()}`;

    if (!includeTime) {
        return `${dateText} UTC`;
    }

    return `${dateText} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

function formatLocal(date, includeTime = true) {
    const dateText =
        `${pad(date.getMonth() + 1)}/${pad(date.getDate())}/${date.getFullYear()}`;

    if (!includeTime) {
        return dateText;
    }

    const hours = date.getHours();
    const displayHour = hours % 12 || 12;
    const period = hours >= 12 ? "PM" : "AM";

    return `${dateText} ${displayHour}:${pad(date.getMinutes())} ${period}`;
}

function formatByMode(value, includeTime = true) {
    const date = parseDate(value);

    if (!date) {
        return null;
    }

    return getStoredMode() === utcMode
        ? formatUtc(date, includeTime)
        : formatLocal(date, includeTime);
}

function renderTimestampElement(element) {
    const value = element.getAttribute("datetime") ??
        element.dataset.bwTimestamp;
    const formatted = formatByMode(value, true);

    if (formatted) {
        element.textContent = formatted;
    }
}

export function renderTimestamps(root = document) {
    if (root instanceof Element &&
        (root.matches("time[datetime]") ||
         root.matches("[data-bw-timestamp]"))) {
        renderTimestampElement(root);
    }

    root.querySelectorAll?.("time[datetime], [data-bw-timestamp]")
        .forEach(renderTimestampElement);
}

export async function getTimestampPreference() {
    const response = await fetch(
        "/bff/account/preferences",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!response.ok) {
        throw new Error("Could not load timestamp preference.");
    }

    return await response.json();
}

export async function saveTimestampPreference(mode) {
    const normalizedMode = normalizeMode(mode);

    const antiforgeryResponse = await fetch(
        "/bff/antiforgery",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!antiforgeryResponse.ok) {
        throw new Error("Could not initialize secure preference update.");
    }

    const antiforgery = await antiforgeryResponse.json();

    const response = await fetch(
        "/bff/account/preferences",
        {
            method: "PUT",
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": antiforgery.requestToken
            },
            body: JSON.stringify({
                timestampDisplayMode: normalizedMode
            })
        });

    if (!response.ok) {
        throw new Error("Could not save timestamp preference.");
    }

    const saved = await response.json();
    setStoredMode(saved.timestampDisplayMode);
    renderTimestamps(document);

    return saved;
}

export async function initializeTimestampPreferences() {
    try {
        const preference = await getTimestampPreference();
        setStoredMode(preference.timestampDisplayMode);
    }
    catch {
        if (window.localStorage.getItem(storageKey) === null) {
            setStoredMode(localMode);
        }
    }

    renderTimestamps(document);

    if (observer) {
        observer.disconnect();
    }

    observer = new MutationObserver(mutations => {
        for (const mutation of mutations) {
            mutation.addedNodes.forEach(node => {
                if (node instanceof Element) {
                    renderTimestamps(node);
                }
            });
        }
    });

    observer.observe(
        document.body,
        {
            childList: true,
            subtree: true
        });
}

export function formatLocalDate(value) {
    return formatByMode(value, false);
}

export function formatLocalDateTime(value) {
    return formatByMode(value, true);
}
