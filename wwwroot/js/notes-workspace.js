/**
 * SHELFWISE — Quill Rich-Text Notes Workspace Controller
 * Quill.js Delta JSON · Autosave Debounce · PDF Quotation Injection · Multi-Note
 */

(function () {
    'use strict';

    let quill = null;
    let activeNote = null;
    let notesList = [];
    let autosaveTimer = null;
    let isSaving = false;

    window.ShelfwiseNotes = {
        init: initNotes,
        insertQuote: insertQuote,
        saveActiveNote: saveActiveNote,
        createNewNote: createNewNote,
        deleteActiveNote: deleteActiveNote,
        switchNote: switchNote
    };

    function initNotes(initialNotes, bookId) {
        notesList = initialNotes || [];
        activeNote = notesList.length > 0 ? notesList[0] : null;

        initQuillEditor();
        setupNoteControls();
        setupAutosaveListener();

        if (activeNote) {
            loadNoteIntoEditor(activeNote);
        }
    }

    function initQuillEditor() {
        const toolbarOptions = [
            [{ 'header': [1, 2, 3, false] }],
            ['bold', 'italic', 'underline', 'strike'],
            [{ 'color': [] }, { 'background': [] }],
            [{ 'list': 'ordered' }, { 'list': 'bullet' }],
            ['blockquote', 'code-block'],
            [{ 'align': [] }],
            ['clean']
        ];

        quill = new Quill('#quillEditor', {
            theme: 'snow',
            placeholder: 'Capture your thoughts, annotations, and research insights here...',
            modules: {
                toolbar: toolbarOptions
            }
        });
    }

    function setupNoteControls() {
        const titleInput = document.getElementById('noteTitleInput');
        if (titleInput) {
            titleInput.addEventListener('input', function () {
                if (activeNote) {
                    activeNote.title = this.value;
                    scheduleAutosave();
                }
            });
        }

        const newNoteBtn = document.getElementById('newNoteBtn');
        if (newNoteBtn) {
            newNoteBtn.addEventListener('click', createNewNote);
        }

        const notesSelector = document.getElementById('notesSelector');
        if (notesSelector) {
            notesSelector.addEventListener('change', function () {
                const noteId = parseInt(this.value, 10);
                switchNote(noteId);
            });
        }

        const deleteNoteBtn = document.getElementById('deleteNoteBtn');
        if (deleteNoteBtn) {
            deleteNoteBtn.addEventListener('click', deleteActiveNote);
        }

        const exportNoteBtn = document.getElementById('exportNoteBtn');
        if (exportNoteBtn) {
            exportNoteBtn.addEventListener('click', function () {
                if (activeNote) {
                    window.open(`/DigitalLibrary/ExportNote/${activeNote.id}`, '_blank');
                }
            });
        }
    }

    function setupAutosaveListener() {
        quill.on('text-change', function (delta, oldDelta, source) {
            if (source === 'user') {
                setSaveStatus('unsaved');
                scheduleAutosave();
            }
        });

        // Also save on window unload
        window.addEventListener('beforeunload', function () {
            if (activeNote) {
                saveActiveNote(true);
            }
        });
    }

    function scheduleAutosave() {
        clearTimeout(autosaveTimer);
        autosaveTimer = setTimeout(() => {
            saveActiveNote(false);
        }, 1500); // 1.5s debounce delay
    }

    function saveActiveNote(isSync = false) {
        if (!activeNote || isSaving) return;

        setSaveStatus('saving');
        isSaving = true;

        const delta = quill.getContents();
        const html = quill.root.innerHTML;
        const text = quill.getText();

        const payload = {
            noteId: activeNote.id,
            bookId: window.ShelfwiseWorkspace.state.bookId,
            title: activeNote.title || 'Reading Notes',
            contentDeltaJson: JSON.stringify(delta),
            contentHtml: html,
            plainTextExcerpt: text.substring(0, 300).trim(),
            clientVersion: activeNote.version || 1
        };

        fetch('/DigitalLibrary/SaveNote', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getCsrfToken()
            },
            body: JSON.stringify(payload),
            keepalive: isSync
        })
        .then(r => r.json())
        .then(res => {
            isSaving = false;
            if (res.success) {
                activeNote.id = res.noteId;
                activeNote.version = res.newVersion;
                activeNote.lastModifiedUtc = res.lastModifiedUtc;

                updateNoteInSelector(activeNote);
                setSaveStatus('saved');
            } else {
                setSaveStatus('error');
            }
        })
        .catch(err => {
            isSaving = false;
            console.error('Autosave error:', err);
            setSaveStatus('error');
        });
    }

    function loadNoteIntoEditor(note) {
        activeNote = note;
        const title = note.title || note.Title || 'Reading Reflections';
        document.getElementById('noteTitleInput').value = title;

        try {
            const deltaJson = note.contentDeltaJson || note.ContentDeltaJson;
            const html = note.sanitizedContentHtml || note.SanitizedContentHtml;
            if (deltaJson) {
                const delta = typeof deltaJson === 'string' ? JSON.parse(deltaJson) : deltaJson;
                quill.setContents(delta, 'silent');
            } else if (html) {
                quill.root.innerHTML = html;
            } else {
                quill.setText('');
            }
        } catch (e) {
            quill.setText('');
        }

        setSaveStatus('saved');
    }

    function createNewNote() {
        const bookId = window.ShelfwiseWorkspace.state.bookId;
        const noteTitle = `Notes · ${new Date().toLocaleDateString()}`;

        fetch(`/DigitalLibrary/CreateNote?bookId=${bookId}&title=${encodeURIComponent(noteTitle)}`, {
            method: 'POST',
            headers: {
                'RequestVerificationToken': getCsrfToken()
            }
        })
        .then(r => r.json())
        .then(newNote => {
            notesList.push(newNote);
            refreshNotesSelector();
            document.getElementById('notesSelector').value = newNote.id;
            loadNoteIntoEditor(newNote);
        })
        .catch(err => console.error('Failed to create note:', err));
    }

    function deleteActiveNote() {
        if (!activeNote) return;
        if (notesList.length <= 1) {
            alert('You must keep at least one notebook for this volume.');
            return;
        }

        if (confirm(`Permanently delete "${activeNote.title}"?`)) {
            fetch(`/DigitalLibrary/DeleteNote/${activeNote.id}`, {
                method: 'POST',
                headers: {
                    'RequestVerificationToken': getCsrfToken()
                }
            })
            .then(r => r.json())
            .then(res => {
                if (res.success) {
                    notesList = notesList.filter(n => n.id !== activeNote.id);
                    refreshNotesSelector();
                    if (notesList.length > 0) {
                        loadNoteIntoEditor(notesList[0]);
                    }
                }
            });
        }
    }

    function switchNote(noteId) {
        // Save current note before switching
        if (activeNote) {
            saveActiveNote(true);
        }

        const note = notesList.find(n => n.id === noteId);
        if (note) {
            loadNoteIntoEditor(note);
        }
    }

    function insertQuote(selectedText, pageNum) {
        if (!quill) return;

        const selection = quill.getSelection() || { index: quill.getLength() };
        const quoteText = `"${selectedText.trim()}"\n— Page ${pageNum}\n`;

        quill.insertText(selection.index, quoteText, {
            'blockquote': true,
            'color': '#555555'
        });

        quill.insertText(selection.index + quoteText.length, '\n', {});
        quill.setSelection(selection.index + quoteText.length + 1);

        scheduleAutosave();
    }

    function refreshNotesSelector() {
        const selector = document.getElementById('notesSelector');
        if (!selector) return;

        selector.innerHTML = '';
        notesList.forEach(n => {
            const opt = document.createElement('option');
            opt.value = n.id;
            opt.textContent = n.title;
            selector.appendChild(opt);
        });

        if (activeNote) {
            selector.value = activeNote.id;
        }
    }

    function updateNoteInSelector(note) {
        const selector = document.getElementById('notesSelector');
        if (!selector) return;
        const opt = selector.querySelector(`option[value="${note.id}"]`);
        if (opt) {
            opt.textContent = note.title;
        }
    }

    function setSaveStatus(status) {
        const dot = document.getElementById('saveStatusDot');
        const text = document.getElementById('saveStatusText');
        if (!dot || !text) return;

        dot.className = 'sw-status-dot';

        if (status === 'saving') {
            dot.classList.add('saving');
            text.textContent = 'Saving...';
        } else if (status === 'saved') {
            const time = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            text.textContent = `Saved at ${time}`;
        } else if (status === 'unsaved') {
            dot.classList.add('saving');
            text.textContent = 'Unsaved changes';
        } else if (status === 'error') {
            dot.classList.add('error');
            text.textContent = 'Save failed (retrying)';
        }
    }

    function getCsrfToken() {
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : '';
    }

})();
