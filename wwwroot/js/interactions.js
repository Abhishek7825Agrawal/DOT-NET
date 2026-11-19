/**
 * SHELFWISE — THE ART OF READING
 * Form Interactions, Micro-Feedbacks & Safety Guards
 */
document.addEventListener("DOMContentLoaded", () => {
    // 1. Double Submission Prevention with Micro-Spinner
    document.addEventListener("submit", (e) => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;

        if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            e.preventDefault();
            return;
        }

        if (!form.noValidate && !form.checkValidity()) {
            e.preventDefault();
            e.stopPropagation();
            form.classList.add("was-validated");
            return;
        }

        if (form.method.toLowerCase() === "post") {
            if (form.dataset.submitting === "true") {
                e.preventDefault();
                return;
            }

            const action = (form.getAttribute("action") || "").toLowerCase();
            if (action.includes("export") || form.hasAttribute("download")) {
                return;
            }

            const submitBtn = form.querySelector('button[type="submit"]:not([disabled])');
            if (submitBtn && !submitBtn.dataset.noLoading) {
                form.dataset.submitting = "true";
                submitBtn.classList.add("is-loading");

                const spinner = document.createElement("span");
                spinner.className = "btn-spinner";
                spinner.setAttribute("aria-hidden", "true");
                submitBtn.prepend(spinner);

                setTimeout(() => {
                    delete form.dataset.submitting;
                    submitBtn.classList.remove("is-loading");
                    if (spinner.parentNode === submitBtn) spinner.remove();
                }, 6000);
            }
        }
    });

    // 2. Auto-Dismiss Dismissible Alerts
    const alerts = document.querySelectorAll(".alert-dismissible");
    alerts.forEach(alert => {
        setTimeout(() => {
            if (typeof bootstrap !== "undefined" && bootstrap.Alert) {
                const bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
                if (bsAlert) bsAlert.close();
            }
        }, 6000);
    });
});
