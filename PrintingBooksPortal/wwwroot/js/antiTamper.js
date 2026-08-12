(function () {
    'use strict';

    // ── Print protection only ─────────────────────────────────────────────
    // Browser printing is disabled — use the Print button in the book viewer.

    function showPrintNotice() {
        var msg = 'Browser printing is disabled — use the Print button in the book viewer.';
        if (window.showToast) {
            try { window.showToast(msg, 'warning'); return; } catch (err) { /* fall through */ }
        }
        alert(msg);
    }

    window.addEventListener('beforeprint', function (e) {
        e.preventDefault();
        showPrintNotice();
    });

    document.addEventListener('keydown', function (e) {
        var k = (e.key || '').toLowerCase();
        if (
            (e.ctrlKey && k === 'p') ||
            (e.metaKey && k === 'p')
        ) {
            e.preventDefault();
            e.stopPropagation();
            return false;
        }
    }, true);

    document.addEventListener('keyup', function (e) {
        var k = (e.key || '').toLowerCase();
        if (e.ctrlKey && k === 'p') {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);
})();
