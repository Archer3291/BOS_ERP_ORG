// Tabla "Mis tickets" de la vista VerTicket, construida con createTable (TableBuilder.js).
//
// Sustituye al formulario de "ticket id + email" que habia antes: el usuario ya esta
// autenticado, asi que en vez de pedirle el folio de memoria se le listan sus tickets.
//
// Los datos llegan paginados desde /Ticket/MisTickets, que filtra por el usuario de la
// sesion (nunca por un id que venga del cliente).

let tablaMisTickets = null;

// Estado de los filtros. createTable lo lee en cada peticion.
const filtrosMisTickets = {
    estado: '',
    prioridad: ''
};

function escaparHtml(texto) {
    return String(texto ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

function formatoFechaTicket(valor) {
    if (!valor) return '';
    const fecha = new Date(valor);
    if (isNaN(fecha)) return '';
    return fecha.toLocaleDateString('es-MX', {
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit'
    });
}

// El detalle del ticket valida permisos en el servidor (SoporteController.Ticket), asi
// que se navega directo: estas filas ya son del propio usuario.
function abrirTicket(folio) {
    if (!folio) return;
    window.location.href = '/Soporte/Ticket/' + encodeURIComponent(folio);
}

document.addEventListener('DOMContentLoaded', function () {
    if (!document.querySelector('#tablaMisTickets')) return;

    tablaMisTickets = createTable({
        selector: '#tablaMisTickets',
        path: '/Ticket/MisTickets',
        rowKey: 'folio_tkt',
        defaultPageSize: 10,
        searchPlaceholder: 'Buscar por folio, asunto, categoría o descripción...',
        data: () => filtrosMisTickets,
        sortDir: 'desc',
        exports: ['excel', 'pdf'],
        columns: [
            {
                title: 'ID de seguimiento',
                data: 'folio_tkt',
                formatter: (v) => `<a href="#" class="ver-ticket" data-id="${escaparHtml(v)}">${escaparHtml(v)}</a>`
            },
            {
                title: 'Asunto',
                data: 'asunto',
                // El tooltip trae el inicio de la descripcion: da contexto sin abrir el
                // ticket y sin ensanchar la columna.
                formatter: (v, row) => {
                    const detalle = (row.descripcion || v || '').toString();
                    const tooltip = detalle.length > 300 ? detalle.slice(0, 300) + '…' : detalle;
                    return `<span class="tkv-asunto" title="${escaparHtml(tooltip)}">${escaparHtml(v)}</span>`;
                }
            },
            {
                title: 'Categoría',
                data: 'categoria',
                formatter: (v, row) =>
                    `<i class="${escaparHtml(row.icon_class || 'fa-regular fa-circle-question')} me-2 text-muted"></i>${escaparHtml(v)}`
            },
            {
                title: 'Prioridad',
                data: 'prioridad',
                // El color sale del catalogo prio, igual que en el resto del modulo.
                formatter: (v, row) =>
                    `<span class="badge" style="background-color: ${row.color || '#6c757d'}; color:#fff;">${escaparHtml(v)}</span>`
            },
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
            {
                title: 'Atiende',
                data: 'asignado',
                formatter: (v) => v ? escaparHtml(v) : '<span class="text-muted">Sin asignar</span>'
            },
            {
                title: 'Respuestas',
                data: 'respuestas',
                formatter: (v) => Number(v) > 0
                    ? `<span class="badge bg-info">${Number(v)}</span>`
                    : '<span class="text-muted">—</span>'
            },
            { title: 'Actualizado', data: 'fechaactualizacion', formatter: formatoFechaTicket },
            {
                title: 'Documento',
                data: 'folio_doc',
                formatter: (v) => v
                    ? `<span class="text-muted small">${escaparHtml(v)}</span>`
                    : '<span class="text-muted">—</span>'
            }
        ],
        actions: [
            {
                icon: 'fa-eye',
                color: 'cyan-500',
                title: 'Ver detalle',
                onClick: row => abrirTicket(row.folio_tkt)
            }
        ],
        RowsEvents: {
            onRowDblClick: row => abrirTicket(row.folio_tkt)
        }
    });

    // El enlace del folio abre el ticket sin pasar por el boton de acciones.
    document.querySelector('#tablaMisTickets').addEventListener('click', function (e) {
        const enlace = e.target.closest('a.ver-ticket');
        if (!enlace) return;
        e.preventDefault();
        abrirTicket(enlace.dataset.id);
    });

    engancharFiltros();
});

// ── Filtros ─────────────────────────────────────────────────────────────────
function engancharFiltros() {
    const aplicar = () => {
        filtrosMisTickets.estado = document.getElementById('filtroEstado')?.value ?? '';
        filtrosMisTickets.prioridad = document.getElementById('filtroPrioridad')?.value ?? '';
        tablaMisTickets.reload();
    };

    ['filtroEstado', 'filtroPrioridad'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', aplicar);
    });

    document.getElementById('btnLimpiarFiltros')?.addEventListener('click', function () {
        ['filtroEstado', 'filtroPrioridad'].forEach(id => {
            const select = document.getElementById(id);
            if (select) select.value = '';
        });
        aplicar();
    });
}
