// Toggle sidebar
const sidebarToggle = document.getElementById('sidebar-toggle');
const sidebar = document.getElementById('sidebar');
const header = document.getElementById('header');
const mainContent = document.getElementById('main-content');
//const footer = document.getElementById('footer');
const logo = document.getElementById('header_logo');
const sidebarIcon = sidebarToggle.querySelector('i');

// Theme toggle
const themeToggle = document.getElementById('themeToggle');
const body = document.body;

// Check for saved theme preference
const savedTheme = localStorage.getItem('theme');
if (savedTheme) {
    body.setAttribute('data-theme', savedTheme);
    themeToggle.checked = savedTheme === 'dark';
    logo.src = savedTheme === 'dark' ? `/content/img/${empresa}/logo.png` : `/content/img/${empresa}/logo-light.png`;
}

themeToggle.addEventListener('change', () => {
    if (themeToggle.checked) {
        body.setAttribute('data-theme', 'dark');
        localStorage.setItem('theme', 'dark');
        logo.src = `/content/img/${empresa}/logo.png`;
    } else {
        body.removeAttribute('data-theme');
        localStorage.setItem('theme', 'light');
        logo.src = `/content/img/${empresa}/logo-light.png`;
    }
});

// Dropdown functionality
document.querySelectorAll('.dropdown-toggle').forEach(toggle => {
    toggle.addEventListener('click', (e) => {
        e.preventDefault();

        const dropdown = toggle.parentNode;
        const menu = dropdown.querySelector('.nav-dropdown-menu');

        // Close other dropdowns
        document.querySelectorAll('.nav-dropdown-menu.show').forEach(openMenu => {
            if (openMenu !== menu) {
                openMenu.classList.remove('show');
                openMenu.parentNode.querySelector('.dropdown-toggle').classList.add('collapsed');
            }
        });

        // Toggle current dropdown
        menu.classList.toggle('show');
        toggle.classList.toggle('collapsed');
    });
});

// Submenu functionality
document.querySelectorAll('.submenu-toggle').forEach(toggle => {
    toggle.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();

        // En modo icono, los flyouts los maneja CSS hover — no hacer nada
        const sidebarEl = document.getElementById('sidebar');
        if (sidebarEl.classList.contains('icon-mode')) return;

        const submenu = toggle.parentNode;
        const menu = submenu.querySelector('.nav-submenu-menu');
        if (!menu) return;

        const parentDropdown = submenu.closest('.nav-dropdown-menu');
        if (parentDropdown) {
            parentDropdown.querySelectorAll('.nav-submenu-menu.show').forEach(openMenu => {
                if (openMenu !== menu) {
                    openMenu.classList.remove('show');
                    openMenu.parentNode.querySelector('.submenu-toggle')?.classList.add('collapsed');
                }
            });
        }

        menu.classList.toggle('show');
        toggle.classList.toggle('collapsed');
    });
});


// Sub-submenu functionality
document.querySelectorAll('.sub-submenu-toggle').forEach(toggle => {
    toggle.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();

        // En modo icono, los flyouts los maneja CSS hover — no hacer nada
        const sidebarEl = document.getElementById('sidebar');
        if (sidebarEl.classList.contains('icon-mode')) return;

        const subSubmenu = toggle.parentNode;
        const menu = subSubmenu.querySelector('.nav-sub-submenu-menu');
        if (!menu) return;

        const parentSubmenu = subSubmenu.closest('.nav-submenu-menu');
        if (parentSubmenu) {
            parentSubmenu.querySelectorAll('.nav-sub-submenu-menu.show').forEach(openMenu => {
                if (openMenu !== menu) {
                    openMenu.classList.remove('show');
                    openMenu.parentNode.querySelector('.sub-submenu-toggle')?.classList.add('collapsed');
                }
            });
        }

        menu.classList.toggle('show');
        toggle.classList.toggle('collapsed');
    });
});

// ── Aplica posición correcta según modo (izq/der) y estado collapsed ──
function applyLayout(collapsed) {
    const isRight = sidebar.classList.contains('sb-right');
    const isIconMode = sidebar.classList.contains('icon-mode');

    // Ancho efectivo del sidebar según el modo actual
    const sbWidth = isIconMode ? 'var(--sb-icon-width)' : 'var(--sb-width)';

    if (isRight) {
        header.style.left = '0';
        header.style.right = collapsed ? '0' : sbWidth;
        mainContent.style.marginLeft = '0';
        mainContent.style.marginRight = collapsed ? '0' : sbWidth;
        header.classList.remove('expanded');
        mainContent.classList.remove('expanded');
    } else {
        header.style.right = '0';
        header.style.left = '';
        mainContent.style.marginRight = '0';
        mainContent.style.marginLeft = '';
        header.classList.toggle('expanded', collapsed);
        mainContent.classList.toggle('expanded', collapsed);
    }
}

// Mobile responsive
function handleResize() {
    if (window.innerWidth <= 768) {
        sidebar.classList.remove('collapsed');
        sidebar.classList.remove('show');
        header.style.left = '';
        header.style.right = '';
        mainContent.style.marginLeft = '';
        mainContent.style.marginRight = '';
        header.classList.remove('expanded');
        mainContent.classList.remove('expanded');
    } else {
        sidebar.classList.remove('show');
        const pref = localStorage.getItem('sidebarDefaultState');
        const startCollapsed = pref === 'collapsed';
        sidebar.classList.toggle('collapsed', startCollapsed);
        applyLayout(startCollapsed);
    }
}

window.addEventListener('resize', handleResize);
handleResize();

sidebarToggle.addEventListener('click', (e) => {
    e.stopPropagation();
    const isMobile = window.innerWidth <= 768;
    if (isMobile) {
        sidebar.classList.toggle('show');
    } else {
        sidebar.classList.toggle('collapsed');
        applyLayout(sidebar.classList.contains('collapsed'));
    }

    const isSidebarOpen = !sidebar.classList.contains('collapsed') || sidebar.classList.contains('show');
    sidebarIcon.classList.add('rotating');
    setTimeout(() => {
        sidebarIcon.classList.remove('fa-chevrons-left', 'fa-chevrons-right');
        sidebarIcon.classList.add(isSidebarOpen ? 'fa-chevrons-left' : 'fa-chevrons-right');
        sidebarIcon.classList.remove('rotating');
    }, 150);
});


// ── Modo de sidebar (normal / iconos) ──
function aplicarModoSidebar(modo) {
    const sidebar = document.getElementById('sidebar');
    sidebar.classList.toggle('icon-mode', modo === 'iconos');

    // Forzar :has() en navegadores sin soporte (fallback)
    document.body.classList.toggle('sb-icon-mode', modo === 'iconos');

    if (typeof applyLayout === 'function') {
        applyLayout(sidebar.classList.contains('collapsed'));
    }
}

// Aplicar al cargar, leyendo el modo desde un atributo data en el sidebar
document.addEventListener('DOMContentLoaded', () => {
    const sidebar = document.getElementById('sidebar');
    const modoActual = sidebar.dataset.sbModo || 'normal';
    aplicarModoSidebar(modoActual);
});

// Close sidebar when clicking outside
document.addEventListener('click', (e) => {
    if (window.innerWidth <= 768 &&
        !sidebar.contains(e.target) &&
        !sidebarToggle.contains(e.target)) {
        sidebar.classList.remove('show');
    }
});

document.addEventListener('DOMContentLoaded', () => {
    if (!sidebar) return;

    // ── Flyout en modo icono: click en lugar de hover ──
    function closeAllFlyoutsExcept(keepMenu) {
        sidebar.querySelectorAll('.flyout-visible').forEach(m => {
            if (m !== keepMenu) m.classList.remove('flyout-visible');
        });
    }

    function adjustFlyoutPosition(trigger, menu) {
        // Resetear posición antes de medir
        menu.style.top = '';
        menu.style.bottom = '';
        menu.style.maxHeight = '';
        menu.style.overflowY = '';

        const triggerRect = trigger.getBoundingClientRect();
        const menuRect = menu.getBoundingClientRect();
        const vpHeight = window.innerHeight;
        const margin = 8; // px de margen respecto al borde

        // Punto de inicio ideal: alineado al top del trigger
        let topPos = triggerRect.top;

        // Si se sale por abajo, subir
        if (topPos + menuRect.height > vpHeight - margin) {
            topPos = vpHeight - margin - menuRect.height;
        }

        // Si aún se sale por arriba, anclar arriba y hacer scroll interno
        if (topPos < margin) {
            topPos = margin;
            const maxH = vpHeight - margin * 2;
            menu.style.maxHeight = maxH + 'px';
            menu.style.overflowY = 'auto';
        }

        menu.style.top = topPos + 'px';
        menu.style.bottom = 'auto';

        // Convertir a position:fixed para salir del flujo del sidebar
        const parentRect = menu.offsetParent?.getBoundingClientRect() ?? { top: 0 };
        menu.style.top = (topPos - parentRect.top) + 'px';
    }

    function bindFlyoutClick(parent, menu) {
        if (!menu) return;

        const trigger = parent.querySelector(
            ':scope > .nav-link, :scope > .submenu-toggle, :scope > .sub-submenu-toggle'
        );
        if (!trigger) return;

        trigger.addEventListener('click', (e) => {
            if (!sidebar.classList.contains('icon-mode')) return;

            e.preventDefault();
            e.stopPropagation();

            const isOpen = menu.classList.contains('flyout-visible');

            // Cierra flyouts del mismo nivel
            const parentContainer = parent.parentElement;
            if (parentContainer) {
                parentContainer.querySelectorAll(
                    ':scope > * > .nav-dropdown-menu.flyout-visible,' +
                    ':scope > * > .nav-submenu-menu.flyout-visible,' +
                    ':scope > * > .nav-sub-submenu-menu.flyout-visible'
                ).forEach(m => {
                    m.classList.remove('flyout-visible');
                    m.style.top = '';
                    m.style.bottom = '';
                    m.style.maxHeight = '';
                    m.style.overflowY = '';
                });
            }

            if (!isOpen) {
                // Mostrar primero para poder medir
                menu.classList.add('flyout-visible');
                adjustFlyoutPosition(trigger, menu);
            }
        });
    }

    // Nivel 1
    document.querySelectorAll('.nav-dropdown').forEach(el => {
        const menu = el.querySelector(':scope > .nav-dropdown-menu');
        if (menu) bindFlyoutClick(el, menu);
    });

    // Nivel 2
    document.querySelectorAll('.nav-submenu').forEach(el => {
        const menu = el.querySelector(':scope > .nav-submenu-menu');
        if (!menu) return;

        bindFlyoutClick(el, menu);

        // Ajuste horizontal: el flyout de nivel 2 sale a la derecha del flyout de nivel 1
        el.querySelector(':scope > .submenu-toggle')?.addEventListener('click', () => {
            if (!sidebar.classList.contains('icon-mode')) return;
            if (!menu.classList.contains('flyout-visible')) return;

            const parentMenu = el.closest('.nav-dropdown-menu');
            if (!parentMenu) return;

            const parentRect = parentMenu.getBoundingClientRect();
            menu.style.left = (parentRect.right + 4) + 'px';
            menu.style.right = 'auto';
        });
    });

    // Nivel 3
    document.querySelectorAll('.nav-sub-submenu').forEach(el => {
        const menu = el.querySelector(':scope > .nav-sub-submenu-menu');
        if (!menu) return;

        bindFlyoutClick(el, menu);

        el.querySelector(':scope > .sub-submenu-toggle')?.addEventListener('click', () => {
            if (!sidebar.classList.contains('icon-mode')) return;
            if (!menu.classList.contains('flyout-visible')) return;

            const parentMenu = el.closest('.nav-submenu-menu');
            if (!parentMenu) return;

            const parentRect = parentMenu.getBoundingClientRect();
            menu.style.left = (parentRect.right + 4) + 'px';
            menu.style.right = 'auto';
        });
    });

    // Cerrar flyouts al hacer click fuera del sidebar
    document.addEventListener('click', (e) => {
        if (!sidebar.classList.contains('icon-mode')) return;
        if (!sidebar.contains(e.target)) {
            sidebar.querySelectorAll('.flyout-visible')
                .forEach(m => m.classList.remove('flyout-visible'));
        }
    });

    // ── Tooltip de nivel 1 en modo icono ──
    const sbTooltip = document.createElement('div');
    sbTooltip.className = 'sb-tooltip';
    document.body.appendChild(sbTooltip);
    let tooltipTimeout = null;

    sidebar.querySelectorAll('.nav-item > .nav-link[data-label], .nav-dropdown > .nav-link[data-label]').forEach(link => {
        link.addEventListener('mouseenter', (e) => {
            if (!sidebar.classList.contains('icon-mode')) return;

            const label = link.dataset.label?.trim();
            if (!label) return;

            clearTimeout(tooltipTimeout);

            const rect = link.getBoundingClientRect();
            const isRight = sidebar.classList.contains('sb-right');

            sbTooltip.textContent = label;
            sbTooltip.style.top = (rect.top + rect.height / 2) + 'px';

            // Posicionar temporalmente fuera para medir ancho
            sbTooltip.style.left = '-9999px';
            sbTooltip.style.opacity = '0';
            sbTooltip.classList.add('visible');

            requestAnimationFrame(() => {
                const tw = sbTooltip.offsetWidth;
                if (isRight) {
                    sbTooltip.style.left = (rect.left - tw - 10) + 'px';
                } else {
                    sbTooltip.style.left = (rect.right + 10) + 'px';
                }
                sbTooltip.style.top = (rect.top + rect.height / 2 - sbTooltip.offsetHeight / 2) + 'px';
                sbTooltip.style.opacity = '';
            });
        });

        link.addEventListener('mouseleave', () => {
            tooltipTimeout = setTimeout(() => {
                sbTooltip.classList.remove('visible');
            }, 100);
        });

        // Ocultar al hacer click (abre el flyout)
        link.addEventListener('click', () => {
            sbTooltip.classList.remove('visible');
        });
    });
});
