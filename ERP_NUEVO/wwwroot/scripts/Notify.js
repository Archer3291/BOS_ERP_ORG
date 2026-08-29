// Función para mostrar toast con botones de acción
function showToastWithActions(type, title, message, actions = [], duration = 5000) {
    const toastContainer = document.getElementById('my-toast-container');
    const toastId = 'toast-' + Date.now();

    // Iconos para cada tipo
    const icons = {
        success: 'fas fa-check',
        error: 'fas fa-times',
        warning: 'fas fa-exclamation-triangle',
        info: 'fas fa-info-circle'
    };

    // Iconos para botones
    const buttonIcons = {
        save: 'fas fa-save',
        trash: 'fas fa-trash',
        edit: 'fas fa-edit',
        download: 'fas fa-download',
        share: 'fas fa-share',
        link: 'fas fa-external-link-alt',
        refresh: 'fas fa-sync-alt'
    };

    // Generar HTML de botones de acción
    let actionsHTML = '';
    if (actions && actions.length > 0) {
        const buttonsHTML = actions.map(action => {
            const iconHTML = action.icon ? `<i class="${buttonIcons[action.icon] || action.icon}"></i>` : '';
            const styleClass = action.style ? `my-toast-btn-${action.style}` : '';
            return `
                <button class="my-toast-btn ${styleClass}" onclick="handleToastAction('${toastId}', ${actions.indexOf(action)})">
                    ${iconHTML}${action.text}
                </button>
                    `;
        }).join('');

        actionsHTML = `<div class="my-toast-actions">${buttonsHTML}</div>`;
    }

    // Crear el HTML del toast
    const toastHTML = `
        <div class="toast my-toast custom-toast my-toast-${type}" id="${toastId}" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="my-toast-header toast-header">
                <div class="my-toast-icon">
                    <i class="${icons[type]}"></i>
                </div>
                <strong class="my-toast-title">${title}</strong>
                <button type="button" class="toast-close my-toast-close" data-bs-dismiss="toast" aria-label="Close">
                    <i class="fas fa-times"></i>
                </button>
            </div>
            <div class="my-toast-body">
                ${message}
            </div>
            ${actionsHTML}
        </div>`;

    // Insertar el toast en el contenedor
    toastContainer.insertAdjacentHTML('beforeend', toastHTML);

    // Guardar las acciones en el elemento para acceso posterior
    const toastElement = document.getElementById(toastId);
    toastElement._actions = actions;

    // Inicializar el toast de Bootstrap
    const toast = new bootstrap.Toast(toastElement, {
        delay: duration,
        autohide: duration > 0
    });

    // Mostrar el toast
    toast.show();

    // Limpiar el DOM cuando el toast se oculte
    toastElement.addEventListener('hidden.bs.toast', () => {
        toastElement.remove();
    });

    return toast;
}

// Función para manejar acciones de botones
function handleToastAction(toastId, actionIndex) {
    const toastElement = document.getElementById(toastId);
    if (toastElement && toastElement._actions && toastElement._actions[actionIndex]) {
        const action = toastElement._actions[actionIndex];

        // Ejecutar la acción si existe
        if (action.action && typeof action.action === 'function') {
            action.action();
        }

        // Cerrar el toast después de la acción (opcional)
        const toast = bootstrap.Toast.getInstance(toastElement);
        if (toast) {
            toast.hide();
        }
    }
}

// Función de utilidad para toast rápido
function quickToast(message, type = 'info') {
    const titles = {
        success: 'Éxito',
        error: 'Error',
        warning: 'Advertencia',
        info: 'Información'
    };

    return showToast(type, titles[type], message);
}