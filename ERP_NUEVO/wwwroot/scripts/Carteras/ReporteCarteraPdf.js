// ============================================================
//  REPORTE DE CARTERA — Generador jsPDF (client-side)
//  Sustituye la exportación via Rotativa/ViewAsPdf del servidor.
//  Requiere: jsPDF + jsPDF-AutoTable ya cargados globalmente.
//
//  Punto de entrada público:
//    exportarCarteraPDF(modo, filtros, columnas)
//
//  modo     → 'facturas' | 'clientes'
//  filtros  → objeto con los valores actuales del panel de filtros
//  columnas → array de keys visibles, ej. ['cliente','folio','monto_total',...]
// ============================================================

/* ────────────────────────────────────────────────────────────
   DESIGN TOKENS — igual que el resto del sistema PDF
   ──────────────────────────────────────────────────────────── */
const CARTERA_THEME = {
    primary: [15, 52, 96],
    accent: [0, 120, 215],
    textDark: [30, 30, 30],
    textMid: [90, 90, 90],
    textLight: [160, 160, 160],
    white: [255, 255, 255],
    rowAlt: [248, 250, 253],
    bgLight: [245, 247, 250],
    borderLight: [220, 225, 232],
    success: [39, 174, 96],
    danger: [192, 57, 43],
    warning: [217, 119, 6],
    kpiBg: [239, 246, 255],
    totalBg: [254, 243, 199],
};

/* ────────────────────────────────────────────────────────────
   CATÁLOGO DE COLUMNAS
   Define qué columna del objeto de datos corresponde a cada key
   y cómo formatear su valor en el PDF.
   ──────────────────────────────────────────────────────────── */
const CARTERA_COL_CATALOG = {
    cliente: { title: 'Cliente', field: 'n_cli', type: 'text', w: 'auto' },
    cve_cli: { title: 'Código', field: 'cve_cli', type: 'text', w: 'auto' },
    rfc: { title: 'RFC', field: 'rfc', type: 'text', w: 'auto' },
    zona: { title: 'Zona', field: 'cve_zona', type: 'text', w: 'auto' },
    clave_vendedor: { title: 'Clave Vendedor', field: 'clave_vendedor', type: 'text', w: 'auto' },
    nombre_vendedor: { title: 'Nombre Vendedor', field: 'nombre_vendedor', type: 'text', w: 'auto' },
    folio: { title: 'Folio', field: 'folio', type: 'text', w: 'auto' },
    tipo_doc: { title: 'Tipo Doc', field: 'nat', type: 'text', w: 'auto' },
    tipo_proceso: { title: 'Tipo Proceso', field: 'tipo_proceso', type: 'text', w: 'auto' },
    fecha_emision: { title: 'Fecha Emisión', field: 'fecha_emision', type: 'date', w: 'auto' },
    fecha_venc: { title: 'Fecha Vencim.', field: 'fecha_vencimiento', type: 'date', w: 'auto' },
    dias_vencido: { title: 'Días Vencido', field: 'dias_vencido', type: 'int', w: 'auto' },
    monto_total: { title: 'Monto Total', field: 'monto_total', type: 'currency', w: 'auto' },
    cobrado: { title: 'Cobrado', field: 'cobrado', type: 'currency', w: 'auto' },
    saldo_pend: { title: 'Saldo Pendiente', field: 'saldo_pendiente', type: 'currency', w: 'auto' },
    pct_cobro: { title: '% Cobro', field: null, type: 'pct', w: 'auto' },
    estado: { title: 'Estado', field: 'estado', type: 'text', w: 'auto' },
    saldo_favor: { title: 'Saldo a Favor', field: 'saldo_favor', type: 'currency', w: 'auto' },
    moneda: { title: 'Moneda', field: 'moneda', type: 'text', w: 'auto' },
    dias_cobro: { title: 'Días Prom. Cobro', field: 'dias_cobro', type: 'int', w: 'auto' },
    uuid: { title: 'UUID Factura', field: 'factura_uuid', type: 'uuid', w: 'auto' },
};

/* ────────────────────────────────────────────────────────────
   HELPERS LOCALES
   ──────────────────────────────────────────────────────────── */

function _toDecimal(row, field) {
    if (!row || !field) return 0;
    const v = row[field];
    if (v === null || v === undefined) return 0;
    const n = parseFloat(v);
    return isNaN(n) ? 0 : n;
}

function _fmtPct(row) {
    const total = _toDecimal(row, 'monto_total');
    const cobrado = _toDecimal(row, 'cobrado');
    if (!total) return '100%';
    return (Math.round((cobrado / total) * 1000) / 10).toFixed(1) + '%';
}

function _fmtUuid(v) {
    if (!v) return '—';
    return String(v).length > 26 ? String(v).slice(0, 24) + '…' : v;
}

function _cellValue(row, key) {
    const def = CARTERA_COL_CATALOG[key];
    if (!def) return '';
    if (key === 'pct_cobro') return _fmtPct(row);
    const raw = def.field ? row[def.field] : null;
    switch (def.type) {
        case 'currency': return Currency.format(raw);
        case 'date': return dateFormatter(raw);
        case 'int': return (raw !== null && raw !== undefined) ? String(Math.round(raw)) : '—';
        case 'uuid': return _fmtUuid(raw);
        default: return raw !== null && raw !== undefined ? String(raw) : '—';
    }
}

function _computeKpis(rows) {
    const now = new Date();
    return {
        totalFacturas: rows.length,
        totalMonto: rows.reduce((s, r) => s + _toDecimal(r, 'monto_total'), 0),
        totalCobrado: rows.reduce((s, r) => s + _toDecimal(r, 'cobrado'), 0),
        totalPendiente: rows.reduce((s, r) => s + _toDecimal(r, 'saldo_pendiente'), 0),
        facturasVencidas: rows.filter(r => {
            const fv = r.fecha_vencimiento ? new Date(r.fecha_vencimiento) : null;
            return fv && fv < now && _toDecimal(r, 'saldo_pendiente') > 0;
        }).length,
        montoVencido: rows
            .filter(r => {
                const fv = r.fecha_vencimiento ? new Date(r.fecha_vencimiento) : null;
                return fv && fv < now && _toDecimal(r, 'saldo_pendiente') > 0;
            })
            .reduce((s, r) => s + _toDecimal(r, 'saldo_pendiente'), 0),
    };
}

/* ────────────────────────────────────────────────────────────
   ENCABEZADO CORPORATIVO
   Reutiliza el mismo patrón visual del sistema existente.
   ──────────────────────────────────────────────────────────── */
function _encabezado(doc, logoBase64, datosEmpresa, fecha) {
    const W = doc.internal.pageSize.getWidth();
    const T = CARTERA_THEME;

    // Franja lateral
    doc.setFillColor(...T.primary);
    doc.rect(10, 10, 4, 32, 'F');

    // Logo
    if (logoBase64) {
        try { doc.addImage(logoBase64, 'PNG', 18, 16, 26, 18); } catch (_) { }
    }

    const cx = 50;

    // Razón social
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(11);
    doc.setTextColor(...T.primary);
    doc.text((datosEmpresa && datosEmpresa.razon_social) || 'EMPRESA', cx, 19);

    // RFC + datos breves
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(7.5);
    doc.setTextColor(...T.textMid);
    let ly = 25;
    if (datosEmpresa && datosEmpresa.RFC) { doc.text(`RFC: ${datosEmpresa.RFC}`, cx, ly); ly += 4; }
    if (datosEmpresa && datosEmpresa.direccion) { doc.text(datosEmpresa.direccion.slice(0, 80), cx, ly); ly += 4; }
    if (datosEmpresa && datosEmpresa.telefono) { doc.text(`Tel: ${datosEmpresa.telefono}`, cx, ly); }

    // Fecha (esquina derecha)
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(7);
    doc.setTextColor(...T.textMid);
    doc.text('FECHA', W - 12, 19, { align: 'right' });
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(8.5);
    doc.setTextColor(...T.textDark);
    doc.text(fecha || '', W - 12, 25, { align: 'right' });

    // Línea inferior del banner
    doc.setDrawColor(...T.borderLight);
    doc.setLineWidth(0.6);
    doc.line(10, 44, W - 10, 44);
}

/* ────────────────────────────────────────────────────────────
   BANDA DE TÍTULO + FILTROS
   ──────────────────────────────────────────────────────────── */
function _tituloBanda(doc, titulo, filtrosTexto) {
    const W = doc.internal.pageSize.getWidth();
    const T = CARTERA_THEME;

    doc.setFillColor(...T.bgLight);
    doc.rect(10, 47, W - 20, 14, 'F');

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(9.5);
    doc.setTextColor(...T.primary);
    doc.text(titulo.toUpperCase(), 14, 56);

    if (filtrosTexto) {
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(7);
        doc.setTextColor(...T.textLight);
        doc.text(filtrosTexto, W - 12, 56, { align: 'right', maxWidth: W - 130 });
    }
}

/* ────────────────────────────────────────────────────────────
   TARJETAS DE KPIs (fila horizontal)
   ──────────────────────────────────────────────────────────── */
function _kpiRow(doc, kpis, startY) {
    const W = doc.internal.pageSize.getWidth();
    const T = CARTERA_THEME;
    const margin = 10;
    const usable = W - margin * 2;

    const items = [
        { label: 'Facturas', value: String(kpis.totalFacturas) },
        { label: 'Monto Total', value: Currency.format(kpis.totalMonto) },
        { label: 'Cobrado', value: Currency.format(kpis.totalCobrado) },
        { label: 'Pendiente', value: Currency.format(kpis.totalPendiente) },
        { label: 'Vencidas', value: String(kpis.facturasVencidas) },
        { label: 'Monto Vencido', value: Currency.format(kpis.montoVencido) },
    ];

    const cellW = usable / items.length;
    const boxH = 16;

    items.forEach((item, i) => {
        const x = margin + i * cellW;

        doc.setFillColor(...T.kpiBg);
        doc.rect(x, startY, cellW - 2, boxH, 'F');
        doc.setDrawColor(...T.borderLight);
        doc.setLineWidth(0.3);
        doc.rect(x, startY, cellW - 2, boxH, 'S');

        doc.setFont('helvetica', 'bold');
        doc.setFontSize(6.5);
        doc.setTextColor(...T.textLight);
        doc.text(item.label.toUpperCase(), x + (cellW - 2) / 2, startY + 5, { align: 'center' });

        doc.setFont('helvetica', 'bold');
        doc.setFontSize(8);
        doc.setTextColor(...T.primary);
        doc.text(item.value, x + (cellW - 2) / 2, startY + 12, { align: 'center' });
    });

    return startY + boxH + 4;
}

/* ────────────────────────────────────────────────────────────
   TABLA PRINCIPAL — autoTable
   ──────────────────────────────────────────────────────────── */
function _tabla(doc, rows, colKeys, startY) {
    const T = CARTERA_THEME;
    const W = doc.internal.pageSize.getWidth();
    const defs = colKeys.split(',').map(k => CARTERA_COL_CATALOG[k]).filter(Boolean);

    // Calcular anchos relativos
    const totalW = defs.reduce((s, d) => s + d.w, 0);
    const usable = W - 20; // margen 10 a cada lado

    const columns = colKeys.split(',').map(k => {
        const def = CARTERA_COL_CATALOG[k];
        return { header: def.title, dataKey: k };
    });

    const body = rows.map(row => {
        const obj = {};
        colKeys.split(',').forEach(k => { obj[k] = _cellValue(row, k); });
        return obj;
    });

    // Calcular anchos proporcionales por columna
    const colStyles = {};
    colKeys.split(',').forEach((k, i) => {
        const def = CARTERA_COL_CATALOG[k];
        const w = Math.round((def.w / totalW) * usable);
        const halign = ['currency', 'pct', 'int'].includes(def.type) ? 'right' : 'left';
        colStyles[k] = { cellWidth: w, halign };
    });

    doc.autoTable({
        head: [columns.map(c => c.header)],
        body: body.map(row => colKeys.split(',').map(k => row[k])),
        startY,
        margin: { left: 10, right: 10 },
        styles: {
            fontSize: 7.5,
            cellPadding: { top: 2.5, bottom: 2.5, left: 3, right: 3 },
            lineColor: T.borderLight,
            lineWidth: 0.15,
            textColor: T.textDark,
            font: 'helvetica',
            overflow: 'ellipsize',
        },
        headStyles: {
            fillColor: T.primary,
            textColor: T.white,
            fontStyle: 'bold',
            halign: 'center',
            fontSize: 7,
            cellPadding: { top: 3, bottom: 3, left: 3, right: 3 },
        },
        alternateRowStyles: { fillColor: T.rowAlt },
        //columnStyles,
        didParseCell: (data) => {
            // Colorear días vencidos
            if (data.section === 'body' && colKeys.split(',')[data.column.index] === 'dias_vencido') {
                const dias = parseInt(data.cell.raw) || 0;
                if (dias > 90) data.cell.styles.textColor = T.danger;
                else if (dias > 60) data.cell.styles.textColor = T.warning;
                else if (dias > 30) data.cell.styles.textColor = [59, 130, 246];
                else if (dias > 0) data.cell.styles.textColor = [16, 185, 129];
            }
            // Colorear estado
            if (data.section === 'body' && colKeys.split(',')[data.column.index] === 'estado') {
                const est = (data.cell.raw || '').toLowerCase();
                if (est === 'vencido') data.cell.styles.textColor = T.danger;
                else if (est === 'cobrado') data.cell.styles.textColor = T.success;
                else if (est === 'parcial') data.cell.styles.textColor = [59, 130, 246];
                else data.cell.styles.textColor = T.warning;
                data.cell.styles.fontStyle = 'bold';
            }
            // Parceo de fecha
            if (data.section === 'body' && colKeys.split(',')[data.column.index] === 'fecha_emision') {
                const fch = (data.cell.raw || '');
                data.cell.text = dateFormatter(fch);
            }
            if (data.section === 'body' && colKeys.split(',')[data.column.index] === 'fecha_venc') {
                const fch = (data.cell.raw || '');
                data.cell.text = dateFormatter(fch);
            }
            // Formatear tipo proceso
            if (data.section === 'body' && colKeys.split(',')[data.column.index] === 'tipo_proceso') {
                const tp = (data.cell.raw || '');
                data.cell.styles.textColor = [37, 99, 235];
                data.cell.styles.fontStyle = 'bold';
            }
        },
    });

    return doc.lastAutoTable.finalY;
}

/* ────────────────────────────────────────────────────────────
   FILA DE TOTALES
   ──────────────────────────────────────────────────────────── */
function _totalesRow(doc, rows, colKeys, finalY) {
    const T = CARTERA_THEME;
    const W = doc.internal.pageSize.getWidth();
    const defs = colKeys.split(',').map(k => CARTERA_COL_CATALOG[k]).filter(Boolean)
    const usable = W - 20;
    const colW = usable / defs.length;

    const currencyKeys = colKeys.split(',').filter(k => {
        const d = CARTERA_COL_CATALOG[k];
        return d && d.type === 'currency';
    });

    if (!currencyKeys.length) return;

    const rowY = finalY + 1;
    const rowH = 8;

    // Fondo
    doc.setFillColor(...T.totalBg);
    doc.rect(10, rowY, W - 20, rowH, 'F');
    doc.setDrawColor(...[215, 119, 6]);
    doc.setLineWidth(0.6);
    doc.rect(10, rowY, W - 20, rowH, 'S');

    // Etiqueta "TOTALES" en la primera columna
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(7);
    doc.setTextColor(...T.textDark);
    doc.text('TOTALES', 13, rowY + rowH / 2 + 2);

    // Calcular posición X de cada columna para alinear los totales
    let xOffset = 10;

    colKeys.split(',').forEach((k) => {
        const def = CARTERA_COL_CATALOG[k];

        if (currencyKeys.includes(k)) {
            const sum = rows.reduce((s, r) => s + _toDecimal(r, def.field), 0);

            const txt = Currency.format(sum);

            doc.setFont('helvetica', 'bold');
            doc.setFontSize(7);
            doc.setTextColor(...T.primary);

            doc.text(
                txt,
                xOffset + colW - 3,
                rowY + rowH / 2 + 2,
                { align: 'right' }
            );
        }

        xOffset += colW;
    });
}

/* ────────────────────────────────────────────────────────────
   SECCIÓN POR CLIENTE — encabezado de sección
   ──────────────────────────────────────────────────────────── */
function _seccionHeader(doc, titulo, kpis, y) {
    const W = doc.internal.pageSize.getWidth();
    const T = CARTERA_THEME;

    // Barra lateral de acento
    doc.setFillColor(...T.bgLight);
    doc.rect(10, y, W - 20, 11, 'F');
    doc.setFillColor(...T.accent);
    doc.rect(10, y, 3, 11, 'F');

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(8.5);
    doc.setTextColor(...T.primary);
    doc.text(titulo, 17, y + 7.5);

    // Mini KPIs a la derecha
    if (kpis) {
        const parts = [
            `Facturas: ${kpis.totalFacturas}`,
            `Total: ${Currency.format(kpis.totalMonto)}`,
            `Pendiente: ${Currency.format(kpis.totalPendiente)}`,
        ];
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(7);
        doc.setTextColor(...T.textMid);
        doc.text(parts.join('   ·   '), W - 12, y + 7.5, { align: 'right' });
    }

    return y + 13;
}

/* ────────────────────────────────────────────────────────────
   PIE DE PÁGINA — número de página y timestamp
   ──────────────────────────────────────────────────────────── */
function _footer(doc, timestamp) {
    const T = CARTERA_THEME;
    const totalPages = doc.internal.getNumberOfPages();
    const W = doc.internal.pageSize.getWidth();

    for (let i = 1; i <= totalPages; i++) {
        doc.setPage(i);
        const H = doc.internal.pageSize.getHeight();

        doc.setDrawColor(...T.borderLight);
        doc.setLineWidth(0.4);
        doc.line(10, H - 10, W - 10, H - 10);

        doc.setFont('helvetica', 'normal');
        doc.setFontSize(6.5);
        doc.setTextColor(...T.textLight);
        doc.text(`Reporte de Cartera  —  ${timestamp}`, 12, H - 5);
        doc.text(`Página ${i} de ${totalPages}`, W - 12, H - 5, { align: 'right' });
    }
}

/* ────────────────────────────────────────────────────────────
   CONSTRUCTOR MODO "FACTURAS" — todo en una sola tabla
   ──────────────────────────────────────────────────────────── */
function _buildModoFacturas(doc, rows, colKeys, datosEmpresa, filtrosTexto, logoBase64) {
    const timestamp = new Date().toLocaleString('es-MX');
    const kpis = _computeKpis(rows);

    _encabezado(doc, logoBase64, datosEmpresa, new Date().toLocaleDateString('es-MX'));
    _tituloBanda(doc, 'Reporte de Cartera de Clientes', filtrosTexto);

    let y = _kpiRow(doc, kpis, 64);
    const finalY = _tabla(doc, rows, colKeys, y + 2);
    _totalesRow(doc, rows, colKeys, finalY);
    _footer(doc, timestamp);
}

/* ────────────────────────────────────────────────────────────
   CONSTRUCTOR MODO "CLIENTES" — una sección por cliente
   ──────────────────────────────────────────────────────────── */
function _buildModoClientes(doc, rows, colKeys, datosEmpresa, filtrosTexto, logoBase64) {
    const timestamp = new Date().toLocaleString('es-MX');
    const kpisGlobal = _computeKpis(rows);
    const H = doc.internal.pageSize.getHeight();

    // Agrupa por id_cliente conservando el orden de aparición
    const grupos = {};
    const orden = [];
    rows.forEach(r => {
        const id = r.id_cliente || r.cve_cli || '?';
        if (!grupos[id]) { grupos[id] = []; orden.push(id); }
        grupos[id].push(r);
    });

    // ── Página de resumen global ──
    _encabezado(doc, logoBase64, datosEmpresa, new Date().toLocaleDateString('es-MX'));
    _tituloBanda(doc, 'Reporte de Cartera — Resumen por Cliente', filtrosTexto);
    _kpiRow(doc, kpisGlobal, 64);

    // Mini tabla resumen
    const resumenBody = orden.map(id => {
        const g = grupos[id];
        const primer = g[0];
        const monto = g.reduce((s, r) => s + _toDecimal(r, 'monto_total'), 0);
        const cobrado = g.reduce((s, r) => s + _toDecimal(r, 'cobrado'), 0);
        const pend = g.reduce((s, r) => s + _toDecimal(r, 'saldo_pendiente'), 0);
        const pct = monto > 0 ? (Math.round(cobrado / monto * 1000) / 10).toFixed(1) + '%' : '100%';
        return [
            primer.n_cli || '—',
            primer.cve_cli || '—',
            primer.rfc || '—',
            String(g.length),
            Currency.format(monto),
            Currency.format(cobrado),
            Currency.format(pend),
            pct,
        ];
    });

    const T = CARTERA_THEME;
    doc.autoTable({
        head: [['Cliente', 'Código', 'RFC', 'Facturas', 'Monto Total', 'Cobrado', 'Pendiente', '% Cobro']],
        body: resumenBody,
        startY: 84,
        margin: { left: 10, right: 10 },
        styles: {
            fontSize: 7.5, cellPadding: { top: 2, bottom: 2, left: 3, right: 3 },
            lineColor: T.borderLight, lineWidth: 0.15,
            textColor: T.textDark, font: 'helvetica',
        },
        headStyles: {
            fillColor: T.primary, textColor: T.white, fontStyle: 'bold',
            halign: 'center', fontSize: 7,
        },
        alternateRowStyles: { fillColor: T.rowAlt },
        columnStyles: {
            0: { cellWidth: 55 }, 1: { cellWidth: 18 }, 2: { cellWidth: 22 }, 3: { cellWidth: 14, halign: 'center' },
            4: { cellWidth: 28, halign: 'right' }, 5: { cellWidth: 28, halign: 'right' },
            6: { cellWidth: 28, halign: 'right' }, 7: { cellWidth: 18, halign: 'right' },
        },
    });

    // ── Una sección por cliente ──
    orden.forEach((id, idx) => {
        const g = grupos[id];
        const primer = g[0];
        const kpis = {
            totalFacturas: g.length,
            totalMonto: g.reduce((s, r) => s + _toDecimal(r, 'monto_total'), 0),
            totalCobrado: g.reduce((s, r) => s + _toDecimal(r, 'cobrado'), 0),
            totalPendiente: g.reduce((s, r) => s + _toDecimal(r, 'saldo_pendiente'), 0),
        };

        doc.addPage();
        _encabezado(doc, logoBase64, datosEmpresa, new Date().toLocaleDateString('es-MX'));

        let y = _seccionHeader(doc, primer.n_cli || '—', kpis, 48);
        const finalY = _tabla(doc, g, colKeys, y);
        _totalesRow(doc, g, colKeys, finalY);
    });

    _footer(doc, timestamp);
}

/* ────────────────────────────────────────────────────────────
   PUNTO DE ENTRADA PÚBLICO
   ──────────────────────────────────────────────────────────── */

/**
 * Genera y abre el PDF del reporte de cartera.
 *
 * @param {string}   modo          'facturas' | 'clientes'
 * @param {object}   filtros       Valores del panel de filtros (para texto informativo)
 * @param {string[]} columnas      Array de keys visibles, ej. ['cliente','folio','monto_total']
 * @param {object[]} [rows]        Filas ya cargadas. Si se omite, las busca via fetch.
 * @param {object}   [datosEmpresa] Datos de empresa { razon_social, RFC, direccion, telefono }
 * @param {string}   [logoUrl]     URL del logo (relativa o absoluta)
 */
async function exportarCarteraPDF(modo, filtros, columnas, rows, datosEmpresa, logoUrl) {
    if (typeof window.jspdf === 'undefined') {
        console.error('jsPDF no está cargado. Incluye jsPDF + jsPDF-AutoTable antes de este script.');
        return;
    }

    const spinner = document.getElementById('exportSpinner');
    const spinMsg = document.getElementById('spinnerMsg');
    const spinSub = document.getElementById('spinnerSub');

    if (spinner) {
        spinMsg && (spinMsg.textContent = `Generando PDF (${modo === 'clientes' ? 'por cliente' : 'todas las facturas'})…`);
        spinSub && (spinSub.textContent = 'Construyendo el documento…');
        spinner.classList.add('show');
    }

    try {
        // ── 1. Obtener datos si no se pasaron ──
        if (!rows || !rows.length) {
            const params = new URLSearchParams({
                ...filtros,
                formato: 'excel', // solo para traer los datos; no se descarga
                modo,
                columnas: columnas,
            });

            // Reutilizamos el endpoint de datos sin paginación
            rows = await GetData({
                path: `/Carteras/ExportarPdf?${params.toString()}`,
                method: 'GET'
            });
        }

        // ── 2. Logo en base64 ──
        let logoBase64 = null;
        const logoSrc = logoUrl || (typeof empresa !== 'undefined' ? `/Content/img/${empresa}/logo.png` : null);
        if (logoSrc && typeof convertToBase64 === 'function') {
            try { logoBase64 = await convertToBase64(logoSrc); } catch (_) { }
        }

        // ── 3. Texto de filtros activos ──
        const partesFiltros = [];
        if (filtros.desde) partesFiltros.push(`Desde: ${filtros.desde}`);
        if (filtros.hasta) partesFiltros.push(`Hasta: ${filtros.hasta}`);
        if (filtros.estado) partesFiltros.push(`Estado: ${filtros.estado}`);
        if (filtros.vendedor) partesFiltros.push(`Vendedor: ${filtros.vendedor}`);
        if (filtros.zona) partesFiltros.push(`Zona: ${filtros.zona}`);
        if (filtros.tipo_doc) partesFiltros.push(`Tipo doc: ${filtros.tipo_doc}`);
        const filtrosTexto = partesFiltros.length ? partesFiltros.join('  |  ') : 'Sin filtros aplicados';

        // ── 4. Construir documento ──
        const { jsPDF } = window.jspdf;
        const doc = new jsPDF({
            orientation: 'landscape',
            unit: 'mm',
            format: 'letter',
        });

        const colKeys = columnas.length ? columnas : Object.keys(CARTERA_COL_CATALOG).slice(0, 11);

        if (modo === 'clientes') {
            _buildModoClientes(doc, rows, colKeys, datosEmpresa, filtrosTexto, logoBase64);
        } else {
            _buildModoFacturas(doc, rows, colKeys, datosEmpresa, filtrosTexto, logoBase64);
        }

        // ── 5. Abrir en nueva pestaña ──
        const blob = doc.output('blob');
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');

    } catch (err) {
        console.error('Error generando PDF de cartera:', err);
        if (typeof _Swal !== 'undefined') {
            _Swal.fire({ icon: 'error', title: 'Error al generar PDF', text: err.message });
        } else {
            alert('Error al generar PDF: ' + err.message);
        }
    } finally {
        spinner && spinner.classList.remove('show');
    }
}