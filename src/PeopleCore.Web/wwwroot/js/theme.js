// Light/dark theme for the shadcn token set in index.html.
// Loaded before Blazor so the stored theme is applied on the first paint and the
// page never flashes light before the app boots.
window.peopleCoreTheme = (function () {
    const storageKey = 'peoplecore-theme';
    const media = window.matchMedia('(prefers-color-scheme: dark)');

    function read() {
        try {
            const stored = localStorage.getItem(storageKey);
            return stored === 'light' || stored === 'dark' ? stored : 'system';
        } catch {
            return 'system';
        }
    }

    function apply(theme) {
        const dark = theme === 'dark' || (theme === 'system' && media.matches);
        document.documentElement.classList.toggle('dark', dark);
    }

    media.addEventListener('change', () => {
        if (read() === 'system') {
            apply('system');
        }
    });

    return {
        applyStoredTheme: () => apply(read()),
        getTheme: read,
        setTheme: (theme) => {
            try {
                if (theme === 'system') {
                    localStorage.removeItem(storageKey);
                } else {
                    localStorage.setItem(storageKey, theme);
                }
            } catch {
                // Storage can be unavailable (private mode); the theme still
                // applies for this page load.
            }
            apply(theme);
        }
    };
})();
