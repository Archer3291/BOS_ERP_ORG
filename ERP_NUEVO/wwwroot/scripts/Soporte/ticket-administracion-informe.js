
let graficoEstadoChart = null;
let graficoPrioridadChart = null;
let graficoTendenciaChart = null;
let tableInforme = null;
function onCambioTipoFecha() {
    const tipo = this.value;
    // Ocultar todos los contenedores
    document.getElementById('contenedorFechaSimple').classList.add('d-none');
    document.getElementById('contenedorFechaDia').classList.add('d-none');
    document.getElementById('contenedorFechaAnio').classList.add('d-none');
    document.getElementById('contenedorFechaRango').classList.add('d-none');

    if (tipo === 'mes') {
        document.getElementById('contenedorFechaSimple').classList.remove('d-none');
    } else if (tipo === 'dia') {
        document.getElementById('contenedorFechaDia').classList.remove('d-none');
    } else if (tipo === 'anio') {
        document.getElementById('contenedorFechaAnio').classList.remove('d-none');
    } else if (tipo === 'rango') {
        document.getElementById('contenedorFechaRango').classList.remove('d-none');
    }
}
const estadoColor = estado =>
    estado === "Crítico" ? "danger" :
        estado === "Resuelto" ? "success" : "warning";

const estadoIcono = estado =>
    estado === "Crítico" ? "report_problem" :
        estado === "Resuelto" ? "check_circle" : "hourglass_top";

document.addEventListener("DOMContentLoaded", () => {
    // Todo el enganchado vive aqui: el partial de informes puede no estar en el DOM
    // cuando se evalua el archivo, y antes esto reventaba con addEventListener de null.
    document.getElementById('tipoFiltroFecha')?.addEventListener('change', onCambioTipoFecha);
    document.getElementById("btnAplicarFiltros")?.addEventListener("click", () => cargarDatosInforme(true));
    document.getElementById("btnLimpiarFiltros1")?.addEventListener("click", limpiarFiltros);

    // Catalogos de los filtros, antes pedidos en el nivel superior del archivo.
    fetch('/Informe/DatosSelect')
        .then(response => response.json())
        .then(data => {
            llenarSelect('filtroEstado1', data.estatus, 'id_stat_tkt', 'n', 'Todos');
            llenarSelect('filtroPrioridad1', data.prioridades, 'id_prio', 'n', 'Todas');
        })
        .catch(error => {
            console.error('Error al obtener los datos:', error);
        });

    inicializarTablaDetalle();       // se construye antes de la primera carga
    cargarDatosInforme(); // Primera carga con datos reales
});
// Devuelve los filtros como objeto plano: createTable los adjunta a cada peticion,
// y los graficos los convierten a FormData. Antes solo existia la version FormData.
function obtenerFiltros() {
    const filtros = {};
    const estado = document.getElementById("filtroEstado1")?.value;
    const prioridad = document.getElementById("filtroPrioridad1")?.value;
    const tipoFecha = document.getElementById("tipoFiltroFecha")?.value;

    if (estado) filtros.idEstado = estado;
    if (prioridad) filtros.idPrioridad = prioridad;

    if (tipoFecha === "mes") {
        const fechaMes = document.getElementById("filtroFechaMes")?.value;
        if (fechaMes) {
            const [anio, mes] = fechaMes.split("-");
            filtros.fechaDesde = `${anio}-${mes}-01`;
            // Dia 0 del mes siguiente = ultimo dia de este mes, sin pasar por UTC.
            const ultimo = new Date(Number(anio), Number(mes), 0);
            filtros.fechaHasta = `${anio}-${mes}-${String(ultimo.getDate()).padStart(2, '0')}`;
        }
    } else if (tipoFecha === "dia") {
        const fechaDia = document.getElementById("filtroFechaDia")?.value;
        if (fechaDia) { filtros.fechaDesde = fechaDia; filtros.fechaHasta = fechaDia; }
    } else if (tipoFecha === "anio") {
        const anio = document.getElementById("filtroFechaAnio")?.value;
        if (anio) { filtros.fechaDesde = `${anio}-01-01`; filtros.fechaHasta = `${anio}-12-31`; }
    } else if (tipoFecha === "rango") {
        const d = document.getElementById("filtroFechaDesde")?.value;
        const h = document.getElementById("filtroFechaHasta")?.value;
        if (d) filtros.fechaDesde = d;
        if (h) filtros.fechaHasta = h;
    }

    return filtros;
}

function filtrosComoFormData(filtros) {
    const fd = new FormData();
    Object.entries(filtros).forEach(([k, v]) => {
        if (v !== undefined && v !== null && v !== '') fd.append(k, v);
    });
    return fd;
}

function limpiarFiltros() {
    document.getElementById('filtroEstado1').value = '';
    document.getElementById('filtroPrioridad1').value = '';
    document.getElementById('tipoFiltroFecha').value = 'mes';

    // Restablecer inputs de fecha
    document.getElementById('filtroFechaMes').value = '';
    document.getElementById('filtroFechaDia').value = '';
    document.getElementById('filtroFechaAnio').value = '';
    document.getElementById('filtroFechaDesde').value = '';
    document.getElementById('filtroFechaHasta').value = '';

    // Mostrar contenedor de mes y ocultar los demás
    document.getElementById('contenedorFechaSimple').classList.remove('d-none');
    document.getElementById('contenedorFechaDia').classList.add('d-none');
    document.getElementById('contenedorFechaAnio').classList.add('d-none');
    document.getElementById('contenedorFechaRango').classList.add('d-none');
    cargarDatosInforme(false);
}
function cargarDatosInforme(conFiltros = false) {
    const filtros = conFiltros ? obtenerFiltros() : {};

    fetch('/Informe/DatosInforme', { method: "POST", body: filtrosComoFormData(filtros) })
        .then(res => res.json())
        .then(data => {
            const totalTickets = data.conteoTickets[0].todos || 0;
            const estadoData = data.conteoEstadoActivo || [];
            const prioridadData = data.conteoPrioridad || [];
            const fechaData = data.conteoFecha || [];

            document.getElementById("totalTickets").innerText = totalTickets;
            document.getElementById("ticketsPendientes").innerText = obtenerPendientes(estadoData);
            document.getElementById("ticketsResueltos").innerText = obtenerConteoPorEstado(estadoData, "Resuelto");

            renderGraficosDesdeDatos(estadoData, prioridadData, fechaData);
        })
        .catch(err => console.error("Error al obtener datos del informe:", err));

    // La tabla ya no se alimenta de esta respuesta: tiene su propio endpoint
    // paginado y solo hay que recargarla con los filtros vigentes.
    if (tableInforme) tableInforme.reload();
}

function obtenerConteoPorEstado(data, nombre) {
    return data.find(e => e.estado === nombre)?.totaltickets || 0;
}
function obtenerPendientes(data) {
    return data
        .filter(e => e.estado !== "Resuelto")
        .reduce((acc, curr) => acc + curr.totaltickets, 0);
}
// Recibe la fecha tal como la serializa .NET Core (ISO 8601). Antes esperaba el
// formato /Date(ms)/ del ASP.NET clasico, asi que el eje del grafico de tendencia
// mostraba 01/01/1970 en todos los puntos.
function formatearFecha(fecha) {
    if (!fecha) return '';
    const d = new Date(fecha);
    if (isNaN(d)) return '';
    return d.toLocaleDateString('es-MX', {
        year: 'numeric', month: '2-digit', day: '2-digit'
    });
}
function renderGraficosDesdeDatos(estadoData, prioridadData, fechaData) {
    [graficoEstadoChart, graficoPrioridadChart, graficoTendenciaChart].forEach(chart => chart?.destroy());

    graficoEstadoChart = new Chart("graficoEstado", {
        type: "pie",
        data: {
            labels: estadoData.map(e => e.estado),
            datasets: [{
                data: estadoData.map(e => e.totaltickets),
                backgroundColor: ['#fbbc05', '#34a853', '#ea4335', '#4285f4', '#9c27b0']
            }]
        }
    });

    graficoPrioridadChart = new Chart("graficoPrioridad", {
        type: "doughnut",
        data: {
            labels: prioridadData.map(p => p.n),
            datasets: [{
                label: "Tickets",
                data: prioridadData.map(p => p.totaltickets),
                backgroundColor: ['#4285f4', '#fbbc05', '#34a853', '#ea4335', '#9c27b0']
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { position: 'bottom' }
            }
        }
    });

    graficoTendenciaChart = new Chart("graficoTendencia", {
        type: "line",
        data: {
            labels: fechaData.map(f => formatearFecha(f.fecha)),
            datasets: [{
                label: "Tickets por día",
                data: fechaData.map(f => Number(f.totaltickets)),
                borderColor: "#007bff",
                backgroundColor: "#007bff22",
                fill: true,
                tension: 0.3
            }]
        },
        options: {
            responsive: true,
            scales: {
                y: {
                    beginAtZero: true,
                    ticks: { precision: 0 }
                }
            }
        }
    });
}
// Tabla de detalle con createTable (TableBuilder): genera su propio <thead> desde
// "columns" y pide los datos paginados, en lugar de recibir el listado completo
// dentro de la respuesta del informe.
function inicializarTablaDetalle() {
    if (!document.querySelector('#tablaTicketsAdmin')) return;

    tableInforme = createTable({
        selector: '#tablaTicketsAdmin',
        path: '/Informe/DetalleTickets',
        rowKey: 'folio_tkt',
        defaultPageSize: 10,
        searchPlaceholder: 'Buscar por folio, asunto, usuario o categoría...',
        data: () => obtenerFiltros(),
        sortDir: 'desc',
        exports: ['excel', 'pdf'],
        columns: [
            { title: 'ID', data: 'folio_tkt' },
            { title: 'Asunto', data: 'asunto' },
            { title: 'Categoría', data: 'categoria' },
            {
                title: 'Prioridad',
                data: 'prioridad',
                formatter: (v, row) =>
                    `<span class="badge" style="background-color: ${row.color || '#6c757d'}; color:#fff;">${escaparHtmlInf(v)}</span>`
            },
            { title: 'Estado', data: 'estado' },
            { title: 'Última actualización', data: 'fechaactualizacion', formatter: formatearFechaHora },
            { title: 'Usuario creador', data: 'creador' },
            { title: 'Asignado a', data: 'asignado' },
            { title: 'Última respuesta', data: 'ultimarespuesta' }
        ]
    });
}

function escaparHtmlInf(t) {
    return String(t ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

function formatearFechaHora(valor) {
    if (!valor) return '';
    const d = new Date(valor);
    if (isNaN(d)) return '';
    return d.toLocaleDateString('es-MX', {
        year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit'
    });
}

function llenarSelect(selectId, datos, valueKey, textKey, placeholder) {
    const select = document.getElementById(selectId);
    if (!select) return;                 // el partial puede no estar montado

    select.innerHTML = `<option value="">${placeholder || 'Todos'}</option>`;

    (datos || []).forEach(item => {
        const option = document.createElement('option');
        option.value = item[valueKey];
        option.textContent = item[textKey];
        select.appendChild(option);
    });
}


