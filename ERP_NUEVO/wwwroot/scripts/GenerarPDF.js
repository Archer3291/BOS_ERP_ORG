// ============================================================
// DESIGN TOKENS — cambia aquí para afectar todo el sistema
// ============================================================
const PDF_THEME = {
    primary: [15, 52, 96],   // Azul corporativo oscuro
    accent: [0, 120, 215],   // Azul medio para acentos
    textDark: [30, 30, 30],
    textMid: [90, 90, 90],
    textLight: [160, 160, 160],
    white: [255, 255, 255],
    rowAlt: [248, 250, 253],  // Filas alternadas tabla
    bgLight: [245, 247, 250],  // Bandas de info
    borderLight: [220, 225, 232],
    success: [39, 174, 96],
    danger: [192, 57, 43],
};

// ============================================================
// HELPERS ESTANDARIZADOS
// ============================================================

/**
 * Encabezado corporativo minimalista.
 * Franja izquierda + logo + título + línea de acento inferior.
 */
function agregarEncabezadoPDF(doc, logoBase64, titulo, datos, fecha) {
    const pdfWidth = doc.internal.pageSize.getWidth();
    const marginH = 12;
    const bannerY = 10;
    const bannerH = 32;

    // Franja lateral
    doc.setFillColor(...PDF_THEME.primary);
    doc.rect(marginH, bannerY, 4, bannerH, 'F');

    // Logo centrado
    const logoW = 26;
    const logoH = 18;
    const logoY = bannerY + (bannerH - logoH) / 2;

    if (logoBase64) {
        doc.addImage(logoBase64, 'PNG', marginH + 8, logoY, logoW, logoH);
    }

    const contentX = marginH + 40;

    // Razón social
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(11.5);
    doc.setTextColor(...PDF_THEME.primary);
    doc.text(datos.razon_social, contentX, bannerY + 9);

    // Datos empresa
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(7.5);
    doc.setTextColor(...PDF_THEME.textMid);

    let y = bannerY + 15;

    // RFC
    doc.text(`RFC: ${datos.RFC}`, contentX, y);
    y += 4;

    // Dirección con saltos automáticos
    // Inserta salto de línea antes del código postal (CP, C.P., Código Postal)
    const direccionNormalizada = datos.direccion.replace(
        /,?\s*(C\.?P\.?|C\.?\s*P\.?|[Cc][oó]digo\s+[Pp]ostal)\s*/g,
        '\nC.P. '
    );
    const direccionLines = doc.splitTextToSize(
        direccionNormalizada,
        pdfWidth - contentX - marginH - 50 // dejar espacio para la fecha a la derecha
    );
    doc.text(direccionLines, contentX, y);
    y += direccionLines.length * 4;

    // Teléfono
    doc.text(`Tel: ${datos.telefono}`, contentX, y);

    // FECHA en la esquina superior derecha del banner
    if (fecha) {
        const rightX = pdfWidth - marginH;
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(7);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text('FECHA', rightX, bannerY + 9, { align: 'right' });
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(8.5);
        doc.setTextColor(...PDF_THEME.textDark);
        doc.text(fecha, rightX, bannerY + 15, { align: 'right' });
    }

    // Línea inferior
    doc.setDrawColor(...PDF_THEME.borderLight);
    doc.setLineWidth(0.6);
    doc.line(
        marginH,
        bannerY + bannerH + 4,
        pdfWidth - marginH,
        bannerY + bannerH + 4
    );
}

/**
 * Banda de folio + fecha. extras: [{ label, valor }]
 */
function agregarFolioFecha(doc, folio, titulo, extras = []) {
    const pdfWidth = doc.internal.pageSize.getWidth();
    const marginH = 10;
    const y = 42;

    doc.setFillColor(...PDF_THEME.bgLight);
    doc.rect(marginH, y, pdfWidth - marginH * 2, 10, 'F');

    // Folio
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(7);
    doc.setTextColor(...PDF_THEME.textMid);
    doc.text('FOLIO', marginH + 3, y + 3.5);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(8.5);
    doc.setTextColor(...PDF_THEME.textDark);
    doc.text(folio, marginH + 3, y + 8.5);

    // Extras centrados
    let offsetX = pdfWidth / 2 - 20;
    extras.forEach(({ label, valor }) => {
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(7);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(label.toUpperCase(), offsetX, y + 3.5);
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(8.5);
        doc.setTextColor(...PDF_THEME.textDark);
        doc.text(String(valor), offsetX, y + 8.5);
        offsetX += 40;
    });

    // TÍTULO DEL DOCUMENTO alineado a la derecha
    if (titulo) {
        const rightX = pdfWidth - marginH - 3;
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(8.5);
        doc.setTextColor(...PDF_THEME.primary);
        doc.text(titulo.toUpperCase(), rightX, y + 7, { align: 'right' });
    }
}

/**
 * Tabla estándar minimalista con filas alternadas.
 */
function agregarTabla(doc, columns, rows, startY, extraOptions = {}) {
    doc.autoTable({
        head: [columns.map(col => col.header)],
        body: rows.map(row => columns.map(col => row[col.dataKey])),
        startY,
        margin: { left: 10, right: 10 },
        tableWidth: 190,
        styles: {
            fontSize: 8.5,
            //cellPadding: { top: 3.5, bottom: 3.5, left: 4, right: 4 },
            lineColor: PDF_THEME.borderLight,
            lineWidth: 0.15,
            textColor: PDF_THEME.textDark,
            font: 'helvetica',
        },
        headStyles: {
            fillColor: PDF_THEME.primary,
            textColor: PDF_THEME.white,
            fontStyle: 'bold',
            halign: 'center',
            fontSize: 8,
            cellPadding: { top: 4, bottom: 4, left: 4, right: 4 },
        },
        bodyStyles: { halign: 'left' },
        alternateRowStyles: { fillColor: PDF_THEME.rowAlt },
        ...extraOptions,
    });
}

/**
 * Bloque de comentarios con borde izquierdo de acento.
 * Retorna { alturaComentario }.
 */
function agregarComentarios(doc, comentario, y) {
    const marginH = 10;
    const boxW = 130;

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(7);
    doc.setTextColor(...PDF_THEME.textMid);
    doc.text('COMENTARIOS', marginH, y);

    const texto = doc.splitTextToSize(comentario || '—', boxW - 8);
    const altura = Math.max(texto.length * 5.5 + 6, 14);

    // Caja exterior suave
    doc.setDrawColor(...PDF_THEME.borderLight);
    doc.setLineWidth(0.3);
    doc.rect(marginH, y + 3, boxW, altura, 'S');

    // Borde izquierdo de acento
    doc.setDrawColor(...PDF_THEME.accent);
    doc.setLineWidth(1.2);
    doc.line(marginH, y + 3, marginH, y + 3 + altura);

    doc.setFont('helvetica', 'normal');
    doc.setFontSize(8.5);
    doc.setTextColor(...PDF_THEME.textDark);
    doc.text(texto, marginH + 5, y + 9);

    return { alturaComentario: altura };
}

/**
 * Totales a la derecha. Sin impuestos → solo subtotal.
 * Con impuestos → subtotal + desglose + total.
 * Retorna el Y final.
 */
function agregarTotales(doc, y, subtotal, impuestos = []) {
    const pdfWidth = doc.internal.pageSize.getWidth();
    const marginH = 10;
    const colX = 148;
    const valX = pdfWidth - marginH;

    const drawRow = (label, valor, cy, { bold = false, color = PDF_THEME.textDark, size = 8.5 } = {}) => {
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(size);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(label, colX, cy);
        doc.setFont('helvetica', bold ? 'bold' : 'normal');
        doc.setTextColor(...color);
        doc.text(Currency.format(valor), valX, cy, { align: 'right' });
    };

    let currentY = y;
    drawRow('Subtotal', subtotal, currentY);

    if (impuestos.length > 0) {
        let totalImpuestos = 0;
        impuestos.forEach((imp) => {
            currentY += 6;
            drawRow(`${imp.nombre} (${imp.tasa}%)`, imp.importe, currentY);
            totalImpuestos += imp.esRetencion ? -imp.importe : imp.importe;
        });

        // Línea separadora
        currentY += 4;
        doc.setDrawColor(...PDF_THEME.borderLight);
        doc.setLineWidth(0.4);
        doc.line(colX, currentY, valX, currentY);

        currentY += 6;
        drawRow('TOTAL', subtotal + totalImpuestos, currentY, {
            bold: true, color: PDF_THEME.primary, size: 10,
        });
    }

    return currentY;
}

/**
 * Bloque de firmas con separador superior y etiqueta.
 */
function agregarFirma(doc, imgBase64, rol, nombre, colIndex, yFirmas, colWidth, margin) {
    return new Promise((resolve) => {
        const image = new Image();
        image.onload = function () {
            const firmaWidthMM = 60;   // más pequeño pero rectangular
            const firmaHeightMM = 30;  // proporción 2:1

            const baseX = margin + colIndex * colWidth;
            const centerX = baseX + colWidth / 2;

            // Rol (arriba de la firma)
            doc.setFontSize(9);
            const rolWidth = doc.getTextWidth(rol);
            doc.text(rol, centerX - rolWidth / 2, yFirmas + 7);

            // Firma centrada
            const imgY = yFirmas + 9;
            doc.addImage(
                imgBase64,
                'PNG',
                centerX - firmaWidthMM / 2, // centrado horizontal
                imgY,
                firmaWidthMM,
                firmaHeightMM
            );

            // Nombre (abajo de la firma)
            const nombreY = imgY + firmaHeightMM + 5;
            const nombreWidth = doc.getTextWidth(nombre);
            doc.text(nombre, centerX - nombreWidth / 2, nombreY);

            resolve();
        };

        image.src = imgBase64;
    });
}

async function agregarBloqueFiremas(doc, y, firmasConfig, pdfWidth) {
    const margin = 10;
    const colWidth = (pdfWidth - margin * 2) / 2;

    doc.setDrawColor(...PDF_THEME.borderLight);
    doc.setLineWidth(0.3);
    doc.line(margin, y - 4, pdfWidth - margin, y - 4);

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(7);
    doc.setTextColor(...PDF_THEME.textMid);
    doc.text('FIRMAS Y AUTORIZACIONES', margin, y);

    const firmas = firmasConfig.filter(f => f.firmaBase64);

    // Fila superior (máx 2)
    const promesas = firmas.slice(0, 2).map((f, i) =>
        agregarFirma(
            doc,
            f.firmaBase64,
            f.rol,
            f.nombre || '—',
            i,
            y + 4,
            colWidth,
            margin
        )
    );

    await Promise.all(promesas);

    // Tercera firma centrada abajo
    if (firmas[2]) {
        const firma = firmas[2];

        const marginCentrado = (pdfWidth - colWidth) / 2;
        const yAbajo = y + 4 + 45; // ajusta si quieres más aire

        await agregarFirma(
            doc,
            firma.firmaBase64,
            firma.rol,
            firma.nombre || '—',
            0,                // columna 0
            yAbajo,
            colWidth,
            marginCentrado    // margin especial
        );
    }
}

// ============================================================
// MAPAS DE TÍTULOS Y NOMBRES DE ARCHIVO
// ============================================================
const TITULOS_NAT = {
    SCINT: 'SOLICITUD DE COTIZACIÓN INTERNACIONAL',
    TRAINV: 'TRANSACCIÓN DE INVENTARIO',
    SOLINV: 'SOLICITUD DE INVENTARIO ENTRE SUCURSALES',
    ENVINV: 'DOCUMENTO DE ENVÍO DE MATERIAL',
    DISINV: 'DOCUMENTO DE DISCREPANCIA EN ENVÍO DE MATERIAL',
    DONINV: 'DOCUMENTO DE DONACIÓN',
    RINV: 'DOCUMENTO DE RECEPCIÓN DE INVENTARIO',
    CTZ: 'COTIZACIÓN',
    RM: 'REQUISICIÓN DE MATERIAL',
    OC: 'ORDEN DE COMPRA',
    GTO: 'SOLICITUD DE GASTO',
    OCD: 'ORDEN DE COMPRA DIRECTA',
    OCDI: 'ORDEN DE COMPRA DIRECTA INTERNACIONAL',
    VIREM: 'REMISION DE INDUSTRIAL',
    VSREM: 'REMISION DE SUCURSAL',
    VNREM: 'REMISION DE NACIONAL',
    VINREM: 'REMISION DE INTERNACIONAL',
    VICOT: 'COTIZACION INDUSTRIAL',
    VSCOT: 'COTIZACION SUCURSAL',
    VNCOT: 'COTIZACION NACIONAL',
    VINCOT: 'COTIZACION INTERNACIONAL',
    VIPED: 'PEDIDO INTERNACIONAL',
    VSPED: 'PEDIDO SUCURSAL',
    VNPED: 'PEDIDO NACIONAL',
    VINPED: 'PEDIDO INTERNACIONAL',
    _DEFAULT: 'SOLICITUD DE COTIZACIÓN',
};

const NOMBRES_ARCHIVO = {
    CTZ: (f) => `Cotizacion-${f}.pdf`,
    RM: (f) => `Requisicion-${f}.pdf`,
    OC: (f) => `OrdenCompra-${f}.pdf`,
    GTO: (f) => `SolicitudGasto-${f}.pdf`,
    OCD: (f) => `OrdenCompraDirecta-${f}.pdf`,
    OCDI: (f) => `OrdenCompraDirecta-${f}.pdf`,
    VIREM: (f) => `remision-industrial-${f}`,
    VSREM: (f) => `remision-sucursal-${f}`,
    VNREM: (f) => `remision-nacional-${f}`,
    VINREM: (f) => `remision-internacional-${f}`,
    VICOT: (f) => `cotizacion-industrial-${f}`,
    VSCOT: (f) => `cotizacion-sucursal-${f}`,
    VNCOT: (f) => `cotizacion-nacional-${f}`,
    VINCOT: (f) => `cotizacion-internacional-${f}`,
    VIPED: (f) => `pedido-internacional-${f}`,
    VSPED: (f) => `pedido-sucursal-${f}`,
    VNPED: (f) => `pedido-nacional-${f}`,
    VINPED: (f) => `pedido-internacional-${f}`,
    _DEFAULT: (f) => `Solicitud-${f}.pdf`,
};

// ============================================================
// ROUTER PRINCIPAL
// ============================================================
async function GenerarPDFGeneral(id_encabezado) {
    const _res = await GetData({ path: '/Requisicion/gerPdfData', data: { id: id_encabezado } });

    switch (_res.requisicion.nat) {
        case 'SCTZ': case 'SCTZI': case 'SCTZT':
        case 'SCINT': case 'SSR': case 'SOLINV':
        case 'TRAINV': case 'ENVINV': case 'DISINV':
        case 'DONINV': case 'RINV': case 'VICOT': case 'VSCOT':
        case 'VNCOT': case 'VINCOT':
            await generarPDFSolicitud(_res); break;
        case 'CTZ':
            await generarPDFOpciones(_res); break;
        case 'RM':
            await generarPDFRequisicion(_res); break;
        case 'OC': case 'OCDI': case 'GTO': case 'OCD': case 'VIPED': case 'VIREM':
        case 'VSPED': case 'VSREM': case 'VNPED': case 'VNREM': case 'VINPED': case 'VINREM': case 'RG':
            await generarPDFOrdenCompra(_res); break;
        case 'VIFAC': case 'VSFAC': case 'VNFAC': case 'VINFAC':
            window.open(`/Facturacion/facturas/${_res.requisicion.factura}.pdf`, '_blank');
            break;
    }
}

// ============================================================
// GENERADORES ESPECÍFICOS
// ============================================================

async function generarPDFSolicitud(_res) {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF();
    const { partidas, requisicion: encabezado } = _res;
    const pdfWidth = doc.internal.pageSize.getWidth();

    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);
    agregarEncabezadoPDF(doc, logoBase64, TITULOS_NAT[encabezado.nat] ?? TITULOS_NAT._DEFAULT, _res.datos_empresa, dateFormatter(encabezado.fch));

    const extras = encabezado.nat === 'SCINT' ? [{ label: 'Moneda', valor: encabezado.ccy }] : [];
    agregarFolioFecha(doc, encabezado.folio, TITULOS_NAT[encabezado.nat] ?? TITULOS_NAT._DEFAULT);

    const ocultarCostos = ['TRINV', 'SOLINV', 'ENVINV'].includes(encabezado.nat);
    const columns = [
        { header: '#', dataKey: 'num' },
        { header: 'Cantidad', dataKey: 'cantidad' },
        { header: 'Código', dataKey: 'codigo' },
        { header: 'Descripción', dataKey: 'descripcion' },
        { header: 'Unidad', dataKey: 'unidad' },
        ...(!ocultarCostos ? [
            { header: 'Costo Unit.', dataKey: 'costounitario' },
            { header: 'Subtotal', dataKey: 'total' },
        ] : []),
    ];

    const rows = partidas.map((item, i) => {
        const base = {
            num: i + 1,
            cantidad: item.cantidad,
            codigo: item.codigo,
            descripcion: `${item.descripcion ?? ''}${item.pedimentos == null || item.pedimentos.trim() == '' ? '' : '\n' + item.pedimentos}${item.tp_doc_ant == null || item.tp_doc_ant.trim() == '' ? '' : '\n' + item.tp_doc_ant}`,
            unidad: item.unidad
        };
        if (!ocultarCostos) { base.costounitario = Currency.format(item.costounitario); base.total = Currency.format(item.total); }
        return base;
    });

    agregarTabla(doc, columns, rows, 57);

    const finalY = doc.lastAutoTable.finalY + 10;
    const { alturaComentario } = agregarComentarios(doc, encabezado.observaciones, finalY);

    if (!ocultarCostos) {
        const totalGlobal = partidas.reduce((acc, item) => acc + parseFloat(item.total), 0);
        agregarTotales(doc, finalY, totalGlobal);
    }

    const yFirmas = finalY + alturaComentario + 14;
    const firmasConfig = encabezado.nat === 'SCINT'
        ? [{ firmaBase64: encabezado.firmagerente, rol: 'Director General', nombre: encabezado.gerente, index: 0 }]
        : [
            { firmaBase64: encabezado.firmagerente, rol: 'Gerente de Área', nombre: encabezado.gerente, index: 0 },
            { firmaBase64: encabezado.firmadireccion, rol: 'Director General', nombre: encabezado.direccion, index: 1 },
            { firmaBase64: encabezado.firmapresupuesto, rol: 'Aprobador de presupuesto', nombre: encabezado.presupuestorevision, index: 2 }
        ];

    await agregarBloqueFiremas(doc, yFirmas, firmasConfig, pdfWidth);
    /*doc.save(NOMBRES_ARCHIVO[encabezado.nat]?.(encabezado.folio) ?? NOMBRES_ARCHIVO._DEFAULT(encabezado.folio));*/

    const pdfBlob = doc.output("blob");
    const url = URL.createObjectURL(pdfBlob);

    window.open(url, "_blank");
}

async function generarPDFOpciones(_res) {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF();
    const { opciones, requisicion: encabezado } = _res;
    const partidas = encabezado.variacion > 0 ? _res.partidas_opciones_variacion : _res.partidasPadre;
    const pdfHeight = doc.internal.pageSize.getHeight();

    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);
    agregarEncabezadoPDF(doc, logoBase64, TITULOS_NAT.CTZ, _res.datos_empresa, dateFormatter(encabezado.fch));
    agregarFolioFecha(doc, encabezado.folio, TITULOS_NAT[encabezado.nat] ?? TITULOS_NAT._DEFAULT);

    let currentY = 57;
    const columns = [
        { header: '#', dataKey: 'num' },
        { header: 'Cantidad', dataKey: 'cant_ud' },
        { header: 'Unidad', dataKey: 'unidad' },
        { header: 'Proveedor', dataKey: 'proveedor_nombre' },
        { header: 'C/U.', dataKey: 'precio' },
        { header: 'Subtotal', dataKey: 'total' },
    ];

    partidas.forEach((p) => {
        const opcionesFiltradas = opciones.filter(o => o.partidas_id === p.id_partidas);
        if (!opcionesFiltradas.length) return;

        if (currentY + 30 > pdfHeight) { doc.addPage(); currentY = 20; }

        // Etiqueta de partida con borde lateral
        doc.setFillColor(...PDF_THEME.bgLight);
        doc.rect(10, currentY - 1, 190, 9, 'F');
        doc.setDrawColor(...PDF_THEME.accent);
        doc.setLineWidth(1.2);
        doc.line(10, currentY - 1, 10, currentY + 8);

        doc.setFont('helvetica', 'bold');
        doc.setFontSize(8.5);
        doc.setTextColor(...PDF_THEME.primary);
        const titulo = doc.splitTextToSize(`${p.cve_prod}  ·  ${p.descr_prod}`, 182);
        doc.text(titulo, 15, currentY + 5);
        currentY += titulo.length * 4.5 + 6;

        const rows = opcionesFiltradas.map((o, i) => ({
            num: i + 1, cant_ud: o.cant_ud, unidad: o.unidad,
            proveedor_nombre: o.proveedor_nombre,
            precio: Currency.format(o.precio), total: Currency.format(o.total),
        }));

        agregarTabla(doc, columns, rows, currentY);
        currentY = doc.lastAutoTable.finalY + 10;
    });

    const finalY = doc.lastAutoTable?.finalY ? doc.lastAutoTable.finalY + 10 : currentY + 10;
    agregarComentarios(doc, encabezado.observaciones, finalY);

    //doc.save(NOMBRES_ARCHIVO.CTZ(encabezado.folio));

    const pdfBlob = doc.output("blob");
    const url = URL.createObjectURL(pdfBlob);

    window.open(url, "_blank");
}

async function generarPDFRequisicion(_res) {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF();
    const { partidas, requisicion: encabezado } = _res;
    const pdfWidth = doc.internal.pageSize.getWidth();

    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);
    agregarEncabezadoPDF(doc, logoBase64, TITULOS_NAT.RM, _res.datos_empresa, dateFormatter(encabezado.fch));
    agregarFolioFecha(doc, encabezado.folio, TITULOS_NAT.RM);

    const tieneDescuento = partidas.some(p => p.descuento && p.descuento > 0);
    const columns = [
        { header: '#', dataKey: 'num' },
        { header: 'Cant.', dataKey: 'cantidad' },
        { header: 'Código', dataKey: 'codigo' },
        { header: 'Descripción', dataKey: 'descripcion' },
        { header: 'Ud.', dataKey: 'unidad' },
        { header: 'Costo Unit.', dataKey: 'costounitario' },
        ...(tieneDescuento ? [{ header: 'Desc.', dataKey: 'descuento' }] : []),
        { header: 'Subtotal', dataKey: 'total' },
    ];

    const rows = partidas.map((item, i) => {
        const base = {
            num: i + 1, cantidad: item.cantidad, codigo: item.codigo,
            descripcion: item.descripcion, unidad: item.unidad,
            costounitario: Currency.format(item.costounitario),
        };
        if (tieneDescuento) base.descuento = item.descuento + '%' || 0 + '%';
        base.total = Currency.format(item.totaldescuento);
        return base;
    });

    agregarTabla(doc, columns, rows, 57);

    const finalY = doc.lastAutoTable.finalY + 10;
    const { alturaComentario } = agregarComentarios(doc, encabezado.observaciones, finalY);

    const totalGlobal = partidas.reduce((acc, item) => acc + parseFloat(item.totaldescuento), 0);
    agregarTotales(doc, finalY, totalGlobal);

    const yFirmas = finalY + alturaComentario + 14;
    await agregarBloqueFiremas(doc, yFirmas, [
        { firmaBase64: encabezado.firmagerente, rol: 'Gerente de Área', nombre: encabezado.gerenterevision, index: 0 },
        { firmaBase64: encabezado.firmadireccion, rol: 'Director General', nombre: encabezado.direccion, index: 1 },
        { firmaBase64: encabezado.firmapresupuesto, rol: 'Aprobador de presupuesto', nombre: encabezado.presupuestorevision, index: 2 }
    ], pdfWidth);

    //doc.save(NOMBRES_ARCHIVO.RM(encabezado.folio));

    const pdfBlob = doc.output("blob");
    const url = URL.createObjectURL(pdfBlob);

    window.open(url, "_blank");
}

async function generarPDFOrdenCompra(_res) {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF();
    const { partidas, requisicion: encabezado } = _res;
    const pdfWidth = doc.internal.pageSize.getWidth();

    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);
    agregarEncabezadoPDF(doc, logoBase64, TITULOS_NAT[encabezado.nat], _res.datos_empresa, dateFormatter(encabezado.fch));
    agregarFolioFecha(doc, encabezado.folio, TITULOS_NAT[encabezado.nat]);

    // Banda de proveedor
    let currentY = 57;
    doc.setFillColor(...PDF_THEME.bgLight);
    doc.rect(10, currentY, 190, 10, 'F');
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(7);
    doc.setTextColor(...PDF_THEME.textMid);
    if (_res.requisicion.nat === 'VIREM') {
        doc.text('CLIENTE', 14, currentY + 4);
    }
    else {
        doc.text('PROVEEDOR', 14, currentY + 4);
    }
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(9);
    doc.setTextColor(...PDF_THEME.textDark);
    if (_res.requisicion.nat === 'VIREM') {
        doc.text(`${encabezado.cli_prov ?? '—'} ${encabezado.n_cli ?? ''}`, 14, currentY + 8.5);
    }
    else {
        doc.text(encabezado.cli_prov ?? '—', 14, currentY + 8.5);
    }
    currentY += 14;

    const tieneDescuento = partidas.some(p => p.descuento && p.descuento > 0);
    const columns = [
        { header: '#', dataKey: 'num' },
        { header: 'Cant.', dataKey: 'cantidad' },
        { header: 'Código', dataKey: 'codigo' },
        { header: 'Descripción', dataKey: 'descripcion' },
        { header: 'Ud.', dataKey: 'unidad' },
        { header: 'C/U', dataKey: 'costounitario' },
        ...(tieneDescuento ? [{ header: 'Desc.', dataKey: 'descuento' }] : []),
        { header: 'Subtotal', dataKey: 'total' },
    ];

    const rows = partidas.map((item, i) => {
        const base = {
            num: i + 1, cantidad: item.cantidad, codigo: item.codigo,
            descripcion: `${item.descripcion ?? ''}${item.pedimentos == null || item.pedimentos.trim() == '' ? '' : '\n' + item.pedimentos}${item.tp_doc_ant == null || item.tp_doc_ant.trim() == '' ? '' : '\n' + item.tp_doc_ant}`,
            unidad: item.unidad,
            costounitario: Currency.format(item.costounitario),
        };
        if (tieneDescuento) base.descuento = item.descuento + '%' || 0 + '%';
        base.total = Currency.format(item.totaldescuento);
        return base;
    });

    agregarTabla(doc, columns, rows, currentY);

    const finalY = doc.lastAutoTable.finalY + 10;
    const { alturaComentario } = agregarComentarios(doc, encabezado.observaciones, finalY);

    const subtotalGlobal = partidas.reduce((acc, item) => acc + parseFloat(item.totaldescuento), 0);
    const impuestosOrdenados = (_res.impuestos || []).sort((a, b) => a.orden_apl - b.orden_apl);
    const impuestosFormateados = impuestosOrdenados.map(imp => ({
        nombre: imp.cve_impuesto,
        tasa: imp.imp_variable,
        importe: parseFloat(imp.subtotal) * (parseFloat(imp.imp_variable) / 100),
        esRetencion: imp.es_retencion,
    }));

    const currentYImp = agregarTotales(doc, finalY, subtotalGlobal, impuestosFormateados);

    if (_res.codigoQR) {
        const qrY = currentYImp + 12;
        doc.addImage(_res.codigoQR, 'PNG', pdfWidth / 2 - 20, qrY, 40, 40);
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(7.5);
        doc.setTextColor(...PDF_THEME.textLight);
        doc.text('Escanee para validar esta orden', pdfWidth / 2, qrY + 44, { align: 'center' });
    }

    const yFirmas = finalY + alturaComentario + 14;
    await agregarBloqueFiremas(doc, yFirmas, [
        { firmaBase64: encabezado.firmagerente, rol: 'Gerente de Área', nombre: encabezado.gerenterevision, index: 0 },
        { firmaBase64: encabezado.firmadireccion, rol: 'Director General', nombre: encabezado.direccion, index: 1 },
        { firmaBase64: encabezado.firmapresupuesto, rol: 'Aprobador de presupuesto', nombre: encabezado.presupuestorevision, index: 2 }
    ], pdfWidth);
    let currentYPagare = yFirmas + 10; 

    if (_res.requisicion.nat === 'VIREM') {
        currentYPagare += 10;

        currentYPagare = agregarPagare(
            doc,
            currentYPagare,
            _res.datos_empresa?.razon_social || 'EMPRESA'
        );
    }
    //doc.save(NOMBRES_ARCHIVO[encabezado.nat]?.(encabezado.folio) ?? NOMBRES_ARCHIVO._DEFAULT(encabezado.folio));

    const pdfBlob = doc.output("blob");
    const url = URL.createObjectURL(pdfBlob);

    window.open(url, "_blank");
}

// ============================================================
// PÓLIZA INDIVIDUAL
// ============================================================
async function generarPDFPoliza(uuid) {
    const data = await GetData({ path: '/Contabilidad/GetDetallesPoliza', data: { uuid } });

    const { jsPDF } = window.jspdf;
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'letter' });

    const marginLeft = 10;
    const marginRight = 10;
    const marginTop = 42;
    const contentWidth = doc.internal.pageSize.getWidth() - marginLeft - marginRight;
    const pdfWidth = doc.internal.pageSize.getWidth();
    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);

    const TITULOS_POLIZA = {
        factura_contado: 'PÓLIZA CONTABLE — CONTADO',
        factura_credito: 'PÓLIZA CONTABLE — CRÉDITO',
        _DEFAULT: 'PÓLIZA CONTABLE',
    };

    const addPageHeader = () => {
        const currentPage = doc.internal.getCurrentPageInfo().pageNumber;

        doc.setFillColor(...PDF_THEME.primary);
        doc.rect(marginLeft, 8, 4, 26, 'F');

        if (logoBase64) doc.addImage(logoBase64, 'PNG', marginLeft + 8, 12, 22, 14);

        doc.setDrawColor(...PDF_THEME.borderLight);
        doc.setLineWidth(0.4);
        doc.line(marginLeft + 34, 12, marginLeft + 34, 32);

        const tituloPoliza = TITULOS_POLIZA[data.referencia?.tipo_proceso] ?? TITULOS_POLIZA._DEFAULT;
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(12);
        doc.setTextColor(...PDF_THEME.primary);
        doc.text(tituloPoliza, marginLeft + 40, 20);

        doc.setFont('helvetica', 'normal');
        doc.setFontSize(7.5);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(`Generado: ${new Date().toLocaleString('es-MX')}`, marginLeft + 40, 26);

        doc.setDrawColor(...PDF_THEME.accent);
        doc.setLineWidth(0.8);
        doc.line(marginLeft, 35, pdfWidth - marginRight, 35);

        doc.setFont('helvetica', 'bold');
        doc.setFontSize(8);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(`Página ${currentPage}`, pdfWidth - marginRight, 20, { align: 'right' });
    };

    addPageHeader();
    let currentY = marginTop + 2;

    // Banda info póliza
    doc.setFillColor(...PDF_THEME.bgLight);
    doc.rect(marginLeft, currentY, contentWidth, 20, 'F');
    doc.setDrawColor(...PDF_THEME.accent);
    doc.setLineWidth(1.2);
    doc.line(marginLeft, currentY, marginLeft, currentY + 20);

    const labelY = currentY + 4.5;
    const valueY = currentY + 11;

    const infoItems = [
        { label: 'REFERENCIA', valor: data.poliza.folio, x: marginLeft + 4, align: 'left' },
        { label: 'RESPONSABLE', valor: data.poliza.nombre, x: pdfWidth / 2 - 15, align: 'left' },
        { label: 'FECHA', valor: dateFormatter(data.poliza.fecha), x: pdfWidth - marginRight - 4, align: 'right' },
    ];

    infoItems.forEach(({ label, valor, x, align }) => {
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(7);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(label, x, labelY, { align });
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(9);
        doc.setTextColor(...PDF_THEME.textDark);
        doc.text(valor, x, valueY, { align });
    });

    doc.setFont('helvetica', 'normal');
    doc.setFontSize(7);
    doc.setTextColor(...PDF_THEME.textLight);
    doc.text(`UUID: ${data.poliza.uuid}`, marginLeft + 4, currentY + 17);

    // Badges outline
    const badgeY = currentY + 12;
    const badgeItems = [
        { text: data.poliza.tipo.toUpperCase(), color: PDF_THEME.accent, xOffset: 28 },
        { text: data.poliza.estado.toUpperCase(), color: data.poliza.estado.toLowerCase() === 'activo' ? PDF_THEME.success : PDF_THEME.danger, xOffset: 0 },
    ];

    let badgeX = pdfWidth - marginRight;
    [...badgeItems].reverse().forEach(({ text, color }) => {
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(6.5);
        const bW = doc.getTextWidth(text) + 6;
        badgeX -= bW + 2;
        doc.setDrawColor(...color);
        doc.setLineWidth(0.5);
        doc.roundedRect(badgeX, badgeY, bW, 5, 1, 1, 'S');
        doc.setTextColor(...color);
        doc.text(text, badgeX + bW / 2, badgeY + 3.5, { align: 'center' });
    });

    currentY += 24;

    // Tabla
    const columns = [
        { header: '#', dataKey: 'num' },
        { header: 'Cuenta', dataKey: 'cuenta' },
        { header: 'Nombre cuenta', dataKey: 'nombre' },
        { header: 'Centro Costos', dataKey: 'centro' },
        { header: 'Descripción', dataKey: 'descripcion' },
        { header: 'Debe', dataKey: 'debe' },
        { header: 'Haber', dataKey: 'haber' },
    ];

    const rows = data.detalles.map((item, i) => ({
        num: i + 1, cuenta: item.codigo, nombre: item.nombrecuenta,
        centro: item.nombre_costos, descripcion: item.descripcion,
        debe: item.debe === 0 ? '' : Currency.format(item.debe),
        haber: item.haber === 0 ? '' : Currency.format(item.haber),
    }));

    agregarTabla(doc, columns, rows, currentY, {
        margin: { left: marginLeft, right: marginRight, top: marginTop },
        columnStyles: {
            0: { cellWidth: 10, halign: 'center' },
            1: { cellWidth: 22 },
            2: { cellWidth: 'auto' },
            3: { cellWidth: 28 },
            4: { cellWidth: 'auto' },
            5: { cellWidth: 26, halign: 'right' },
            6: { cellWidth: 26, halign: 'right' },
        },
        didDrawPage: (hookData) => { if (hookData.pageNumber > 1) addPageHeader(); },
    });

    const finalY = doc.lastAutoTable.finalY + 6;

    // Totales póliza
    const totalDebe = data.detalles.reduce((s, d) => s + d.debe, 0);
    const totalHaber = data.detalles.reduce((s, d) => s + d.haber, 0);
    const diferencia = totalDebe - totalHaber;

    doc.setFillColor(...PDF_THEME.bgLight);
    doc.rect(marginLeft, finalY - 2, contentWidth, 26, 'F');
    doc.setDrawColor(...PDF_THEME.accent);
    doc.setLineWidth(0.8);
    doc.line(marginLeft, finalY - 2, marginLeft, finalY + 24);

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(8);
    doc.setTextColor(...PDF_THEME.textMid);
    doc.text(`${data.detalles.length} partidas registradas`, marginLeft + 4, finalY + 5);

    const printTotalRow = (label, valor, cy, colorValor = PDF_THEME.textDark) => {
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(8.5);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(label, pdfWidth - marginRight - 48, cy);
        doc.setFont('helvetica', 'bold');
        doc.setTextColor(...colorValor);
        doc.text(Currency.format(valor), pdfWidth - marginRight, cy, { align: 'right' });
    };

    printTotalRow('Total Debe:', totalDebe, finalY + 5);
    printTotalRow('Total Haber:', totalHaber, finalY + 13);

    const diferenciaColor = Math.abs(diferencia) < 0.01 ? PDF_THEME.success : PDF_THEME.danger;
    printTotalRow('Diferencia:', Math.abs(diferencia), finalY + 21, diferenciaColor);

    // Footer
    const footerY = finalY + 32;
    doc.setDrawColor(...PDF_THEME.borderLight);
    doc.setLineWidth(0.3);
    doc.line(marginLeft + 30, footerY, pdfWidth - marginRight - 30, footerY);
    doc.setFontSize(7);
    doc.setFont('helvetica', 'italic');
    doc.setTextColor(...PDF_THEME.textLight);
    doc.text('Documento generado automáticamente por el sistema contable', pdfWidth / 2, footerY + 4, { align: 'center' });

    //doc.save(`Poliza_${data.poliza.folio}.pdf`);

    const pdfBlob = doc.output("blob");
    const url = URL.createObjectURL(pdfBlob);

    window.open(url, "_blank");
}

// ============================================================
// MÚLTIPLES PÓLIZAS
// ============================================================
async function generarPDFPolizas(filtros) {
    const data = await GetData({ path: '/Contabilidad/GetPolizasPDF', data: filtros });

    const { jsPDF } = window.jspdf;
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'letter' });
    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);

    const marginLeft = 10, marginRight = 10, marginTop = 42;
    const contentWidth = doc.internal.pageSize.getWidth() - marginLeft - marginRight;
    const pdfWidth = doc.internal.pageSize.getWidth();
    const pageHeight = doc.internal.pageSize.getHeight();
    let currentY = marginTop + 2;

    const TIPO_PROCESO_LABELS = {
        factura_contado: 'Póliza a contado',
        factura_credito: 'Póliza a crédito',
        remision_contado: 'Movimiento de almacén',
        remision_credito: 'Movimiento de almacén',
        remision_anticipo: 'Movimiento de almacén',
    };

    const addPageHeader = () => {
        const currentPage = doc.internal.getCurrentPageInfo().pageNumber;
        doc.setFillColor(...PDF_THEME.primary);
        doc.rect(marginLeft, 8, 4, 26, 'F');
        if (logoBase64) doc.addImage(logoBase64, 'PNG', marginLeft + 8, 12, 22, 14);
        doc.setDrawColor(...PDF_THEME.borderLight);
        doc.setLineWidth(0.4);
        doc.line(marginLeft + 34, 12, marginLeft + 34, 32);
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(12);
        doc.setTextColor(...PDF_THEME.primary);
        doc.text('PÓLIZAS CONTABLES', marginLeft + 40, 20);
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(7.5);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(`Generado: ${new Date().toLocaleString('es-MX')}  ·  ${data.total} pólizas`, marginLeft + 40, 26);
        doc.setDrawColor(...PDF_THEME.accent);
        doc.setLineWidth(0.8);
        doc.line(marginLeft, 35, pdfWidth - marginRight, 35);
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(8);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text(`Página ${currentPage}`, pdfWidth - marginRight, 20, { align: 'right' });
    };

    addPageHeader();

    const tableColumns = [
        { header: '#', dataKey: 'num' },
        { header: 'Cuenta', dataKey: 'cuenta' },
        { header: 'Nombre cuenta', dataKey: 'nombre' },
        { header: 'Centro Costos', dataKey: 'centro' },
        { header: 'Descripción', dataKey: 'descripcion' },
        { header: 'Debe', dataKey: 'debe' },
        { header: 'Haber', dataKey: 'haber' },
    ];

    data.data.forEach((poliza, index) => {
        const estimatedHeight = 50 + poliza.detalles.length * 8;
        if (currentY + estimatedHeight > pageHeight - 20) {
            doc.addPage();
            addPageHeader();
            currentY = marginTop + 2;
        }

        // Encabezado de póliza individual
        doc.setFillColor(...PDF_THEME.bgLight);
        doc.rect(marginLeft, currentY - 2, contentWidth, 20, 'F');
        doc.setDrawColor(...PDF_THEME.accent);
        doc.setLineWidth(1.2);
        doc.line(marginLeft, currentY - 2, marginLeft, currentY + 18);

        const lY = currentY + 4;
        const vY = currentY + 11;

        doc.setFont('helvetica', 'bold');
        doc.setFontSize(7);
        doc.setTextColor(...PDF_THEME.textMid);
        doc.text('REFERENCIA', marginLeft + 4, lY);
        doc.text('RESPONSABLE', pdfWidth / 2 - 15, lY);
        doc.text('FECHA', pdfWidth - marginRight - 4, lY, { align: 'right' });

        doc.setFont('helvetica', 'normal');
        doc.setFontSize(9);
        doc.setTextColor(...PDF_THEME.textDark);
        doc.text(poliza.folio, marginLeft + 4, vY);
        doc.text(poliza.usuario, pdfWidth / 2 - 15, vY);
        doc.text(dateFormatter(poliza.fecha), pdfWidth - marginRight - 4, vY, { align: 'right' });

        const tipoProceso = TIPO_PROCESO_LABELS[poliza.tipo_proceso] ?? '';
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(7);
        doc.setTextColor(...PDF_THEME.textLight);
        doc.text(`UUID: ${poliza.uuid}`, marginLeft + 4, currentY + 16);
        if (tipoProceso) doc.text(tipoProceso, pdfWidth / 2 - 15, currentY + 16);

        // Badges outline
        let badgeX = pdfWidth - marginRight;
        [
            { text: poliza.estado.toUpperCase(), color: poliza.estado.toLowerCase() === 'activo' ? PDF_THEME.success : PDF_THEME.danger },
            { text: poliza.tipo.toUpperCase(), color: PDF_THEME.accent },
        ].forEach(({ text, color }) => {
            doc.setFont('helvetica', 'bold');
            doc.setFontSize(6.5);
            const bW = doc.getTextWidth(text) + 6;
            badgeX -= bW + 2;
            doc.setDrawColor(...color);
            doc.setLineWidth(0.5);
            doc.roundedRect(badgeX, currentY + 11, bW, 5, 1, 1, 'S');
            doc.setTextColor(...color);
            doc.text(text, badgeX + bW / 2, currentY + 14.5, { align: 'center' });
        });

        currentY += 22;

        const rows = poliza.detalles.map((item, i) => ({
            num: i + 1, cuenta: item.codigo, nombre: item.nombrecuenta,
            centro: item.nombre_costos, descripcion: item.descripcion,
            debe: item.debe === 0 ? '' : Currency.format(item.debe),
            haber: item.haber === 0 ? '' : Currency.format(item.haber),
        }));

        agregarTabla(doc, tableColumns, rows, currentY, {
            margin: { left: marginLeft, right: marginRight, top: marginTop },
            columnStyles: {
                0: { cellWidth: 10, halign: 'center' },
                1: { cellWidth: 22 },
                2: { cellWidth: 'auto' },
                3: { cellWidth: 28 },
                4: { cellWidth: 'auto' },
                5: { cellWidth: 26, halign: 'right' },
                6: { cellWidth: 26, halign: 'right' },
            },
            didDrawPage: (hookData) => {
                if (hookData.pageNumber > 1 && doc.internal.getCurrentPageInfo().pageNumber > 1) addPageHeader();
            },
        });

        currentY = doc.lastAutoTable.finalY + 5;

        // Mini totales por póliza
        const totalDebe = poliza.detalles.reduce((s, d) => s + d.debe, 0);
        const totalHaber = poliza.detalles.reduce((s, d) => s + d.haber, 0);
        const diferencia = totalDebe - totalHaber;

        doc.setFillColor(250, 251, 254);
        doc.rect(marginLeft, currentY - 1, contentWidth, 8, 'F');

        const l1 = `Debe: ${Currency.format(totalDebe)}`;
        const l2 = `Haber: ${Currency.format(totalHaber)}`;
        const l3 = `Diferencia: ${Currency.format(Math.abs(diferencia))}`;
        const w1 = doc.getTextWidth(l1);
        const w2 = doc.getTextWidth(l2);
        const gap = (contentWidth - 6 - (w1 + w2 + doc.getTextWidth(l3))) / 2;

        doc.setFont('helvetica', 'normal');
        doc.setFontSize(8);
        doc.setTextColor(...PDF_THEME.textDark);
        doc.text(l1, marginLeft + 3, currentY + 4);
        doc.text(l2, marginLeft + 3 + w1 + gap, currentY + 4);
        doc.setFont('helvetica', 'bold');
        doc.setTextColor(...(Math.abs(diferencia) < 0.01 ? PDF_THEME.success : PDF_THEME.danger));
        doc.text(l3, marginLeft + 3 + w1 + gap + w2 + gap, currentY + 4);

        currentY += 12;

        if (index < data.data.length - 1) {
            doc.setDrawColor(...PDF_THEME.borderLight);
            doc.setLineWidth(0.3);
            doc.line(marginLeft + 20, currentY, pdfWidth - marginRight - 20, currentY);
            currentY += 8;
        }
    });

    //doc.save('Polizas_Filtradas.pdf');

    const pdfBlob = doc.output("blob");
    const url = URL.createObjectURL(pdfBlob);

    window.open(url, "_blank");
}

function agregarPagare(doc, startY, razonSocial) {
    const marginX = 10;
    const boxWidth = 190;

    // Nota pequeña (texto gris)
    doc.setFont('helvetica', 'italic');
    doc.setFontSize(7);
    doc.setTextColor(120);

    doc.text(
        'Las diferencias de centavos que se puedan presentar, son producto del redondeo en los importes.',
        marginX,
        startY
    );

    let currentY = startY + 5;

    // Caja del pagaré
    doc.setDrawColor(51);
    doc.setLineWidth(0.5);
    doc.rect(marginX, currentY, boxWidth, 45); // ajusta altura si crece texto

    currentY += 5;

    doc.setFont('helvetica', 'normal');
    doc.setFontSize(8);
    doc.setTextColor(0);

    const textoPagare = `Debo(emos) y pagaremos en forma incondicional a la orden de ${razonSocial} SA DE CV en la ciudad de San Luis Potosí, San Luis Potosí, el importe que ampara el total de esta factura recibida a nuestra satisfacción el día _______ de _________ de _______. En caso de demora en el pago, causará un interés del _____%, este pagaré es mercantil y está regido por la Ley General de Títulos y Operaciones en su artículo 170.`;

    const textoDividido = doc.splitTextToSize(textoPagare, boxWidth - 6);

    doc.text(textoDividido, marginX + 3, currentY);

    // Línea de firma
    const firmaY = startY + 45;

    doc.line(marginX + 60, firmaY, marginX + 130, firmaY);

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(8);
    doc.text('ACEPTO', marginX + 95, firmaY + 4, { align: 'center' });

    return startY + 55; // retorna nueva Y
}