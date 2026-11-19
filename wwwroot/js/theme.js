/**
 * SHELFWISE — THE ART OF READING
 * Hybrid Theme System (Light, Dark, and System Auto)
 */
(() => {
    const STORAGE_KEY = "shelfwise-theme";
    const THEMES = ["light", "dark", "auto"];

    const getStoredTheme = () => {
        try {
            return localStorage.getItem(STORAGE_KEY) || "auto";
        } catch {
            return "auto";
        }
    };

    const setStoredTheme = (theme) => {
        try {
            localStorage.setItem(STORAGE_KEY, theme);
        } catch {}
    };

    const applyTheme = (theme) => {
        const root = document.documentElement;
        if (theme === "auto") {
            root.setAttribute("data-theme", "auto");
        } else {
            root.setAttribute("data-theme", theme);
        }
        updateThemeToggleUI(theme);
    };

    const updateThemeToggleUI = (theme) => {
        const toggles = document.querySelectorAll("[data-theme-switcher]");
        toggles.forEach(toggle => {
            const icon = toggle.querySelector("i");
            if (icon) {
                if (theme === "dark") {
                    icon.className = "bi bi-moon-stars-fill";
                } else if (theme === "light") {
                    icon.className = "bi bi-sun-fill";
                } else {
                    icon.className = "bi bi-circle-half";
                }
            }
            toggle.setAttribute("aria-label", `Current theme: ${theme}. Click to change.`);
            toggle.setAttribute("title", `Theme: ${theme.charAt(0).toUpperCase() + theme.slice(1)}`);
        });
    };

    // Cycle theme: Light -> Dark -> Auto -> Light
    window.cycleShelfwiseTheme = () => {
        const current = getStoredTheme();
        const next = current === "light" ? "dark" : current === "dark" ? "auto" : "light";
        setStoredTheme(next);
        applyTheme(next);
    };

    // Initialize immediately before paint to prevent flash
    const initialTheme = getStoredTheme();
    applyTheme(initialTheme);

    // Listen for OS system theme changes
    try {
        window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
            if (getStoredTheme() === "auto") {
                applyTheme("auto");
            }
        });
    } catch {}

    document.addEventListener("DOMContentLoaded", () => {
        updateThemeToggleUI(getStoredTheme());
        document.querySelectorAll("[data-theme-switcher]").forEach(btn => {
            btn.addEventListener("click", (e) => {
                e.preventDefault();
                window.cycleShelfwiseTheme();
            });
        });
    });
})();
