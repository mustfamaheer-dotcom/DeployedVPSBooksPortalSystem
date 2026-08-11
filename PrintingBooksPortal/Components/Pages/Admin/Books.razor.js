let dropZone = null;

export function initFileDrop() {
    const zone = document.getElementById('pdfDropZone');
    const input = document.getElementById('pdfUpload');
    if (!zone || !input) return;

    // The card is re-rendered when reopened; only wire the current node once.
    if (zone === dropZone) return;
    dropZone = zone;

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

        // Forward the dropped file into the hidden input and fire the change
        // event Blazor's InputFile listens for.
        input.files = files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
}
