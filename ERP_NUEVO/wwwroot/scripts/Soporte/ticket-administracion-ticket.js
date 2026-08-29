// Tabla de administración de tickets, construida con createTable (TableBuilder.js).
//
// Sustituye a DataTables: createTable genera el <thead> desde "columns", así que no hay
// encabezados que mantener sincronizados a mano — el desajuste entre <th> y columnas era
// lo que provocaba "Cannot read properties of undefined (reading 'style')".
//
// Los datos llegan paginados desde el servidor: /Ticket/TicketTodos recibe page, pageSize,
// nombre (búsqueda), sortColumn/sortDir y los filtros de la barra superior.

let tablaTickets = null;

// Estado de los filtros. createTable lo lee en cada petición, así que basta con
// recargar la tabla para que se apliquen.
const filtrosTickets = {
    estado: '',
    prioridad: '',
    creador: '',
    asignado: ''
};

function formatoFechaTicket(valor) {
    if (!valor) return '';
    const fecha = new Date(valor);
    if (isNaN(fecha)) return '';
    return fecha.toLocaleDateString('es-MX', {
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit'
    });
}

function escaparHtml(texto) {
    return String(texto ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

document.addEventListener('DOMContentLoaded', function () {
    if (!document.querySelector('#tablaTickets')) return;

    tablaTickets = createTable({
        selector: '#tablaTickets',
        path: '/Ticket/TicketTodos',
        rowKey: 'folio_tkt',
        defaultPageSize: 10,
        searchPlaceholder: 'Buscar por folio, asunto, usuario o categoría...',
        data: () => filtrosTickets,
        sortDir: 'desc',
        exports: ['excel', 'pdf'],
        selectable: {
            enabled: true,
            // La asignación masiva trabaja con folios.
            map: row => row.folio_tkt
        },
        columns: [
            {
                title: 'ID de seguimiento',
                data: 'folio_tkt',
                formatter: (v) => `<a href="#" class="ver-ticket" data-id="${escaparHtml(v)}">${escaparHtml(v)}</a>`
            },
            { title: 'Actualizado', data: 'fechaactualizacion', formatter: formatoFechaTicket },
            { title: 'Categoría', data: 'categoria' },
            { title: 'Usuario', data: 'creador' },
            { title: 'Asunto', data: 'asunto' },
            {
                title: 'Estado',
                data: 'estado',
                formatter: (v) => {
                    let clase = 'secondary';
                    if (v === 'Abierto') clase = 'success';
                    else if (v === 'Resuelto') clase = 'primary';
                    else if (v === 'En espera') clase = 'warning';
                    return `<span class="badge bg-${clase}">${escaparHtml(v)}</span>`;
                }
            },
            { title: 'Última respuesta', data: 'ultimarespuesta' },
            {
                title: 'Prioridad',
                data: 'prioridad',
                // El color sale del catálogo prio, igual que en el resto del módulo.
                formatter: (v, row) =>
                    `<span class="badge" style="background-color: ${row.color || '#6c757d'}; color:#fff;">${escaparHtml(v)}</span>`
            },
            { title: 'Asignado', data: 'asignado' }
        ],
        actions: [
            {
                icon: 'fa-eye',
                color: 'cyan-500',
                title: 'Ver ticket',
                onClick: row => abrirTicket(row.folio_tkt)
            }
        ],
        RowsEvents: {
            onRowDblClick: row => abrirTicket(row.folio_tkt)
        }
    });

    // El enlace del folio abre el ticket sin esperar al botón de acciones.
    document.querySelector('#tablaTickets').addEventListener('click', function (e) {
        const enlace = e.target.closest('a.ver-ticket');
        if (!enlace) return;
        e.preventDefault();
        abrirTicket(enlace.dataset.id);
    });

    engancharFiltros();
    cargarCatalogos();
    engancharAsignacionMasiva();
});

// ── Filtros ─────────────────────────────────────────────────────────────────
function engancharFiltros() {
    const aplicar = () => {
        filtrosTickets.estado = document.getElementById('filtroEstado')?.value ?? '';
        filtrosTickets.prioridad = document.getElementById('filtroPrioridad')?.value ?? '';
        filtrosTickets.creador = document.getElementById('filtroUsuario')?.value ?? '';
        filtrosTickets.asignado =
            document.querySelector('input[name="filtroAsignado"]:checked')?.value ?? '';
        tablaTickets.reload();
    };

    ['filtroEstado', 'filtroPrioridad', 'filtroUsuario'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', aplicar);
    });
    document.querySelectorAll('input[name="filtroAsignado"]')
        .forEach(r => r.addEventListener('change', aplicar));

    document.getElementById('btnLimpiarFiltros')?.addEventListener('click', function () {
        ['filtroEstado', 'filtroPrioridad', 'filtroUsuario'].forEach(id => {
            const s = document.getElementById(id);
            if (s) s.value = '';
        });
        const todos = document.querySelector('input[name="filtroAsignado"][value=""]');
        if (todos) todos.checked = true;
        aplicar();
    });
}

// ── Catálogos de los filtros ────────────────────────────────────────────────
function cargarCatalogos() {
    fetch('/Soporte/DatosSelect')
        .then(r => r.json())
        .then(data => {
            // Los filtros mandan ids al servidor, no nombres.
            llenarSelect('filtroEstado', data.estatus, 'id_stat_tkt', 'n', 'Todos');
            llenarSelect('filtroPrioridad', data.prioridades, 'id_prio', 'n', 'Todas');
            llenarSelect('filtroUsuario', data.usuarioPropietario, 'id_usr', 'nombre', 'Todos');
            llenarSelect('nuevoResponsable', data.usuarios, 'id_usr', 'nombre', 'Selecciona un responsable');

            // "Asignados a mí" filtra por id, no por nombre de usuario.
            const yo = data.usuarioActual && data.usuarioActual[0];
            const radioMio = document.getElementById('asignadosMiUsuario');
            if (radioMio && yo) radioMio.value = yo.usuarioid ?? '';
        })
        .catch(err => console.error('Error al obtener los catálogos:', err));
}

function llenarSelect(selectId, datos, valueKey, textKey, placeholder) {
    const select = document.getElementById(selectId);
    if (!select) return;

    select.innerHTML = `<option value="">${placeholder || 'Todos'}</option>`;
    (datos || []).forEach(item => {
        const option = document.createElement('option');
        option.value = item[valueKey];
        option.textContent = item[textKey];
        select.appendChild(option);
    });
}

// ── Navegación al detalle ───────────────────────────────────────────────────
function abrirTicket(folio) {
    if (!folio) return;

    const formData = new FormData();
    formData.append('ticketId', folio);
    formData.append('__RequestVerificationToken',
        document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '');

    fetch('/BuscarTickets/BuscarT', { method: 'POST', body: formData })
        .then(res => res.json())
        .then(data => {
            if (data.success && data.redirectUrl) window.location.href = data.redirectUrl;
            else toastr.error(data.error || 'No se pudo abrir el ticket.');
        })
        .catch(error => toastr.error('Error de conexión: ' + error.message));
}

// ── Asignación masiva ───────────────────────────────────────────────────────
function engancharAsignacionMasiva() {
    document.getElementById('btnAccion')?.addEventListener('click', function () {
        const folios = tablaTickets.getSelectedRows();

        if (!folios.length) {
            toastr.warning('No hay tickets seleccionados.');
            return;
        }

        const nuevoResponsable = document.getElementById('nuevoResponsable')?.value;
        if (!nuevoResponsable) {
            toastr.warning('Selecciona un responsable válido.');
            return;
        }

        const formData = new FormData();
        formData.append('__RequestVerificationToken',
            document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '');
        formData.append('usuario', nuevoResponsable);
        folios.forEach(f => formData.append('tickets', f));

        fetch('/Ticket/HistorialAsignacionMultiple', {
            method: 'POST',
            body: formData,
            credentials: 'same-origin'
        })
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    toastr.success(data.message);
                } else {
                    // Los errores vienen como lista; se muestran en líneas separadas.
                    toastr.error((data.errores || [data.message]).join('<br>'));
                }
                tablaTickets.clearSelection();
                tablaTickets.reload();
            })
            .catch(error => {
                console.error('Error en la asignación:', error);
                toastr.error('Ocurrió un error en la solicitud.');
            });
    });
}
