/**
 * SHELFWISE — THE ART OF READING
 * Rich, Smooth Animations: Count-Up KPIs, Chart Progression & Reveal
 */
document.addEventListener("DOMContentLoaded", () => {
    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (prefersReducedMotion) return;

    // 1. Smooth Count-Up for Numbers in KPI Blocks
    const countUpElements = document.querySelectorAll("[data-count-up]");
    if (countUpElements.length > 0 && "IntersectionObserver" in window) {
        const countObserver = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const el = entry.target;
                    const targetVal = parseFloat(el.getAttribute("data-count-up"));
                    const prefix = el.getAttribute("data-prefix") || "";
                    const suffix = el.getAttribute("data-suffix") || "";
                    const decimals = parseInt(el.getAttribute("data-decimals") || "0", 10);
                    const duration = 1000;
                    const startTimestamp = performance.now();

                    const step = (now) => {
                        const elapsed = now - startTimestamp;
                        const progress = Math.min(elapsed / duration, 1);
                        // Ease-out cubic
                        const easeOut = 1 - Math.pow(1 - progress, 3);
                        const currentVal = targetVal * easeOut;
                        
                        el.textContent = `${prefix}${currentVal.toFixed(decimals)}${suffix}`;

                        if (progress < 1) {
                            requestAnimationFrame(step);
                        } else {
                            el.textContent = `${prefix}${targetVal.toFixed(decimals)}${suffix}`;
                        }
                    };

                    requestAnimationFrame(step);
                    observer.unobserve(el);
                }
            });
        }, { threshold: 0.2 });

        countUpElements.forEach(el => countObserver.observe(el));
    }

    // 2. Progressive Chart Bar Rendering
    const chartBars = document.querySelectorAll(".sw-chart-bar-fill[data-height]");
    if (chartBars.length > 0 && "IntersectionObserver" in window) {
        const barObserver = new IntersectionObserver((entries, observer) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const bar = entry.target;
                    const targetHeight = bar.getAttribute("data-height");
                    setTimeout(() => {
                        bar.style.height = targetHeight;
                    }, 100);
                    observer.unobserve(bar);
                }
            });
        }, { threshold: 0.1 });

        chartBars.forEach(bar => {
            bar.style.height = "0%";
            barObserver.observe(bar);
        });
    }
});
