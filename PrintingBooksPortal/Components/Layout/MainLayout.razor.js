export function toggleUserMenu() {
    const menu = document.getElementById('userDropdown');
    if (!menu) return;

    menu.classList.toggle('show');

    const closeHandler = (e) => {
        if (!menu.contains(e.target) && !e.target.closest('.user-menu-trigger')) {
            menu.classList.remove('show');
            document.removeEventListener('click', closeHandler);
        }
    };

    setTimeout(() => document.addEventListener('click', closeHandler), 10);
}

export function toggleSidebar() {
    const sidebar = document.getElementById('appSidebar');
    if (sidebar) {
        sidebar.classList.toggle('show');
    }
    const overlay = document.querySelector('.sidebar-overlay');
    if (overlay) {
        overlay.classList.toggle('show');
    }
}
