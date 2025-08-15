window.themeManager = {
    // This function applies the theme to the HTML document
    setTheme: (theme) => {
        if (theme === 'auto') {
            // In 'auto' mode, we remove the attribute. This lets the
            // CSS @media (prefers-color-scheme) query take control.
            document.documentElement.removeAttribute('data-theme');
        } else {
            // For 'light' or 'dark', we explicitly set the attribute,
            // which overrides the media query.
            document.documentElement.setAttribute('data-theme', theme);
        }
    },

    // This function saves the user's preference to the browser's local storage
    saveThemePreference: (theme) => {
        localStorage.setItem('theme', theme);
    },

    // This function retrieves the saved preference on page load
    loadThemePreference: () => {
        return localStorage.getItem('theme');
    }
};