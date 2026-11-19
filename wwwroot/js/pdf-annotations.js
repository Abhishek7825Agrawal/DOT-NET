/**
 * SHELFWISE — PDF Annotations & Persistent Highlights Engine
 * Real Text Layer Selection · Normalized Positioning · SQL Server Sync · Add to Notes
 */

(function () {
    'use strict';

    const annotationsStore = new Map(); // id -> annotation object
    let currentSelectionData = null;

    window.ShelfwiseAnnotations = {
        init: initAnnotations,
        restorePageAnnotations: restorePageAnnotations,
        highlightSelection: highlightSelection,
        addSelectionToNotes: addSelectionToNotes,
        deleteAnnotation: deleteAnnotation,
        toggleBookmark: toggleBookmark
    };

    function initAnnotations(initialAnnotations, initialBookmarks) {
        if (Array.isArray(initialAnnotations)) {
            initialAnnotations.forEach(a => {
                const id = a.id || a.Id;
                const pageNum = a.pageNumber || a.PageNumber;
                const normalized = {
                    id: id,
                    pageNumber: pageNum,
                    selectedText: a.selectedText || a.SelectedText || '',
                    highlightColor: a.highlightColor || a.HighlightColor || 'yellow',
                    positionJson: a.positionJson || a.PositionJson || '[]',
                    textAnchorJson: a.textAnchorJson || a.TextAnchorJson || '{}',
                    documentVersion: a.documentVersion || a.DocumentVersion || 1
                };
                annotationsStore.set(id, normalized);
            });
        }

        setupTextSelectionListener();
        setupBookmarksPanel(initialBookmarks);
    }

    function setupTextSelectionListener() {
        const toolbar = document.getElementById('selectionToolbar');
        const viewport = document.getElementById('pdfViewport');

        // Close toolbar when clicking outside
        document.addEventListener('mousedown', function (e) {
            if (!toolbar.contains(e.target) && !e.target.closest('.textLayer')) {
                hideSelectionToolbar();
            }
        });

        // Listen for selection completion on PDF pages
        viewport.addEventListener('mouseup', function (e) {
            setTimeout(handleSelectionEnd, 50);
        });

        // Toolbar action buttons
        document.querySelectorAll('.sw-color-swatch').forEach(swatch => {
            swatch.addEventListener('click', function () {
                const color = this.getAttribute('data-color');
                highlightSelection(color);
            });
        });

        const addToNotesBtn = document.getElementById('addToNotesBtn');
        if (addToNotesBtn) {
            addToNotesBtn.addEventListener('click', addSelectionToNotes);
        }

        const copyTextBtn = document.getElementById('copySelectionBtn');
        if (copyTextBtn) {
            copyTextBtn.addEventListener('click', function () {
                if (currentSelectionData && currentSelectionData.text) {
                    navigator.clipboard.writeText(currentSelectionData.text).then(() => {
                        this.innerHTML = '<i class="bi bi-check2"></i> Copied';
                        setTimeout(() => {
                            this.innerHTML = '<i class="bi bi-clipboard"></i> Copy';
                            hideSelectionToolbar();
                        }, 1200);
                    });
                }
            });
        }
    }

    function handleSelectionEnd() {
        const sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) {
            hideSelectionToolbar();
            return;
        }

        const range = sel.getRangeAt(0);
        const selectedText = sel.toString().trim();
        if (!selectedText || selectedText.length < 2) {
            hideSelectionToolbar();
            return;
        }

        // Find parent page container
        const textLayer = range.commonAncestorContainer.nodeType === 1
            ? range.commonAncestorContainer.closest('.textLayer')
            : range.commonAncestorContainer.parentElement.closest('.textLayer');

        if (!textLayer) {
            hideSelectionToolbar();
            return;
        }

        const pageContainer = textLayer.closest('.pdf-page-container');
        if (!pageContainer) return;

        const pageNum = parseInt(pageContainer.getAttribute('data-page-number'), 10);
        const pageRect = pageContainer.getBoundingClientRect();
        const clientRects = range.getClientRects();

        if (clientRects.length === 0) return;

        // Compute normalized coordinates (percentages 0..100) relative to page
        const rects = [];
        for (let i = 0; i < clientRects.length; i++) {
            const r = clientRects[i];
            rects.push({
                x: ((r.left - pageRect.left) / pageRect.width) * 100,
                y: ((r.top - pageRect.top) / pageRect.height) * 100,
                width: (r.width / pageRect.width) * 100,
                height: (r.height / pageRect.height) * 100
            });
        }

        currentSelectionData = {
            pageNumber: pageNum,
            text: selectedText,
            rects: rects,
            pageContainer: pageContainer
        };

        // Position toolbar above selection
        const firstRect = clientRects[0];
        const toolbar = document.getElementById('selectionToolbar');
        toolbar.style.display = 'flex';
        toolbar.style.left = `${firstRect.left + (firstRect.width / 2)}px`;
        toolbar.style.top = `${firstRect.top + window.scrollY - 8}px`;
    }

    function hideSelectionToolbar() {
        const toolbar = document.getElementById('selectionToolbar');
        if (toolbar) toolbar.style.display = 'none';
        currentSelectionData = null;
    }

    function highlightSelection(color) {
        if (!currentSelectionData) return;

        const data = currentSelectionData;
        const ws = window.ShelfwiseWorkspace.state;

        const payload = {
            digitalBookId: ws.digitalBookId,
            documentVersion: ws.documentVersion,
            pageNumber: data.pageNumber,
            selectedText: data.text,
            highlightColor: color || 'yellow',
            positionJson: JSON.stringify(data.rects),
            textAnchorJson: JSON.stringify({ length: data.text.length, preview: data.text.substring(0, 30) }),
            linkedNoteId: null
        };

        fetch('/DigitalLibrary/SaveAnnotation', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify(payload)
        })
        .then(r => r.json())
        .then(res => {
            if (res.success) {
                payload.id = res.annotationId;
                annotationsStore.set(res.annotationId, payload);

                // Render immediately on the page
                renderSingleAnnotation(payload, data.pageContainer);
                window.getSelection().removeAllRanges();
                hideSelectionToolbar();
            }
        })
        .catch(err => console.error('Failed to persist highlight:', err));
    }

    function restorePageAnnotations(pageNum, pageContainer) {
        const highlightLayer = pageContainer.querySelector('.sw-highlight-layer');
        if (!highlightLayer) return;
        highlightLayer.innerHTML = '';

        annotationsStore.forEach(annotation => {
            const annoPage = annotation.pageNumber || annotation.PageNumber;
            if (annoPage === pageNum) {
                renderSingleAnnotation(annotation, pageContainer);
            }
        });
    }

    function renderSingleAnnotation(annotation, pageContainer) {
        const highlightLayer = pageContainer.querySelector('.sw-highlight-layer');
        if (!highlightLayer) return;

        let rects = [];
        try {
            rects = typeof annotation.positionJson === 'string'
                ? JSON.parse(annotation.positionJson)
                : annotation.positionJson;
        } catch (e) {
            return;
        }

        if (!Array.isArray(rects)) return;

        const colorClass = `sw-highlight-${annotation.highlightColor || 'yellow'}`;

        rects.forEach(rect => {
            const span = document.createElement('div');
            span.className = `sw-pdf-highlight ${colorClass}`;
            span.style.left = `${rect.x}%`;
            span.style.top = `${rect.y}%`;
            span.style.width = `${rect.width}%`;
            span.style.height = `${rect.height}%`;
            span.setAttribute('data-annotation-id', annotation.id);
            span.setAttribute('title', `Highlight: "${annotation.selectedText}" (Click to remove)`);

            span.addEventListener('click', function (e) {
                e.stopPropagation();
                if (confirm(`Remove this highlight on page ${annotation.pageNumber}?`)) {
                    deleteAnnotation(annotation.id);
                }
            });

            highlightLayer.appendChild(span);
        });
    }

    function deleteAnnotation(annotationId) {
        fetch(`/DigitalLibrary/DeleteAnnotation/${annotationId}`, {
            method: 'POST',
            headers: {
                'RequestVerificationToken': getCsrfToken()
            }
        })
        .then(r => r.json())
        .then(res => {
            if (res.success) {
                annotationsStore.delete(annotationId);
                document.querySelectorAll(`[data-annotation-id="${annotationId}"]`).forEach(el => el.remove());
            }
        })
        .catch(err => console.error('Error deleting highlight:', err));
    }

    function addSelectionToNotes() {
        if (!currentSelectionData) return;

        const text = currentSelectionData.text;
        const pageNum = currentSelectionData.pageNumber;

        if (window.ShelfwiseNotes && window.ShelfwiseNotes.insertQuote) {
            window.ShelfwiseNotes.insertQuote(text, pageNum);

            // Also give subtle visual feedback
            const btn = document.getElementById('addToNotesBtn');
            btn.innerHTML = '<i class="bi bi-check2"></i> Added to Notes';
            setTimeout(() => {
                btn.innerHTML = '<i class="bi bi-journal-plus"></i> Add to Notes';
                hideSelectionToolbar();
            }, 1000);
        }
    }

    function setupBookmarksPanel(initialBookmarks) {
        const toggleBtn = document.getElementById('bookmarkToggleBtn');
        if (!toggleBtn) return;

        toggleBtn.addEventListener('click', function () {
            const ws = window.ShelfwiseWorkspace.state;
            toggleBookmark(ws.digitalBookId, ws.currentPage);
        });

        // Populate drawer
        renderBookmarksList(initialBookmarks || []);
    }

    function toggleBookmark(digitalBookId, pageNum) {
        fetch('/DigitalLibrary/ToggleBookmark', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify({
                digitalBookId: digitalBookId,
                pageNumber: pageNum
            })
        })
        .then(r => r.json())
        .then(res => {
            if (res.success) {
                const btn = document.getElementById('bookmarkToggleBtn');
                const icon = btn.querySelector('i');
                if (res.isBookmarked) {
                    icon.className = 'bi bi-bookmark-fill text-terracotta';
                    addBookmarkToSidebar(res.bookmarkId, pageNum, res.label);
                } else {
                    icon.className = 'bi bi-bookmark';
                    removeBookmarkFromSidebar(pageNum);
                }
            }
        });
    }

    function renderBookmarksList(bookmarks) {
        const container = document.getElementById('bookmarksList');
        if (!container) return;
        container.innerHTML = '';

        if (bookmarks.length === 0) {
            container.innerHTML = '<div class="text-muted small p-3 text-center">No bookmarks saved yet.</div>';
            return;
        }

        bookmarks.forEach(b => addBookmarkToSidebar(b.id, b.pageNumber, b.label));
    }

    function addBookmarkToSidebar(id, pageNum, label) {
        const container = document.getElementById('bookmarksList');
        if (!container) return;

        // Remove empty state if present
        if (container.querySelector('.text-muted')) {
            container.innerHTML = '';
        }

        const item = document.createElement('div');
        item.className = 'sw-drawer-item';
        item.id = `bookmark-item-${pageNum}`;
        item.innerHTML = `
            <div>
                <strong style="color: var(--sw-navy);">Page ${pageNum}</strong>
                <small class="text-muted d-block">${label || ''}</small>
            </div>
            <i class="bi bi-arrow-right-short text-muted fs-5"></i>
        `;

        item.addEventListener('click', () => {
            window.ShelfwiseWorkspace.jumpToPage(pageNum);
            document.getElementById('bookmarksDrawer').classList.remove('open');
        });

        container.appendChild(item);
    }

    function removeBookmarkFromSidebar(pageNum) {
        const item = document.getElementById(`bookmark-item-${pageNum}`);
        if (item) item.remove();
    }

    function getCsrfToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

})();
