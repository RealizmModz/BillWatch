(() => {
    const storageKey = "billwatch-theme";
    let theme = "dark";

    try {
        const savedTheme = window.localStorage.getItem(storageKey);

        if (savedTheme === "light" || savedTheme === "dark") {
            theme = savedTheme;
        }
    }
    catch {
        // Restricted storage contexts still receive the secure dark default.
    }

    document.documentElement.dataset.theme = theme;
    document.documentElement.style.colorScheme = theme;
})();
