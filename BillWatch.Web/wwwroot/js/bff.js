let antiforgeryToken = null;

const statementFileSizeLimit =
    15 * 1024 * 1024;

async function getSafeErrorMessage(
    response) {

    if (response.status === 413) {
        return "The selected file is too large.";
    }

    const contentType =
        response.headers.get(
            "content-type") ?? "";

    if (contentType.includes(
        "application/json")) {

        try {
            const payload =
                await response
                    .clone()
                    .json();

            if (typeof payload?.message ===
                "string" &&
                payload.message.trim()) {

                return payload
                    .message
                    .trim();
            }
        } catch {
        }
    }

    return `BillWatch request failed with status ${response.status}.`;
}

async function handleResponse(
    response) {

    if (response.status === 401) {
        window.location.assign(
            "/login");

        throw new Error(
            "BillWatch session expired.");
    }

    if (!response.ok) {
        throw new Error(
            await getSafeErrorMessage(
                response));
    }

    if (response.status === 204) {
        return null;
    }

    const contentType =
        response.headers.get(
            "content-type") ?? "";

    if (!contentType.includes(
        "application/json")) {
        return null;
    }

    return await response.json();
}

async function getJson(url) {
    const response =
        await fetch(
            url,
            {
                method:
                    "GET",

                credentials:
                    "same-origin",

                headers: {
                    "Accept":
                        "application/json"
                },

                cache:
                    "no-store"
            });

    return await handleResponse(
        response);
}

async function getAntiforgeryToken() {
    if (antiforgeryToken) {
        return antiforgeryToken;
    }

    const response =
        await fetch(
            "/bff/antiforgery",
            {
                method:
                    "GET",

                credentials:
                    "same-origin",

                headers: {
                    "Accept":
                        "application/json"
                },

                cache:
                    "no-store"
            });

    const result =
        await handleResponse(
            response);

    if (!result?.requestToken) {
        throw new Error(
            "BillWatch could not establish a secure request token.");
    }

    antiforgeryToken =
        result.requestToken;

    return antiforgeryToken;
}

async function mutateJson(
    url,
    method) {

    const requestToken =
        await getAntiforgeryToken();

    const response =
        await fetch(
            url,
            {
                method,

                credentials:
                    "same-origin",

                headers: {
                    "Accept":
                        "application/json",

                    "X-CSRF-TOKEN":
                        requestToken
                },

                cache:
                    "no-store"
            });

    return await handleResponse(
        response);
}

function setConnectStatus(
    element,
    message,
    state = "") {

    if (!element) {
        return;
    }

    element.textContent =
        message;

    element.dataset.state =
        state;
}

function delay(milliseconds) {
    return new Promise(
        resolve =>
            window.setTimeout(
                resolve,
                milliseconds));
}

function isTrustedPlaidHostedUrl(
    hostedLinkUrl) {

    let url;

    try {
        url =
            new URL(
                hostedLinkUrl);
    } catch {
        return false;
    }

    const host =
        url.hostname
            .toLowerCase();

    const isPlaidHost =
        host === "plaid.com" ||
        host.endsWith(
            ".plaid.com");

    return url.protocol ===
        "https:" &&
        isPlaidHost &&
        !url.username &&
        !url.password;
}

function openPlaidWindow() {
    const plaidWindow =
        window.open(
            "about:blank",
            "billwatch-plaid");

    if (!plaidWindow) {
        return null;
    }

    try {
        plaidWindow.opener =
            null;

        plaidWindow.document.title =
            "BillWatch";

        plaidWindow.document.body.innerHTML =
            "<p style=\"font-family:system-ui;padding:30px\">Preparing secure bank connection…</p>";
    } catch {
    }

    return plaidWindow;
}

async function waitForPlaidCompletion(
    sessionId,
    plaidWindow,
    statusElement,
    completedMessage,
    completedDestination) {

    const deadline =
        Date.now() +
        (10 * 60 * 1000);

    while (Date.now() <
        deadline) {

        await delay(
            2000);

        const result =
            await completePlaidLinkSession(
                sessionId);

        const state =
            String(
                result?.status ?? "")
                .toLowerCase();

        if (state ===
            "pending") {
            continue;
        }

        if (state ===
            "completed") {

            const institution =
                result?.connection
                    ?.institutionName ??
                "Your bank";

            setConnectStatus(
                statusElement,
                completedMessage(
                    institution),
                "success");

            try {
                if (!plaidWindow.closed) {
                    plaidWindow.close();
                }
            } catch {
            }

            await delay(
                900);

            window.location.assign(
                completedDestination);

            return;
        }

        if (state ===
            "exited") {
            setConnectStatus(
                statusElement,
                "Bank connection was canceled.",
                "neutral");

            return;
        }

        if (state ===
            "expired") {
            setConnectStatus(
                statusElement,
                "The secure Plaid session expired. Try again.",
                "error");

            return;
        }

        setConnectStatus(
            statusElement,
            "The bank connection could not be completed.",
            "error");

        return;
    }

    setConnectStatus(
        statusElement,
        "BillWatch stopped waiting for the Plaid session. You can try again.",
        "error");
}

export async function getBillStreams() {
    return await getJson(
        "/bff/bill-streams");
}

export async function getBillStreamDetail(
    billStreamId) {

    if (!billStreamId) {
        throw new Error(
            "Bill stream ID is required.");
    }

    return await getJson(
        `/bff/bill-streams/${encodeURIComponent(billStreamId)}`);
}

export async function getBankAccounts() {
    return await getJson(
        "/bff/bank-accounts");
}

export async function getBankConnections() {
    return await getJson(
        "/bff/bank-connections");
}

export async function getBankTransactions(
    take = 100) {

    const safeTake =
        Math.min(
            Math.max(
                Number(take) || 100,
                1),
            500);

    return await getJson(
        `/bff/bank-transactions?take=${safeTake}`);
}

export async function getAlerts(
    includeDismissed = false,
    unreadOnly = false,
    take = 100) {

    const safeTake =
        Math.min(
            Math.max(
                Number(take) || 100,
                1),
            100);

    const query =
        new URLSearchParams({
            includeDismissed:
                String(
                    Boolean(
                        includeDismissed)),

            unreadOnly:
                String(
                    Boolean(
                        unreadOnly)),

            take:
                String(
                    safeTake)
        });

    return await getJson(
        `/bff/alerts?${query.toString()}`);
}

export async function markAlertRead(
    alertId) {

    if (!alertId) {
        throw new Error(
            "Alert ID is required.");
    }

    return await mutateJson(
        `/bff/alerts/${encodeURIComponent(alertId)}/read`,
        "POST");
}

export async function dismissAlert(
    alertId) {

    if (!alertId) {
        throw new Error(
            "Alert ID is required.");
    }

    return await mutateJson(
        `/bff/alerts/${encodeURIComponent(alertId)}/dismiss`,
        "POST");
}

export async function downloadAccountExport() {
    const response =
        await fetch(
            "/bff/account/export",
            {
                method:
                    "GET",

                credentials:
                    "same-origin",

                headers: {
                    "Accept":
                        "application/json"
                },

                cache:
                    "no-store"
            });

    if (response.status === 401) {
        window.location.assign(
            "/login");

        throw new Error(
            "BillWatch session expired.");
    }

    if (!response.ok) {
        throw new Error(
            await getSafeErrorMessage(
                response));
    }

    const blob =
        await response.blob();

    const objectUrl =
        URL.createObjectURL(
            blob);

    try {
        const anchor =
            document.createElement(
                "a");

        anchor.href =
            objectUrl;

        anchor.download =
            "billwatch-data-export.json";

        anchor.rel =
            "noopener";

        document.body.appendChild(
            anchor);

        anchor.click();

        anchor.remove();
    } finally {
        window.setTimeout(
            () =>
                URL.revokeObjectURL(
                    objectUrl),
            1000);
    }

    return true;
}

export async function deleteBillWatchAccount(
    confirmation) {

    if (confirmation !==
        "DELETE") {
        throw new Error(
            "Type DELETE to confirm permanent account deletion.");
    }

    const requestToken =
        await getAntiforgeryToken();

    const response =
        await fetch(
            "/bff/account",
            {
                method:
                    "DELETE",

                credentials:
                    "same-origin",

                headers: {
                    "Accept":
                        "application/json",

                    "X-CSRF-TOKEN":
                        requestToken,

                    "X-BillWatch-Delete-Confirmation":
                        confirmation
                },

                cache:
                    "no-store"
            });

    if (!response.ok) {
        throw new Error(
            await getSafeErrorMessage(
                response));
    }

    return true;
}

export async function createPlaidLinkSession() {
    return await mutateJson(
        "/bff/plaid/link-session",
        "POST");
}

export async function createPlaidUpdateLinkSession(
    connectionId) {

    if (!connectionId) {
        throw new Error(
            "Bank connection ID is required.");
    }

    return await mutateJson(
        `/bff/plaid/connections/${encodeURIComponent(connectionId)}/update-link-session`,
        "POST");
}

export async function completePlaidLinkSession(
    sessionId) {

    if (!sessionId) {
        throw new Error(
            "Plaid session ID is required.");
    }

    return await mutateJson(
        `/bff/plaid/link-session/${encodeURIComponent(sessionId)}/complete`,
        "POST");
}

export async function disconnectBankConnection(
    connectionId) {

    if (!connectionId) {
        throw new Error(
            "Bank connection ID is required.");
    }

    return await mutateJson(
        `/bff/bank-connections/${encodeURIComponent(connectionId)}`,
        "DELETE");
}

export function getSelectedStatementFileInfo(
    inputId) {

    const input =
        document.getElementById(
            inputId);

    const file =
        input?.files?.[0];

    if (!file) {
        return null;
    }

    return {
        name:
            file.name,

        size:
            file.size,

        type:
            file.type || ""
    };
}

export function clearStatementFileInput(
    inputId) {

    const input =
        document.getElementById(
            inputId);

    if (input) {
        input.value =
            "";
    }
}

export async function uploadBillStatement(
    billStreamId,
    inputId) {

    if (!billStreamId) {
        throw new Error(
            "Bill stream ID is required.");
    }

    const input =
        document.getElementById(
            inputId);

    const file =
        input?.files?.[0];

    if (!file) {
        throw new Error(
            "Select a bill statement first.");
    }

    if (file.size <= 0) {
        throw new Error(
            "The selected statement is empty.");
    }

    if (file.size >
        statementFileSizeLimit) {

        throw new Error(
            "Bill statements must be 15 MB or smaller.");
    }

    const lowerName =
        file.name
            .toLowerCase();

    const supported =
        lowerName.endsWith(".pdf") ||
        lowerName.endsWith(".jpg") ||
        lowerName.endsWith(".jpeg") ||
        lowerName.endsWith(".png");

    if (!supported) {
        throw new Error(
            "Only PDF, JPG, JPEG, and PNG bill statements are supported.");
    }

    const requestToken =
        await getAntiforgeryToken();

    const formData =
        new FormData();

    formData.append(
        "file",
        file,
        file.name);

    const response =
        await fetch(
            `/bff/bill-streams/${encodeURIComponent(billStreamId)}/statement-uploads`,
            {
                method:
                    "POST",

                credentials:
                    "same-origin",

                headers: {
                    "Accept":
                        "application/json",

                    "X-CSRF-TOKEN":
                        requestToken
                },

                body:
                    formData,

                cache:
                    "no-store"
            });

    return await handleResponse(
        response);
}

export async function getBillStatementUploadStatus(
    billStreamId,
    uploadId) {

    if (!billStreamId ||
        !uploadId) {

        throw new Error(
            "Statement upload identifiers are required.");
    }

    return await getJson(
        `/bff/bill-streams/${encodeURIComponent(billStreamId)}/statement-uploads/${encodeURIComponent(uploadId)}`);
}

export function wirePlaidConnectButton(
    buttonId,
    statusId) {

    const button =
        document.getElementById(
            buttonId);

    const status =
        document.getElementById(
            statusId);

    if (!button ||
        button.dataset.billwatchWired ===
        "true") {
        return;
    }

    button.dataset.billwatchWired =
        "true";

    button.addEventListener(
        "click",
        async () => {

            if (button.dataset.busy ===
                "true") {
                return;
            }

            const plaidWindow =
                openPlaidWindow();

            if (!plaidWindow) {
                setConnectStatus(
                    status,
                    "Your browser blocked the Plaid window. Allow pop-ups for BillWatch and try again.",
                    "error");

                return;
            }

            button.dataset.busy =
                "true";

            button.disabled =
                true;

            const originalText =
                button.textContent;

            button.textContent =
                "Preparing…";

            setConnectStatus(
                status,
                "Preparing secure connection with Plaid…",
                "working");

            try {
                const session =
                    await createPlaidLinkSession();

                if (!session?.sessionId ||
                    !session?.hostedLinkUrl) {
                    throw new Error(
                        "BillWatch did not receive a valid Plaid session.");
                }

                if (!isTrustedPlaidHostedUrl(
                    session.hostedLinkUrl)) {
                    throw new Error(
                        "BillWatch refused an invalid Plaid Hosted Link URL.");
                }

                plaidWindow.location.replace(
                    session.hostedLinkUrl);

                setConnectStatus(
                    status,
                    "Complete the secure connection in the Plaid window. BillWatch is waiting…",
                    "working");

                await waitForPlaidCompletion(
                    session.sessionId,
                    plaidWindow,
                    status,
                    institution =>
                        `${institution} is connected. BillWatch will begin automatic monitoring.`,
                    "/app");
            } catch {
                try {
                    if (!plaidWindow.closed) {
                        plaidWindow.close();
                    }
                } catch {
                }

                setConnectStatus(
                    status,
                    "BillWatch could not start or complete the secure bank connection.",
                    "error");
            } finally {
                button.dataset.busy =
                    "false";

                button.disabled =
                    false;

                button.textContent =
                    originalText;
            }
        });
}

export function wirePlaidReconnectButton(
    buttonId,
    statusId,
    connectionId,
    institutionName) {

    const button =
        document.getElementById(
            buttonId);

    const status =
        document.getElementById(
            statusId);

    if (!button ||
        !connectionId ||
        button.dataset.billwatchWired ===
        "true") {
        return;
    }

    button.dataset.billwatchWired =
        "true";

    button.addEventListener(
        "click",
        async () => {

            if (button.dataset.busy ===
                "true") {
                return;
            }

            const plaidWindow =
                openPlaidWindow();

            if (!plaidWindow) {
                setConnectStatus(
                    status,
                    "Your browser blocked the Plaid window. Allow pop-ups for BillWatch and try again.",
                    "error");

                return;
            }

            button.dataset.busy =
                "true";

            button.disabled =
                true;

            const originalText =
                button.textContent;

            button.textContent =
                "Preparing…";

            const displayInstitution =
                institutionName ||
                "Your bank";

            setConnectStatus(
                status,
                `Preparing secure reauthorization for ${displayInstitution}…`,
                "working");

            try {
                const session =
                    await createPlaidUpdateLinkSession(
                        connectionId);

                if (!session?.sessionId ||
                    !session?.hostedLinkUrl) {
                    throw new Error(
                        "BillWatch did not receive a valid Plaid update session.");
                }

                if (!isTrustedPlaidHostedUrl(
                    session.hostedLinkUrl)) {
                    throw new Error(
                        "BillWatch refused an invalid Plaid Hosted Link URL.");
                }

                plaidWindow.location.replace(
                    session.hostedLinkUrl);

                setConnectStatus(
                    status,
                    "Finish reauthorizing the connection in the Plaid window. BillWatch is waiting…",
                    "working");

                await waitForPlaidCompletion(
                    session.sessionId,
                    plaidWindow,
                    status,
                    institution =>
                        `${institution} is reconnected. Automatic monitoring will resume.`,
                    "/app/account");
            } catch {
                try {
                    if (!plaidWindow.closed) {
                        plaidWindow.close();
                    }
                } catch {
                }

                setConnectStatus(
                    status,
                    `BillWatch could not reauthorize ${displayInstitution}.`,
                    "error");
            } finally {
                button.dataset.busy =
                    "false";

                button.disabled =
                    false;

                button.textContent =
                    originalText;
            }
        });
}