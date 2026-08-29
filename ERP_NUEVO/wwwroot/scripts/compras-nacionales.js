// Utilerias compartidas por las vistas de Compras Nacionales.
// Depende de site1.js (Currency, dateFormatter, calcularMinutosHabiles, getIconFile).

// Manejador unico de TomSelect para el modulo.
// TomSelectManager vive en site1.js, que el layout carga despues del body,
// por eso la instancia se crea al primer uso y no al cargar el archivo.
Object.defineProperty(window, 'tomCompras', {
    configurable: true,
    get() {
        if (!window.__tomCompras) window.__tomCompras = new TomSelectManager();
        return window.__tomCompras;
    }
});

// Configuracion base de los selects de proveedor
function selectProveedor(id, selector, proveedores, contenedor = 'body') {
    return tomCompras.create(id, {
        selector,
        options: proveedores,
        valueField: 'id',
        labelField: 'nombre',
        displayField: 'nombre',
        searchField: ['clave', 'nombre'],
        dropdownParent: contenedor,
        persist: false,
        createOnBlur: true,
        create: true,
        placeholder: 'Selecciona un proveedor'
    });
}

// ============================================
// FECHAS
// ============================================
function parseFechaDocumento(valor) {
    if (!valor) return null;
    if (valor instanceof Date) return valor;

    const texto = String(valor);
    const fecha = texto.startsWith('/Date(')
        ? new Date(parseInt(texto.match(/\d+/)[0], 10))
        : new Date(texto);

    return isNaN(fecha) ? null : fecha;
}

function fechaCorta(valor) {
    const fecha = parseFechaDocumento(valor);
    return fecha ? dateFormatter(fecha.toISOString()) : '—';
}

// ============================================
// DESCUENTOS
// dto1 es SIEMPRE un porcentaje por partida (0-100).
// El importe descontado del documento es la suma de los descuentos de partida.
// ============================================
function porcentajeDescuento(valor) {
    const pct = parseFloat(valor);
    if (isNaN(pct) || pct < 0) return 0;
    return pct > 100 ? 100 : pct;
}

function brutoPartida(cantidad, precio) {
    return round((parseFloat(cantidad) || 0) * (parseFloat(precio) || 0));
}

function descuentoPartida(cantidad, precio, dto1) {
    return round(brutoPartida(cantidad, precio) * porcentajeDescuento(dto1) / 100);
}

function netoPartida(cantidad, precio, dto1) {
    return round(brutoPartida(cantidad, precio) - descuentoPartida(cantidad, precio, dto1));
}

function totalizarPartidas(partidas, get) {
    const leer = get || (p => p);
    let subtotal = 0, descuento = 0;

    (partidas || []).forEach(p => {
        const { cantidad, precio, dto1 } = leer(p);
        subtotal += brutoPartida(cantidad, precio);
        descuento += descuentoPartida(cantidad, precio, dto1);
    });

    subtotal = round(subtotal);
    descuento = round(descuento);
    return { subtotal, descuento, base: round(subtotal - descuento) };
}

function formatoPorcentaje(valor) {
    return `${porcentajeDescuento(valor).toFixed(2)} %`;
}

// Lee un td/input con formato "% 12.50" o "$ 1,234.56"
function limpiarNumero(texto) {
    if (texto === null || texto === undefined) return 0;
    const limpio = String(texto).replace(/[^0-9.,-]/g, '').replace(/,/g, '');
    const valor = parseFloat(limpio);
    return isNaN(valor) ? 0 : valor;
}

// ============================================
// COLUMNAS ESTANDAR DE UN DOCUMENTO DE COMPRAS
// ============================================
function columnasDocumento(opciones = {}) {
    const { textoObservaciones = 'coment_aut', mostrarDescuento = true } = opciones;

    const columnas = [
        { title: 'Folio', data: 'nuevo_codigo' },
        { title: 'Fecha', data: 'fch', formatter: v => fechaCorta(v) },
        { title: 'Responsable', data: 'responsable', formatter: v => v || '—' },
        { title: 'Subtotal', data: 'sub', formatter: (v, row) => Currency.format(parseFloat(v ?? row.imp) || 0), className: 'text-end' },
    ];

    if (mostrarDescuento) {
        columnas.push({
            title: 'Descuento',
            data: 'dto',
            className: 'text-end',
            formatter: v => {
                const dto = parseFloat(v) || 0;
                return dto > 0
                    ? `<span class="text-danger fw-semibold">- ${Currency.format(dto)}</span>`
                    : '<span class="text-muted">—</span>';
            }
        });
    }

    columnas.push(
        { title: 'Importe', data: 'imp', formatter: v => `<strong>${Currency.format(parseFloat(v) || 0)}</strong>`, className: 'text-end' },
        {
            title: 'Observaciones',
            data: textoObservaciones,
            sortable: textoObservaciones === 'coment_aut',
            formatter: v => v || '—'
        }
    );

    return columnas;
}

// ============================================
// PRIORIDAD POR TIEMPO DE RESPUESTA
// TableBuilder no expone rowCallback: se pinta desde GlobalEvents.onDataLoaded
// usando el data-row-id que asigna a cada <tr>.
// ============================================
function aplicarPrioridadFilas(selector, respuesta, rowKey = 'id_encabezado') {
    const contenedor = document.querySelector(selector);
    if (!contenedor) return;

    const filas = respuesta?.data ?? respuesta ?? [];
    const clases = ['priority-success', 'priority-warning', 'priority-danger'];

    filas.forEach(row => {
        const tr = contenedor.querySelector(`tr[data-row-id="${row[rowKey]}"]`);
        if (!tr) return;

        tr.classList.remove(...clases);

        const aprobado = parseFechaDocumento(row.fch);
        const actual = parseFechaDocumento(row.fechaactual);
        if (!aprobado || !actual || actual < aprobado) return;

        const minutos = calcularMinutosHabiles(aprobado, actual);
        tr.classList.add(minutos <= 120 ? 'priority-success' : minutos <= 240 ? 'priority-warning' : 'priority-danger');
    });
}

// ============================================
// FILTRO POR FOLIO DESDE LA URL (?id=FOLIO)
// Lo consumen las notificaciones internas del ERP.
// ============================================
function folioDeLaUrl() {
    const params = new URLSearchParams(window.location.search);
    return params.get('id') || params.get('folio') || '';
}

// ============================================
// ADJUNTOS
// ============================================
function enlaceAdjunto(ruta, nombreOriginal, extension) {
    if (typeof ruta !== 'string' || !ruta.trim()) {
        return '<i class="fa-solid fa-file-xmark text-muted" title="Sin adjunto" style="font-size:18px"></i>';
    }

    return `<a href="/${ruta}" download="${nombreOriginal || ''}" class="text-success" title="Descargar adjunto">
                <i class="fa-solid ${getIconFile(extension)}" style="font-size:18px"></i>
            </a>`;
}
