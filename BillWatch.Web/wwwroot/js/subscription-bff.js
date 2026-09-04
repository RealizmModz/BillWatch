let token = null;

async function requestToken() {
    if (token) return token;

    const response = await fetch("/bff/antiforgery", {
        credentials: "same-origin",
        cache: "no-store"
    });

    if (!response.ok) {
        throw new Error("Unable to establish a secure request.");
    }

    token = (await response.json()).requestToken;
    return token;
}

async function handle(response, fallbackMessage) {
    if (response.status === 401) {
        window.location.assign("/login");
        throw new Error("Session expired.");
    }

    if (!response.ok) {
        let message = fallbackMessage;

        try {
            const problem = await response.json();
            if (problem && typeof problem.title === "string" && problem.title.trim()) {
                message = problem.title.trim();
            }
        } catch {
        }

        throw new Error(message);
    }

    return await response.json();
}

async function postJson(path, body, fallbackMessage) {
    return await handle(await fetch(path, {
        method: "POST",
        credentials: "same-origin",
        cache: "no-store",
        headers: {
            "Accept": "application/json",
            "Content-Type": "application/json",
            "X-CSRF-TOKEN": await requestToken()
        },
        body: JSON.stringify(body)
    }), fallbackMessage);
}

async function postEmpty(path, fallbackMessage) {
    return await handle(await fetch(path, {
        method: "POST",
        credentials: "same-origin",
        cache: "no-store",
        headers: {
            "Accept": "application/json",
            "X-CSRF-TOKEN": await requestToken()
        }
    }), fallbackMessage);
}

export async function getSubscription() {
    return await handle(await fetch("/bff/subscription", {
        credentials: "same-origin",
        cache: "no-store",
        headers: { "Accept": "application/json" }
    }), "BillWatch could not load your subscription status.");
}

export async function getSubscriptionPlans() {
    return await handle(await fetch("/bff/subscription/plans", {
        credentials: "same-origin",
        cache: "no-store",
        headers: { "Accept": "application/json" }
    }), "BillWatch could not load paid subscription plans.");
}

export async function startCheckout(billingInterval) {
    const result = await postJson(
        "/bff/subscription/checkout",
        { billingInterval },
        "BillWatch could not start checkout.");

    if (!result || typeof result.url !== "string" || !result.url.startsWith("https://")) {
        throw new Error("BillWatch received an invalid checkout destination.");
    }

    window.location.assign(result.url);
}

export async function openBillingPortal() {
    const result = await postEmpty(
        "/bff/subscription/billing-portal",
        "BillWatch could not open subscription management.");

    if (!result || typeof result.url !== "string" || !result.url.startsWith("https://")) {
        throw new Error("BillWatch received an invalid subscription management destination.");
    }

    window.location.assign(result.url);
}

export async function syncPaidSubscription() {
    return await postEmpty(
        "/bff/subscription/sync",
        "BillWatch could not refresh your paid subscription.");
}

export async function redeemAccessKey(accessKey) {
    return await postJson(
        "/bff/subscription/access-keys/redeem",
        { accessKey },
        "That access key could not be redeemed.");
}
