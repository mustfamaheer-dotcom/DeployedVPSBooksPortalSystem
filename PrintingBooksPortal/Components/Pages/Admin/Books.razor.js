let dotNetRef = null;
let pdfjsLib = null;
let uploadState = { active: false, kind: 'upload', percent: 0, title: '', result: null };

function safeInvoke(method, arg) {
    if (!dotNetRef) return;
    try {
        dotNetRef.invokeMethodAsync(method, arg).catch(() => { });
    } catch (e) { }
}

function loadPdfJs() {
    return new Promise((resolve, reject) => {
        if (pdfjsLib) { resolve(pdfjsLib); return; }
        const script = document.createElement('script');
        script.src = '/js/pdf.min.js';
        script.onload = () => { pdfjsLib = window.pdfjsLib; resolve(pdfjsLib); };
        script.onerror = () => reject(new Error('pdf.js failed to load'));
        document.head.appendChild(script);
    });
}

function countPages(file, hintId) {
    const hint = hintId ? document.getElementById(hintId) : null;
    if (!hint || !file) return;
    hint.textContent = 'Detecting page count...';

    (async () => {
        let url = null;
        try {
            const pdf = await loadPdfJs();
            url = URL.createObjectURL(file);
            const doc = await pdf.getDocument({ url, disableWorker: true, disableFontFace: true }).promise;
            hint.textContent = file.name + ' — ' + doc.numPages + ' page' + (doc.numPages === 1 ? '' : 's');
            doc.destroy?.();
        } catch {
            hint.textContent = 'Could not detect the page count from this PDF.';
        } finally {
            if (url) URL.revokeObjectURL(url);
        }
    })();
}

function wireDropZone(zoneId, inputId, labelId, hintId) {
    const zone = document.getElementById(zoneId);
    const input = document.getElementById(inputId);
    if (!zone || !input || zone.dataset.wired) return;
    zone.dataset.wired = '1';

    zone.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.stopPropagation();
        zone.classList.add('dragover');
    });

    zone.addEventListener('dragleave', (e) => {
        e.preventDefault();
        e.stopPropagation();
        zone.classList.remove('dragover');
    });

    zone.addEventListener('drop', (e) => {
        e.preventDefault();
        e.stopPropagation();
        zone.classList.remove('dragover');

        const files = e.dataTransfer?.files;
        if (!files || files.length === 0) return;

        input.files = files;
        updateLabel(input, labelId);
        countPages(files[0], hintId);
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });

    input.addEventListener('change', () => {
        updateLabel(input, labelId);
        countPages(input.files?.[0], hintId);
    });
}

function updateLabel(input, labelId) {
    const label = document.getElementById(labelId);
    if (!label || !input.files?.[0]) return;
    label.textContent = input.files[0].name;
}

function notify(error) {
    safeInvoke('OnUploadComplete', JSON.stringify({ success: false, error }));
}

function restore() {
    if (!uploadState.active) return;
    const prefix = uploadState.kind === 'edit' ? 'editProgress' : 'uploadProgress';
    const wrap = document.getElementById(prefix + 'Wrap');
    const bar = document.getElementById(prefix + 'Bar');
    const text = document.getElementById(prefix + 'Text');
    if (wrap) wrap.style.display = 'flex';
    if (bar) bar.style.width = uploadState.percent + '%';
    if (text) text.textContent = uploadState.percent + '%';
}

export function init(ref) {
    dotNetRef = ref;
    refresh();
}

export function refresh() {
    wireDropZone('pdfDropZone', 'pdfUpload', 'uploadFileName', 'uploadPagesHint');
    wireDropZone('editPdfDropZone', 'editPdfUpload', 'editFileName', 'editPagesHint');
    restore();
}

export function getState() {
    const s = {
        active: uploadState.active,
        kind: uploadState.kind,
        percent: uploadState.percent,
        title: uploadState.title,
        result: uploadState.result
    };
    uploadState.result = null;
    return JSON.stringify(s);
}

export function uploadNewBook() {
    const input = document.getElementById('pdfUpload');
    const file = input?.files?.[0];
    if (!file) {
        notify('Please select a PDF file.');
        return;
    }
    if (!file.name.toLowerCase().endsWith('.pdf')) {
        notify('Only PDF files are allowed.');
        return;
    }

    const fd = new FormData();
    fd.append('BookId', '0');
    fd.append('Title', document.getElementById('uploadTitle')?.value ?? '');
    fd.append('BoardId', document.getElementById('uploadBoard')?.value ?? '0');
    fd.append('IsActive', 'true');
    fd.append('file', file);

    doUpload(fd, 'uploadProgress', 'upload');
}

export function updateBook(bookId) {
    const fd = new FormData();
    fd.append('BookId', String(bookId));
    fd.append('Title', document.getElementById('editTitle')?.value ?? '');
    fd.append('BoardId', document.getElementById('editBoard')?.value ?? '0');
    fd.append('IsActive', document.getElementById('editIsActive')?.checked ? 'true' : 'false');

    const file = document.getElementById('editPdfUpload')?.files?.[0];
    if (file) {
        if (!file.name.toLowerCase().endsWith('.pdf')) {
            notify('Only PDF files are allowed.');
            return;
        }
        fd.append('file', file);
    }

    doUpload(fd, 'editProgress', 'edit');
}

function doUpload(fd, progressId, kind) {
    if (uploadState.active) {
        notify('An upload is already in progress.');
        return;
    }

    uploadState = {
        active: true,
        kind: kind || 'upload',
        percent: 0,
        title: fd.get('Title') || '',
        result: null
    };

    const wrap = document.getElementById(progressId + 'Wrap');
    const bar = document.getElementById(progressId + 'Bar');
    const text = document.getElementById(progressId + 'Text');
    if (wrap) wrap.style.display = 'flex';
    if (bar) bar.style.width = '0%';
    if (text) text.textContent = '0%';

    const xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/admin/books/upload');
    xhr.upload.onprogress = (e) => {
        if (!e.lengthComputable) return;
        const pct = Math.round((e.loaded / e.total) * 100);
        uploadState.percent = pct;
        if (bar) bar.style.width = pct + '%';
        if (text) text.textContent = pct + '%';
        safeInvoke('OnUploadProgress', pct);
    };
    xhr.onload = () => {
        let data = { success: false, error: 'Upload failed (server error).' };
        try { data = JSON.parse(xhr.responseText); } catch {
            if (xhr.status === 401 || xhr.status === 403) data.error = 'Session expired — please sign in again.';
            else if (xhr.status === 413) data.error = 'File is too large for the server.';
            else if (xhr.status === 400) data.error = 'The server rejected the upload (400).';
            else if (xhr.status >= 500) data.error = 'Server error (' + xhr.status + '). Please try again.';
        }
        if (xhr.status >= 200 && xhr.status < 300 && data.success) {
            uploadState.percent = 100;
            if (bar) bar.style.width = '100%';
            if (text) text.textContent = '100%';
        }
        uploadState.active = false;
        uploadState.result = data;
        safeInvoke('OnUploadComplete', JSON.stringify(data));
    };
    xhr.onerror = () => {
        uploadState.active = false;
        uploadState.result = { success: false, error: 'Network error during upload.' };
        safeInvoke('OnUploadComplete', JSON.stringify(uploadState.result));
    };
    xhr.send(fd);
}
