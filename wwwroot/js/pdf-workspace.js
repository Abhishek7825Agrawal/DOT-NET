/**
 * SHELFWISE — PDF.js Reader & Split Workspace Controller
 * Mozilla PDF.js Integration · Text Layer · Resizer · Page Stream
 */

(function () {
    'use strict';

    // Global workspace state
    const state = {
        pdfDoc: null,
        currentPage: 1,
        totalPages: 0,
        currentScale: 1.25,
        renderQueue: {},
        renderedPages: new Set(),
        pageRendering: false,
        pageNumPending: null,
        streamUrl: '',
        digitalBookId: 0,
        bookId: 0,
        documentVersion: 1,
        activeTab: 'pdf' // 'pdf' or 'notes' for mobile
    };

    window.ShelfwiseWorkspace = {
        state: state,
        init: initWorkspace,
        jumpToPage: jumpToPage,
        zoomIn: zoomIn,
        zoomOut: zoomOut,
        fitWidth: fitWidth,
        fitPage: fitPage
    };

    function initWorkspace(config) {
        state.streamUrl = config.streamUrl;
        state.digitalBookId = config.digitalBookId;
        state.bookId = config.bookId;
        state.documentVersion = config.documentVersion || 1;
        state.currentPage = config.initialPage || 1;

        setupResizer();
        setupMobileTabs();
        setupReaderControls();
        setupKeyboardShortcuts();

        // Configure Mozilla PDF.js Worker
        if (typeof pdfjsLib !== 'undefined') {
            pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
            loadPdfDocument();
        } else {
            console.error('Mozilla PDF.js library not detected on page.');
        }
    }

    function loadPdfDocument() {
        const loadingTask = pdfjsLib.getDocument({
            url: state.streamUrl,
            withCredentials: true,
            rangeChunkSize: 65536 // 64 KB chunks for Range Requests
        });

        const loadingOverlay = document.getElementById('readerLoadingOverlay');

        loadingTask.promise.then(function (pdf) {
            state.pdfDoc = pdf;
            state.totalPages = pdf.numPages;

            document.getElementById('totalPagesLabel').textContent = pdf.numPages;
            document.getElementById('pageNumberInput').max = pdf.numPages;

            if (loadingOverlay) loadingOverlay.classList.add('d-none');

            // Render initial viewport
            renderViewportPages();

            // Scroll to initial page
            setTimeout(() => {
                jumpToPage(state.currentPage);
            }, 300);

        }).catch(function (error) {
            console.error('Error loading PDF document:', error);
            if (loadingOverlay) {
                loadingOverlay.innerHTML = `
                    <div class="text-danger p-4 text-center">
                        <i class="bi bi-exclamation-triangle display-4 mb-2 d-block"></i>
                        <h5>Unable to render digital manuscript</h5>
                        <p class="small text-muted">${error.message || 'The document stream could not be loaded.'}</p>
                    </div>`;
            }
        });
    }

    function renderViewportPages() {
        const container = document.getElementById('pdfPagesContainer');
        container.innerHTML = '';
        state.renderedPages.clear();

        for (let pageNum = 1; pageNum <= state.totalPages; pageNum++) {
            const pageDiv = document.createElement('div');
            pageDiv.className = 'pdf-page-container';
            pageDiv.id = `page-container-${pageNum}`;
            pageDiv.setAttribute('data-page-number', pageNum);

            // Placeholder sizing
            pageDiv.style.minHeight = `${842 * state.currentScale}px`;
            pageDiv.style.width = `${595 * state.currentScale}px`;

            container.appendChild(pageDiv);
        }

        // Setup Intersection Observer for lazy rendering of pages
        setupPageObserver();
    }

    function setupPageObserver() {
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                const pageNum = parseInt(entry.target.getAttribute('data-page-number'), 10);
                if (entry.isIntersecting) {
                    renderPage(pageNum);

                    // Update current page counter and server progress
                    if (pageNum !== state.currentPage) {
                        state.currentPage = pageNum;
                        document.getElementById('pageNumberInput').value = pageNum;
                        onPageChanged(pageNum);
                    }
                }
            });
        }, {
            root: document.getElementById('pdfViewport'),
            rootMargin: '200px 0px 200px 0px',
            threshold: 0.1
        });

        document.querySelectorAll('.pdf-page-container').forEach(el => observer.observe(el));
    }

    function renderPage(pageNum) {
        if (state.renderedPages.has(pageNum)) return;
        state.renderedPages.add(pageNum);

        const pageContainer = document.getElementById(`page-container-${pageNum}`);
        if (!pageContainer) return;

        state.pdfDoc.getPage(pageNum).then(function (page) {
            const viewport = page.getViewport({ scale: state.currentScale });

            pageContainer.style.width = `${viewport.width}px`;
            pageContainer.style.height = `${viewport.height}px`;

            // Canvas rendering
            let canvas = pageContainer.querySelector('canvas');
            if (!canvas) {
                canvas = document.createElement('canvas');
                pageContainer.appendChild(canvas);
            }
            const context = canvas.getContext('2d');
            canvas.height = viewport.height;
            canvas.width = viewport.width;

            const renderContext = {
                canvasContext: context,
                viewport: viewport
            };

            page.render(renderContext).promise.then(function () {
                // Text Layer rendering for real selection and highlighting
                return page.getTextContent();
            }).then(function (textContent) {
                let textLayerDiv = pageContainer.querySelector('.textLayer');
                if (!textLayerDiv) {
                    textLayerDiv = document.createElement('div');
                    textLayerDiv.className = 'textLayer';
                    pageContainer.appendChild(textLayerDiv);
                }
                textLayerDiv.innerHTML = '';
                textLayerDiv.style.width = `${viewport.width}px`;
                textLayerDiv.style.height = `${viewport.height}px`;

                pdfjsLib.renderTextLayer({
                    textContent: textContent,
                    container: textLayerDiv,
                    viewport: viewport,
                    textDivs: []
                });

                // Highlight layer for persistent user highlights
                let highlightLayer = pageContainer.querySelector('.sw-highlight-layer');
                if (!highlightLayer) {
                    highlightLayer = document.createElement('div');
                    highlightLayer.className = 'sw-highlight-layer';
                    pageContainer.appendChild(highlightLayer);
                }

                // Restore saved annotations onto this page
                if (window.ShelfwiseAnnotations && window.ShelfwiseAnnotations.restorePageAnnotations) {
                    window.ShelfwiseAnnotations.restorePageAnnotations(pageNum, pageContainer);
                }
            });
        });
    }

    function jumpToPage(pageNum) {
        pageNum = Math.clamp(pageNum, 1, state.totalPages);
        state.currentPage = pageNum;
        document.getElementById('pageNumberInput').value = pageNum;

        const targetEl = document.getElementById(`page-container-${pageNum}`);
        if (targetEl) {
            targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }

    function zoomIn() {
        if (state.currentScale >= 3.0) return;
        state.currentScale = Math.round((state.currentScale + 0.25) * 100) / 100;
        rerenderAllPages();
    }

    function zoomOut() {
        if (state.currentScale <= 0.5) return;
        state.currentScale = Math.round((state.currentScale - 0.25) * 100) / 100;
        rerenderAllPages();
    }

    function fitWidth() {
        const viewportEl = document.getElementById('pdfViewport');
        const availableWidth = viewportEl.clientWidth - 60;
        if (availableWidth > 300) {
            state.currentScale = Math.round((availableWidth / 595) * 100) / 100;
            rerenderAllPages();
        }
    }

    function fitPage() {
        state.currentScale = 1.0;
        rerenderAllPages();
    }

    function rerenderAllPages() {
        document.getElementById('zoomPercentageLabel').textContent = `${Math.round(state.currentScale * 100)}%`;
        state.renderedPages.clear();
        renderViewportPages();
        jumpToPage(state.currentPage);
    }

    function setupReaderControls() {
        document.getElementById('prevPageBtn').addEventListener('click', () => jumpToPage(state.currentPage - 1));
        document.getElementById('nextPageBtn').addEventListener('click', () => jumpToPage(state.currentPage + 1));

        const pageInput = document.getElementById('pageNumberInput');
        pageInput.addEventListener('change', function () {
            const page = parseInt(this.value, 10);
            if (!isNaN(page)) jumpToPage(page);
        });

        document.getElementById('zoomInBtn').addEventListener('click', zoomIn);
        document.getElementById('zoomOutBtn').addEventListener('click', zoomOut);
        document.getElementById('fitWidthBtn').addEventListener('click', fitWidth);
        document.getElementById('fitPageBtn').addEventListener('click', fitPage);

        // Fullscreen Toggle
        const fullscreenBtn = document.getElementById('fullscreenBtn');
        if (fullscreenBtn) {
            fullscreenBtn.addEventListener('click', () => {
                if (!document.fullscreenElement) {
                    document.documentElement.requestFullscreen().catch(() => {});
                } else {
                    document.exitFullscreen().catch(() => {});
                }
            });
        }
    }

    function setupResizer() {
        const resizer = document.getElementById('workspaceResizer');
        const pdfPane = document.getElementById('panePdf');
        const body = document.getElementById('workspaceBody');

        if (!resizer || !pdfPane || !body) return;

        // Restore saved split width percentage
        const savedRatio = localStorage.getItem('shelfwise-split-ratio');
        if (savedRatio) {
            const ratio = parseFloat(savedRatio);
            if (ratio >= 25 && ratio <= 75) {
                pdfPane.style.width = `${ratio}%`;
            }
        }

        let isResizing = false;

        resizer.addEventListener('mousedown', function (e) {
            e.preventDefault();
            isResizing = true;
            resizer.classList.add('resizing');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
        });

        window.addEventListener('mousemove', function (e) {
            if (!isResizing) return;
            const containerRect = body.getBoundingClientRect();
            const pointerX = e.clientX - containerRect.left;
            let ratio = (pointerX / containerRect.width) * 100;
            ratio = Math.clamp(ratio, 25, 75);

            pdfPane.style.width = `${ratio}%`;
        });

        window.addEventListener('mouseup', function () {
            if (isResizing) {
                isResizing = false;
                resizer.classList.remove('resizing');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';

                // Persist preferred width
                const currentWidth = parseFloat(pdfPane.style.width);
                if (!isNaN(currentWidth)) {
                    localStorage.setItem('shelfwise-split-ratio', currentWidth.toString());
                }
            }
        });
    }

    function setupMobileTabs() {
        const tabPdf = document.getElementById('mobileTabPdf');
        const tabNotes = document.getElementById('mobileTabNotes');
        const panePdf = document.getElementById('panePdf');
        const paneNotes = document.getElementById('paneNotes');

        if (!tabPdf || !tabNotes) return;

        tabPdf.addEventListener('click', () => {
            tabPdf.classList.add('active');
            tabNotes.classList.remove('active');
            panePdf.classList.remove('d-none-mobile');
            paneNotes.classList.add('d-none-mobile');
            state.activeTab = 'pdf';
        });

        tabNotes.addEventListener('click', () => {
            tabNotes.classList.add('active');
            tabPdf.classList.remove('active');
            paneNotes.classList.remove('d-none-mobile');
            panePdf.classList.add('d-none-mobile');
            state.activeTab = 'notes';
        });
    }

    function setupKeyboardShortcuts() {
        window.addEventListener('keydown', function (e) {
            // Ignore when typing inside input or quill editor
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.closest('.ql-editor')) {
                return;
            }

            if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
                jumpToPage(state.currentPage - 1);
            } else if (e.key === 'ArrowRight' || e.key === 'PageDown') {
                jumpToPage(state.currentPage + 1);
            } else if (e.key === '+' || e.key === '=') {
                zoomIn();
            } else if (e.key === '-') {
                zoomOut();
            }
        });
    }

    let progressTimeout = null;
    function onPageChanged(pageNum) {
        clearTimeout(progressTimeout);
        progressTimeout = setTimeout(() => {
            fetch('/DigitalLibrary/UpdateProgress', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getCsrfToken()
                },
                body: JSON.stringify({
                    digitalBookId: state.digitalBookId,
                    lastPageRead: pageNum
                })
            }).catch(() => {});
        }, 1500);
    }

    function getCsrfToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

    // Utility
    Math.clamp = function (val, min, max) {
        return Math.min(Math.max(val, min), max);
    };

})();
