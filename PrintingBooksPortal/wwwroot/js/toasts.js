// ─── Toast notification system ─────────────────────────────────────────────
// Repository-wide popup confirmations used after actions, saves and logins.
// Exposes window.showToast(message, type) — types: success | error | info | warning.

(function () {
    'use strict';

    var container = null;

    function ensureContainer() {
        if (container) return container;
        container = document.createElement('div');
        container.id = 'toastContainer';
        container.setAttribute('aria-live', 'polite');
        document.body.appendChild(container);
        return container;
    }

    var icons = {
        success: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
        error: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>',
        warning: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
        info: '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>'
    };

    function showToast(message, type, duration) {
        type = type || 'success';
        duration = duration || 4500;

        var el = document.createElement('div');
        el.className = 'toast-item toast-' + type;

        var iconWrap = document.createElement('div');
        iconWrap.className = 'toast-icon';
        iconWrap.innerHTML = icons[type] || icons.info;

        var msg = document.createElement('div');
        msg.className = 'toast-msg';
        msg.textContent = message || '';

        var close = document.createElement('button');
        close.className = 'toast-close';
        close.setAttribute('aria-label', 'Dismiss');
        close.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>';
        close.addEventListener('click', function () {
            dismiss(el);
        });

        el.appendChild(iconWrap);
        el.appendChild(msg);
        el.appendChild(close);
        ensureContainer().appendChild(el);

        requestAnimationFrame(function () {
            el.classList.add('toast-in');
        });

        var timer = setTimeout(function () {
            dismiss(el);
        }, duration);

        function dismiss(node) {
            clearTimeout(timer);
            node.classList.remove('toast-in');
            node.classList.add('toast-out');
            setTimeout(function () {
                if (node.parentNode) node.parentNode.removeChild(node);
            }, 300);
        }
    }

    // ── Welcome popup after login ─────────────────────────────────────────────
    // LoginController sets a short-lived cookie bp_welcome; the first page loaded
    // after sign-in turns it into a "Welcome back" confirmation popup.
    function showWelcomeToast() {
        var match = document.cookie.match(/(?:^|;\s*)bp_welcome=([^;]*)/);
        if (!match) return;
        document.cookie = 'bp_welcome=; Path=/; Max-Age=0';
        var name = decodeURIComponent(match[1] || '');
        showToast(name ? 'Welcome back, ' + name + '!' : 'Welcome back!', 'success', 5000);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', showWelcomeToast);
    } else {
        showWelcomeToast();
    }

    // ── Password visibility helpers (bound by Blazor) ─────────────────────────
    window.revealPassword = function (id) {
        var el = document.getElementById(id);
        if (el) el.type = 'text';
    };

    window.concealPassword = function (id) {
        var el = document.getElementById(id);
        if (el) el.type = 'password';
    };
})();