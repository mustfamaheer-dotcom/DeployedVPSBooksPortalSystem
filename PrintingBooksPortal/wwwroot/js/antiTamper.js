(function () {
    'use strict';

    var overlay = null;

    function ensureOverlay() {
        if (overlay) return overlay;
        overlay = document.createElement('div');
        overlay.style.cssText = [
            'position: fixed', 'top: 0', 'left: 0', 'width: 100%', 'height: 100%',
            'z-index: 2147483647', 'background: #ffffff', 'display: flex',
            'align-items: center', 'justify-content: center',
            'flex-direction: column', 'gap: 12px',
            'font-family: Inter, Arial, sans-serif', 'color: #0f172a',
            'text-align: center', 'padding: 24px'
        ].join(';');
        var icon = document.createElement('div');
        icon.style.cssText = 'width: 52px; height: 52px; display: flex; align-items: center; justify-content: center;';
        icon.innerHTML = '<svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>';
        var title = document.createElement('div');
        title.style.cssText = 'font-size: 20px; font-weight: 700;';
        title.textContent = 'Action Blocked';
        var msg = document.createElement('div');
        msg.style.cssText = 'font-size: 14px; color: #64748b; max-width: 420px;';
        msg.id = 'blockOverlayMsg';
        msg.textContent = 'This action is not allowed on the portal.';
        overlay.appendChild(icon);
        overlay.appendChild(title);
        overlay.appendChild(msg);
        document.documentElement.appendChild(overlay);
        return overlay;
    }

    function showOverlay(text) {
        var el = ensureOverlay();
        var msg = document.getElementById('blockOverlayMsg');
        if (msg && text) msg.textContent = text;
        el.style.display = 'flex';
    }

    function hideOverlay() {
        if (overlay) overlay.style.display = 'none';
    }

    function freezeBody(text) {
        var el = ensureOverlay();
        var msg = document.getElementById('blockOverlayMsg');
        if (msg && text) msg.textContent = text;
        el.style.display = 'flex';
        document.body.classList.add('body-frozen');
    }

    window.addEventListener('beforeprint', function (e) {
        e.preventDefault();
        showOverlay('Browser printing is disabled — use the Print button in the book viewer.');
    });

    // ── DevTools / console blocking ──────────────────────────────────────────
    // Blocks F12, Ctrl+Shift+I/J/C, Ctrl+U and the context-menu "Inspect" path.
    document.addEventListener('keydown', function (e) {
        var k = e.key || '';
        if (
            k === 'F12' ||
            (e.ctrlKey && e.shiftKey && (k === 'I' || k === 'J' || k === 'C')) ||
            (e.ctrlKey && k === 'U') ||
            (e.ctrlKey && k === 'S')
        ) {
            e.preventDefault();
            e.stopPropagation();
            return false;
        }
    }, true);

    document.addEventListener('contextmenu', function (e) {
        e.preventDefault();
        return false;
    });

    // Detect an actually-open devtools window (heuristic) and block the session.
    (function () {
        var threshold = 160;
        var blocked = false;
        var check = function () {
            var w = window.outerWidth - window.innerWidth > threshold ||
                    window.outerHeight - window.innerHeight > threshold;
            if (w && !blocked) {
                blocked = true;
                freezeBody('Developer tools cannot be used on the portal. Close DevTools to continue.');
            } else if (!w && blocked) {
                blocked = false;
                hideOverlay();
                document.body.classList.remove('body-frozen');
            }
        };
        if (window.addEventListener) {
            window.addEventListener('resize', check);
            setInterval(check, 1000);
        }
    })();

    // Disable console statements from the console window (best effort).
    try {
        console.log('Access to the developer console is restricted.');
    } catch (err) { /* ignore */ }
})();