function crearBuscador({
    modalId,
    inputBusquedaId,
    listaResultadosId,
    paginacionId,
    urlBusqueda,
    urlDetalle,
    onSelect,
    pageSize = 50,
    extraParams = () => ({})
}) {
    const modal = document.getElementById(modalId);
    const inputBusqueda = document.getElementById(inputBusquedaId);
    const lista = document.getElementById(listaResultadosId);
    const pag = document.getElementById(paginacionId);

    async function buscar(nombre = '', page = 1) {
        try {
            const params = new URLSearchParams({
                nombre,
                page,
                pageSize,
                ...extraParams()
            });

            const res = await fetch(`${urlBusqueda}?${params.toString()}`);
            if (!res.ok) throw new Error('Error en la búsqueda.');
            const data = await res.json();

            lista.innerHTML = '';
            if (!data.length) {
                lista.innerHTML = '<div class="alert alert-warning">No se encontraron resultados.</div>';
                actualizarPaginacion(page, 0);
                return;
            }

            const tabla = document.createElement('table');
            tabla.classList.add('table', 'table-hover', 'table-bordered');
            tabla.innerHTML = `
                <thead class="table-secondary">
                    <tr><th>Clave</th><th>Descripción</th></tr>
                </thead><tbody></tbody>`;
            const tbody = tabla.querySelector('tbody');

            data.forEach(item => {
                const tr = document.createElement('tr');
                tr.style.cursor = 'pointer';
                tr.innerHTML = `<td>${item.id}</td><td>${item.descripcion}</td>`;
                tr.addEventListener('click', () => seleccionar(item.id));
                tbody.appendChild(tr);
            });

            lista.appendChild(tabla);
            actualizarPaginacion(page, data.length);
        } catch (error) {
            console.error('Error al buscar: ', error);
            lista.innerHTML = '<div class="alert alert-danger">Ocurrió un error al cargar los resultados.</div>';
        }
    }

    function actualizarPaginacion(page, count) {
        pag.innerHTML = '';

        const prev = document.createElement('button');
        prev.className = 'btn btn-outline-primary';
        prev.textContent = 'Anterior';
        prev.disabled = page === 1;
        prev.onclick = () => buscar(inputBusqueda.value, page - 1);

        const next = document.createElement('button');
        next.className = 'btn btn-outline-primary';
        next.textContent = 'Siguiente';
        next.disabled = count < pageSize;
        next.onclick = () => buscar(inputBusqueda.value, page + 1);

        pag.append(prev, next);
    }

    async function seleccionar(id) {
        try {
            const sucursal = document.getElementById('selectSucursalFiltro')?.value || '';
            const res = await fetch(`${urlDetalle}?id=${encodeURIComponent(id)}&sucursal=${encodeURIComponent(sucursal)}`);
            if (!res.ok) throw new Error('Error al obtener detalle.');
            const data = await res.json();

            if (data && data[0]) {
                // CRÍTICO: Verificar que onSelect esté definida antes de llamarla
                if (typeof onSelect === 'function') {
                    await onSelect(data[0]);
                } else {
                    console.error('onSelect no es una función válida');
                }
            }

            const modalInst = bootstrap.Modal.getOrCreateInstance(modal);
            modalInst.hide();

            modal.addEventListener('hidden.bs.modal', () => {
                lista.innerHTML = '';
                pag.innerHTML = '';
                inputBusqueda.value = '';
            }, { once: true });
        } catch (error) {
            console.error('Error al seleccionar elemento: ', error);

            // Mostrar error al usuario si hay notificaciones disponibles
            if (typeof NotificationManager !== 'undefined') {
                NotificationManager.show('error', 'Error al seleccionar el elemento. Inténtelo nuevamente.');
            } else if (typeof toastr !== 'undefined') {
                toastr.error('Error al seleccionar el elemento');
            }
        }
    }

    // Debounce para evitar llamadas excesivas
    function debounce(fn, delay = 300) {
        let timer;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delay);
        };
    }

    modal.addEventListener('shown.bs.modal', () => buscar());
    inputBusqueda.addEventListener('input', debounce(() => buscar(inputBusqueda.value)));
}

// ===== FUNCIONES AUXILIARES PARA VALIDACIÓN =====
// Estas funciones verifican que las dependencias estén disponibles

/**
 * Verifica si la tabla está inicializada y disponible
 */
function esTablaDisponible() {
    return (
        (typeof table !== 'undefined' && table !== null) ||
        (typeof window.table !== 'undefined' && window.table !== null) ||
        (typeof window.getTablaActual === 'function' && window.getTablaActual() !== null)
    );
}

/**
 * Obtiene la referencia de la tabla de manera segura
 */
function obtenerTabla() {
    if (typeof table !== 'undefined' && table !== null) {
        return table;
    }

    if (typeof window.table !== 'undefined' && window.table !== null) {
        return window.table;
    }

    if (typeof window.getTablaActual === 'function') {
        return window.getTablaActual();
    }

    return null;
}

/**
 * Función segura para limpiar tabla
 */
function limpiarTablaSafe() {
    const tablaRef = obtenerTabla();
    if (tablaRef && typeof tablaRef.clear === 'function') {
        tablaRef.clear().draw();
        console.log('Tabla limpiada correctamente');
        return true;
    } else {
        console.warn('No se pudo limpiar la tabla - referencia no disponible');
        return false;
    }
}

/**
 * Función segura para agregar fila a tabla
 */
function agregarFilaTablaSafe(datos) {
    const tablaRef = obtenerTabla();
    if (tablaRef && typeof tablaRef.row === 'object' && typeof tablaRef.row.add === 'function') {
        const rowNode = tablaRef.row.add(datos).draw(false).node();
        console.log('Fila agregada correctamente');
        return rowNode;
    } else {
        console.error('No se pudo agregar la fila - tabla no disponible');
        return null;
    }
}

/**
 * Función segura para actualizar totales
 */
function actualizarTotalesSafe() {
    // Intentar diferentes referencias para actualizar totales
    if (typeof actualizarTotales === 'function') {
        actualizarTotales();
        return true;
    }

    if (typeof window.actualizarTotales === 'function') {
        window.actualizarTotales();
        return true;
    }

    if (typeof window.actualizarTotal === 'function') {
        window.actualizarTotal();
        return true;
    }

    console.warn('No se pudo actualizar totales - función no disponible');
    return false;
}

// ===== FUNCIONES DE MANEJO DE SELECCIÓN SEGURAS =====

/**
 * Manejo seguro de selección de productos
 */
async function onProductoSelectSafe(producto) {
    if (!producto) {
        console.error('Producto no válido');
        return;
    }

    try {
        // Verificar que la tabla esté disponible
        if (!esTablaDisponible()) {
            console.error('Tabla no disponible para agregar producto');

            // Mostrar error al usuario
            if (typeof NotificationManager !== 'undefined') {
                NotificationManager.show('error', 'La tabla no está disponible. Recarga la página e intenta nuevamente.');
            }
            return;
        }

        // Llamar a la función global de selección de producto si existe
        if (typeof window.onProductoSelect === 'function') {
            await window.onProductoSelect(producto);
        } else if (typeof onProductoSelect === 'function') {
            await onProductoSelect(producto);
        } else {
            console.error('Función onProductoSelect no encontrada');

            // Fallback: agregar producto manualmente
            await agregarProductoManual(producto);
        }

    } catch (error) {
        console.error('Error en onProductoSelectSafe:', error);

        if (typeof NotificationManager !== 'undefined') {
            NotificationManager.show('error', 'Error al agregar el producto');
        }
    }
}

/**
 * Manejo seguro de selección de clientes
 */
async function onClienteSelectSafe(cliente) {
    if (!cliente) {
        console.error('Cliente no válido');
        return;
    }

    try {
        // Llamar a la función global de selección de cliente si existe
        if (typeof window.onClienteSelect === 'function') {
            await window.onClienteSelect(cliente);
        } else if (typeof onClienteSelect === 'function') {
            await onClienteSelect(cliente);
        } else {
            console.error('Función onClienteSelect no encontrada');

            // Fallback: llenar campos manualmente
            llenarCamposClienteManual(cliente);
        }

    } catch (error) {
        console.error('Error en onClienteSelectSafe:', error);

        if (typeof NotificationManager !== 'undefined') {
            NotificationManager.show('error', 'Error al seleccionar el cliente');
        }
    }
}

/**
 * Fallback para agregar producto manualmente
 */
async function agregarProductoManual(producto) {
    console.log('Agregando producto manualmente:', producto);

    const rowData = [
        producto.c1 || '',
        `<input type="number" class="form-control cantidad" value="1" min="1">`,
        producto.c2 || 'N/A',
        producto.c3 || 'N/A',
        producto.c11 || '',
        `<input type="number" class="form-control precio" value="${parseFloat(producto.precio) || 0}" readonly>`,
        `<input type="number" class="form-control descuento" value="0" min="0" max="100">`,
        `<span class="importe">$${parseFloat(producto.precio) || 0}</span>`,
        producto.c4 || '',
        'N/A'
    ];

    const rowNode = agregarFilaTablaSafe(rowData);
    if (rowNode) {
        // Configurar datos adicionales
        rowNode.dataset.iva = producto.c18 || '16';
        rowNode.dataset.productoId = producto.id || '';

        actualizarTotalesSafe();

        if (typeof NotificationManager !== 'undefined') {
            NotificationManager.show('success', `Producto "${producto.c1}" agregado`);
        }
    }
}

/**
 * Fallback para llenar campos de cliente manualmente
 */
function llenarCamposClienteManual(cliente) {
    console.log('Llenando campos de cliente manualmente:', cliente);

    const campos = [
        { id: 'cliente', value: cliente.c2 || '' },
        { id: 'rfc', value: cliente.c10 || '' },
        { id: 'vendedor', value: cliente.c12 || '' }
    ];

    campos.forEach(campo => {
        const element = document.getElementById(campo.id);
        if (element) {
            element.value = campo.value;
        }
    });

    // Limpiar tabla al seleccionar nuevo cliente
    limpiarTablaSafe();

    if (typeof NotificationManager !== 'undefined') {
        NotificationManager.show('success', `Cliente "${cliente.c2}" seleccionado`);
    }
}

// ===== INICIALIZACIÓN DE BUSCADORES CON VALIDACIÓN =====

/**
 * Inicializa los buscadores cuando el DOM esté listo y las dependencias disponibles
 */
function inicializarBuscadoresSeguro() {
    // Esperar un poco para que se carguen las dependencias
    setTimeout(() => {
        console.log('Inicializando buscadores...');

        // Verificar disponibilidad de dependencias críticas
        if (typeof $ === 'undefined') {
            console.error('jQuery no está disponible');
            return;
        }

        if (typeof bootstrap === 'undefined') {
            console.error('Bootstrap no está disponible');
            return;
        }

        console.log('Dependencias verificadas, buscadores listos');

    }, 1000); // Esperar 1 segundo para que se carguen las dependencias
}

// Auto-inicialización
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', inicializarBuscadoresSeguro);
} else {
    inicializarBuscadoresSeguro();
}

// ===== EXPORTS PARA COMPATIBILIDAD =====
// Hacer funciones disponibles globalmente
window.limpiarTablaSafe = limpiarTablaSafe;
window.agregarFilaTablaSafe = agregarFilaTablaSafe;
window.actualizarTotalesSafe = actualizarTotalesSafe;
window.onProductoSelectSafe = onProductoSelectSafe;
window.onClienteSelectSafe = onClienteSelectSafe;
window.esTablaDisponible = esTablaDisponible;
window.obtenerTabla = obtenerTabla;

console.log('Buscadores.js cargado con validaciones de seguridad');