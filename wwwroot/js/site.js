// Shelfwise Library Management System - Client Scripts
document.addEventListener("DOMContentLoaded", () => {
    // Form confirmation handler
    document.addEventListener("submit", event => {
        const form = event.target;
        if (form instanceof HTMLFormElement && form.dataset.confirm) {
            if (!window.confirm(form.dataset.confirm)) {
                event.preventDefault();
                return;
            }
        }

        // Native HTML5 / Client-side validation check
        if (form instanceof HTMLFormElement && !form.noValidate) {
            if (!form.checkValidity()) {
                event.preventDefault();
                event.stopPropagation();
                form.classList.add("was-validated");
            }
        }
    });

    // Mobile sidebar toggle
    const toggleBtn = document.getElementById("sidebarToggle");
    const sidebar = document.getElementById("appSidebar");
    const overlay = document.getElementById("sidebarOverlay");

    if (toggleBtn && sidebar && overlay) {
        toggleBtn.addEventListener("click", () => {
            const isOpen = sidebar.classList.toggle("show");
            overlay.classList.toggle("show", isOpen);
            toggleBtn.setAttribute("aria-expanded", String(isOpen));
        });

        const closeSidebar = () => {
            sidebar.classList.remove("show");
            overlay.classList.remove("show");
            toggleBtn.setAttribute("aria-expanded", "false");
        };
        overlay.addEventListener("click", closeSidebar);
        sidebar.querySelectorAll("a").forEach(link => link.addEventListener("click", closeSidebar));
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && sidebar.classList.contains("show")) {
                closeSidebar();
                toggleBtn.focus();
            }
        });
    }

    // Auto-dismiss alerts after 5 seconds
    const alerts = document.querySelectorAll(".alert-dismissible");
    alerts.forEach(alert => {
        setTimeout(() => {
            if (typeof bootstrap !== 'undefined' && bootstrap.Alert) {
                const bsAlert = new bootstrap.Alert(alert);
                bsAlert.close();
            }
        }, 6000);
    });
});
