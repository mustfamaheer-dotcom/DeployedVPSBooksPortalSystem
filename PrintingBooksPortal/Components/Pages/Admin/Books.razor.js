let dotNetRef = null;

function wireDropZone(zoneId, inputId, labelId) {
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
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });

    input.addEventListener('change', () => updateLabel(input, labelId));
}

function updateLabel(input, labelId) {
    const label = document.getElementById(labelId);
    if (!label || !input.files?.[0]) return;
    label.textContent = input.files[0].name;
}

function notify(error) {
    dotNetRef?.invokeMethodAsync('OnUploadComplete', JSON.stringify({ success: false, error }));
}

export function init(ref) {
    dotNetRef = ref;
    refresh();
}

export function refresh() {
    wireDropZone('pdfDropZone', 'pdfUpload', 'uploadFileName');
    wireDropZone('editPdfDropZone', 'editPdfUpload', 'editFileName');
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

    doUpload(fd, 'uploadProgress');
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

    doUpload(fd, 'editProgress');
}

function doUpload(fd, progressId) {
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
        if (bar) bar.style.width = pct + '%';
        if (text) text.textContent = pct + '%';
        dotNetRef?.invokeMethodAsync('OnUploadProgress', pct);
    };
    xhr.onload = () => {
        let data = { success: false, error: 'Upload failed (server error).' };
        try { data = JSON.parse(xhr.responseText); } catch { /* keep default */ }
        if (xhr.status >= 200 && xhr.status < 300 && data.success) {
            if (bar) bar.style.width = '100%';
            if (text) text.textContent = '100%';
        }
        dotNetRef?.invokeMethodAsync('OnUploadComplete', JSON.stringify(data));
    };
    xhr.onerror = () => {
        dotNetRef?.invokeMethodAsync('OnUploadComplete', JSON.stringify({ success: false, error: 'Network error during upload.' }));
    };
    xhr.send(fd);
}
