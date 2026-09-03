let token = null;

async function requestToken() {
    if (token) return token;
    const response = await fetch("/bff/antiforgery", { credentials: "same-origin", cache: "no-store" });
    if (!response.ok) throw new Error("Unable to establish a secure request.");
    token = (await response.json()).requestToken;
    return token;
}

async function handle(response) {
    if (response.status === 401) {
        window.location.assign("/login");
        throw new Error("Session expired.");
    }
    if (!response.ok) throw new Error("That access key could not be redeemed.");
    return await response.json();
}

export async function getSubscription() {
    return await handle(await fetch("/bff/subscription", {
        credentials: "same-origin", cache: "no-store", headers: { "Accept": "application/json" }
    }));
}

export async function redeemAccessKey(accessKey) {
    return await handle(await fetch("/bff/subscription/access-keys/redeem", {
        method: "POST",
        credentials: "same-origin",
        cache: "no-store",
        headers: {
            "Accept": "application/json",
            "Content-Type": "application/json",
            "X-CSRF-TOKEN": await requestToken()
        },
        body: JSON.stringify({ accessKey })
    }));
}
