/**
 * SHELFWISE — THE ART OF READING
 * Minimal Horizontal Top Navigation & Responsive Mobile Drawer
 */
document.addEventListener("DOMContentLoaded", () => {
    // 1. Mobile Navigation Drawer
    const navToggle = document.getElementById("swNavToggle");
    const mobileDrawer = document.getElementById("swMobileDrawer");
    const mobileOverlay = document.getElementById("swMobileOverlay");
    const drawerCloseBtn = document.getElementById("swDrawerClose");

    const openDrawer = () => {
        if (!mobileDrawer || !mobileOverlay) return;
        mobileDrawer.classList.add("show");
        mobileOverlay.classList.add("show");
        navToggle?.setAttribute("aria-expanded", "true");
        document.body.style.overflow = "hidden";
    };

    const closeDrawer = () => {
        if (!mobileDrawer || !mobileOverlay) return;
        mobileDrawer.classList.remove("show");
        mobileOverlay.classList.remove("show");
        navToggle?.setAttribute("aria-expanded", "false");
        document.body.style.overflow = "";
    };

    if (navToggle) {
        navToggle.addEventListener("click", openDrawer);
    }
    if (mobileOverlay) {
        mobileOverlay.addEventListener("click", closeDrawer);
    }
    if (drawerCloseBtn) {
        drawerCloseBtn.addEventListener("click", closeDrawer);
    }

    // 2. Notifications Slide-out Drawer
    const notifToggle = document.getElementById("swNotificationToggle");
    const notifDrawer = document.getElementById("swNotificationDrawer");
    const notifClose = document.getElementById("swNotificationClose");

    const toggleNotif = () => {
        if (!notifDrawer) return;
        const isOpen = notifDrawer.classList.toggle("show");
        if (mobileOverlay) mobileOverlay.classList.toggle("show", isOpen);
    };

    if (notifToggle) notifToggle.addEventListener("click", toggleNotif);
    if (notifClose) notifClose.addEventListener("click", toggleNotif);

    // Escape Key Dismiss
    document.addEventListener("keydown", (e) => {
        if (e.key === "Escape") {
            closeDrawer();
            if (notifDrawer?.classList.contains("show")) {
                notifDrawer.classList.remove("show");
                if (mobileOverlay) mobileOverlay.classList.remove("show");
            }
        }
    });

    // 3. Quick Global Search Navigation on Enter
    const searchInputs = document.querySelectorAll(".sw-nav-search input");
    searchInputs.forEach(input => {
        input.addEventListener("keydown", (e) => {
            if (e.key === "Enter" && input.value.trim().length > 0) {
                e.preventDefault();
                window.location.href = `/Books?search=${encodeURIComponent(input.value.trim())}`;
            }
        });
    });
});
