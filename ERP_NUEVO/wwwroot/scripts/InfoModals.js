function GetInfoGeneral(id_encabezado) {
    GetData({
        path: '/Requisicion/gerPdfData',
        data: {
            id: id_encabezado,
        }
    }).then(async (_res) => {
        switch (_res.requisicion.nat) {
            case "SCTZ":
            case "SCTZI":
            case "SCTZT":
            case "SGTO":
            case "SSR":
            case "SRV":
            case "RM":
            case "RG":
            case "OCD":
            case "RINV":
            case "RINVI":
            case "SINV":
            case "RINVD":
            case "RINVP":
            case "RINVDEV":
            case "SOLINV":
            case "TRAINV":
            case "ENVINV":
            case "DISINV":
            case "VICOT":
            case "VSCOT":
            case "VNCOT":
            case "VINCOT":
            case "VSFAC":
                getDocInfo(_res);
                break;

            case "CTZ":
            case "CTZG":
                getDocOptionsInfo(_res);
                break;

            case "GTO":
            case "OC":
            case "OCD":
            case "VSUC":
            case "VIPED":
            case "VSPED":
            case "VNPED":
            case "VINPED":
            case "VIREM":
            case "VSREM":
            case "VNREM":
            case "VINREM":
                getDocCotizacionInfo(_res);
                break;

            case "OCDI":
                getOCDIInfo(_res);
                break;
        }
    })
}

// Nats que ocultan costos
const NATS_SIN_COSTOS = new Set(['TRAINV', 'SOLINV', 'DISINV']);

async function getDocInfo(_res) {
    const { partidas, requisicion: enc, variaciones, encabezadosAnteriores } = _res;

    const html = [
        buildTablaPartidas(partidas, enc),
        `<div class="grid-tablas">
            <div>
                ${buildTablaFlujo(enc)}
            </div>    
            <div>    
                ${buildTablaImpuestos(_res.impuestos)}
            </div>
            <div>    
                ${buildTablaRelacionados(encabezadosAnteriores, enc)}
            </div>
            <div>
                ${buildTablaVariaciones(variaciones)}
            </div>
        </div>`,
        buildTotalesBlock(partidas, _res.impuestos),
    ].join('');

    _Swal.fire({
        title: `<span style="font-size:1.1rem;font-weight:600;">📄 Detalle — ${enc.folio}</span>`,
        html: html,
        width: '90%',
        showCancelButton: false,
        customClass: {
            htmlContainer: 'swal-doc-container',
        }
    });
}

async function getDocOptionsInfo(_res) {
    const { partidasPadre: partidas, opciones, requisicion: enc, variaciones, encabezadosAnteriores } = _res;

    const htmlOpciones = partidas
        .filter(p => opciones.some(o => o.partidas_id === p.id_partidas))
        .map(p => {
            const rows = opciones
                .filter(o => o.partidas_id === p.id_partidas)
                .map((o, i) => `
                    <tr>
                        <td>${i + 1}</td>
                        <td>${o.cant_ud}</td>
                        <td><code>${o.producto}</code></td>
                        <td>${o.descripcion}</td>
                        <td>${o.unidad}</td>
                        <td>${o.proveedor_nombre}</td>
                        <td>${Currency.format(o.precio)}</td>
                        <td class="fw-semibold">${Currency.format(o.total)}</td>
                    </tr>`).join('');

            return `
                <div class="mb-4">
                    <h6 class="fw-bold mb-2">
                        <code>${p.cve_prod}</code> — ${p.descr_prod}
                    </h6>
                    <div class="table-responsive">
                        <table class="table table-bordered table-sm table-hover" style="min-width:900px;">
                            <thead class="table-secondary">
                                <tr>
                                    <th>#</th><th>Cantidad</th><th>Código</th><th>Descripción</th>
                                    <th>Unidad</th><th>Proveedor</th><th>Costo</th><th>Subtotal</th>
                                </tr>
                            </thead>
                            <tbody>${rows}</tbody>
                        </table>
                    </div>
                </div>`;
        }).join('');

    const htmlTablas = `<div class="grid-tablas">
            <div>
                ${buildTablaFlujo(enc)}
            </div>    
            <div>    
                ${buildTablaImpuestos(_res.impuestos)}
            </div>
            <div>    
                ${buildTablaRelacionados(encabezadosAnteriores, enc)}
            </div>
            <div>
                ${buildTablaVariaciones(variaciones)}
            </div>
        </div>`;

    _Swal.fire({
        title: `<span class="fw-semibold">📋 Detalle — ${enc.folio}</span>`,
        html: htmlOpciones + htmlTablas,
        width: '90%',
        showCancelButton: false,
    });
}

async function getDocCotizacionInfo(_res) {
    const { partidas, requisicion: enc, variaciones, encabezadosAnteriores } = _res;

    const htmlPartidas = buildTablaPartidaConCheckbox(partidas, enc);
    const htmlTotales = buildTotalesBlock(partidas, _res.impuestos);

    const htmlTablas = `<div class="grid-tablas">
            <div>
                ${buildTablaFlujo(enc)}
            </div>    
            <div>    
                ${buildTablaImpuestos(_res.impuestos)}
            </div>
            <div>    
                ${buildTablaRelacionados(encabezadosAnteriores, enc)}
            </div>
            <div>
                ${buildTablaVariaciones(variaciones)}
            </div>
        </div>`;


    _Swal.fire({
        title: `<span class="fw-semibold">🧾 Detalle — ${enc.folio}</span>`,
        html: htmlPartidas + htmlTablas + htmlTotales,
        width: '90%',
        confirmButtonText: '<i class="fa-solid fa-download me-1"></i> Descargar',
        cancelButtonText: 'Cerrar',
        preConfirm: () => handleDescargarPartidas(partidas, enc),
    });
}

async function getOCDIInfo(_res) {
    const { partidas, requisicion: enc, historialMovimientos: movimientos, variaciones, encabezadosAnteriores } = _res;
    const tieneIVA = partidas.some(p => parseFloat(p.iva) > 0);
    const totalGlobal = calcTotalGlobal(partidas, tieneIVA);

    const htmlPartidas = buildTablaPartidas_OCDI(partidas, enc, tieneIVA, totalGlobal);
    const htmlTotales = buildTotalesBlock(partidas, _res.impuestos);

    const htmlMovimientos = buildTablaMovimientos(movimientos);

    const htmlTablas = `<div class="grid-tablas">
            <div>
                ${buildTablaFlujo(enc)}
            </div>    
            <div>    
                ${buildTablaImpuestos(_res.impuestos)}
            </div>
            <div>    
                ${buildTablaRelacionados(encabezadosAnteriores, enc)}
            </div>
            <div>
                ${buildTablaVariaciones(variaciones)}
            </div>
        </div>`;


    _Swal.fire({
        title: `<i class="fa-solid fa-file-invoice me-2"></i>Detalle — ${enc.folio}`,
        html: htmlPartidas + htmlMovimientos + htmlTablas + htmlTotales,
        width: '95%',
        confirmButtonText: '<i class="fa-solid fa-download me-1"></i> Descargar PDF',
        cancelButtonText: '<i class="fa-solid fa-times me-1"></i> Cerrar',
        buttonsStyling: false,
        customClass: {
            confirmButton: 'btn btn-primary me-2',
            cancelButton: 'btn btn-secondary',
            htmlContainer: 'swal-doc-container',
        },
        preConfirm: () => handleDescargarPartidas(partidas, enc),
    });
}

// HELPERS
function buildTablaPartidas(partidas, enc) {
    const ocultarCostos = NATS_SIN_COSTOS.has(enc.nat) ? 'd-none' : '';

    let rows = Object.values(partidas).map(p => `` +
        `<tr>` +
        `    <td>${p.cantidad}</td>` +
        `    <td><code>${p.codigo}</code></td>` +
        `    <td>${p.descripcion}</td>` +
        `    <td>${p.unidad}</td>` +
        `    <td class="${ocultarCostos}">${Currency.format(p.costounitario)}</td>` +
        `    <td class="${ocultarCostos} fw-semibold">${Currency.format(p.total)}</td>` +
        `</tr>`).join('');

    if (enc.observaciones?.length > 0) {
        rows += `` +
            `<tr>` +
            `   <td colspan="2">OBSERVACIONES</td>` +
            `   <td colspan="4">${enc.observaciones}</td>` +
            `</tr>`;
    }

    return `
        <div class="doc-section mb-4">
            <h5 class="doc-section-header">
                <i class="fa-solid fa-table-list"></i> Partidas — ${enc.folio}
            </h5>
            <div class="table-responsive">
                <table class="table table-hover table-sm mb-0">
                    <thead>
                        <tr>
                            <th>Cant.</th>
                            <th>Código</th>
                            <th>Descripción</th>
                            <th>Unidad</th>
                            <th class="${ocultarCostos}">C/U</th>
                            <th class="${ocultarCostos}">Subtotal</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </div>`;
}

function buildTablaImpuestos(impuestos) {
    if (!impuestos?.length) return '';

    const rows = impuestos.map(imp => {
        const esRetencion = imp.es_retencion == 1;
        const badge = esRetencion
            ? `<span class="badge-retencion">Retención</span>`
            : `<span class="badge-impuesto">Traslado</span>`;

        return `
            <tr>
                <td class="fw-semibold">${imp.cve_impuesto}</td>
                <td>${badge}</td>
                <td class="text-end">${Currency.format(imp.subtotal)}</td>
                <td class="text-end fw-semibold">${Currency.format(imp.importe)}</td>
            </tr>`;
    }).join('');

    return `
        <div class="doc-section">
            <h5 class="doc-section-header"><i class="fa-solid fa-receipt"></i> Impuestos</h5>
            <table class="table mb-0">
                <thead>
                    <tr>
                        <th>Impuesto</th>
                        <th>Tipo</th>
                        <th class="text-end">Base</th>
                        <th class="text-end">Importe</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </div>`;
}

function buildTotalesBlock(partidas, impuestos) {
    const subtotal = partidas.reduce((acc, p) => acc + parseFloat(p.total ?? p.totaldescuento ?? 0), 0);

    const traslados = (impuestos ?? []).filter(i => i.es_retencion == 0);
    const retenciones = (impuestos ?? []).filter(i => i.es_retencion == 1);

    const totalTrasladados = traslados.reduce((acc, i) => acc + parseFloat(i.importe), 0);
    const totalRetenciones = retenciones.reduce((acc, i) => acc + parseFloat(i.importe), 0);
    const total = subtotal + totalTrasladados - totalRetenciones;

    const rowImpuesto = (label, valor, cls = 'totals-value') => `
        <tr>
            <td class="totals-label">${label}</td>
            <td class="${cls}">${Currency.format(valor)}</td>
        </tr>`;

    let rows = rowImpuesto('Subtotal', subtotal);

    traslados.forEach(i => {
        rows += rowImpuesto(`${i.cve_impuesto} (traslado)`, i.importe);
    });
    retenciones.forEach(i => {
        rows += rowImpuesto(`${i.cve_impuesto} (retención)`, -i.importe);
    });

    rows += `
        <tr>
            <td class="totals-total-label">Total</td>
            <td class="totals-total-value">${Currency.format(total)}</td>
        </tr>`;

    return `
        <div class="totals-block">
            <table class="totals-table">
                <tbody>${rows}</tbody>
            </table>
        </div>`;
}

function buildTablaFlujo(enc) {
    const aprobadores = [
        { label: 'Solicitante', nombre: enc.solicitante, fecha: enc.fch0 },
        { label: 'Gerente de Área', nombre: enc.gerente, fecha: enc.fch1 },
        { label: 'Compras', nombre: enc.comprador, fecha: enc.fch2 },
        { label: 'Gte. Revisión', nombre: enc.gerenterevision, fecha: enc.fch3 },
        { label: 'Presupuesto', nombre: enc.presupuestorevision, fecha: enc.fch4 },
        { label: 'Presupuesto', nombre: enc.presupuestorevision, fecha: enc.fch5 },
        { label: 'Dirección', nombre: enc.direccion, fecha: enc.fch6 },
    ].filter(a => a.nombre?.trim() && a.fecha);

    if (!aprobadores.length) {
        return `
            <div class="doc-section">
                <h5 class="doc-section-header"><i class="fa-solid fa-arrow-trend-right"></i> Flujo de Aprobación</h5>
                <p class="text-muted p-3 mb-0">Sin aprobaciones registradas.</p>
            </div>`;
    }

    const rows = aprobadores.map((a, i) => {
        const diff = i === 0 ? '' : calcDiferencia(aprobadores[i - 1].fecha, a.fecha);
        return `
            <tr>
                <td>
                    <div class="aprobador-nombre">${a.nombre}</div>
                </td>
                <td class="aprobador-fecha">${dateFormatter(a.fecha)}</td>
                <td>${diff ? `<span class="badge-tiempo">${diff}</span>` : ''}</td>
            </tr>`;
    }).join('');

    return `
        <div class="doc-section">
            <h5 class="doc-section-header"><i class="fa-solid fa-route"></i> Flujo de Aprobación</h5>
            <table class="table mb-0">
                <thead>
                    <tr>
                        <th>Aprobador</th>
                        <th>Fecha</th>
                        <th>Espera</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </div>`;
}
function buildTablaVariaciones(variaciones) {
    if (variaciones.length <= 1) return '';

    const rows = variaciones.map(v => `
        <tr>
            <td>${v.variacion}</td>
            <td>${v.folio}</td>
            <td>${dateFormatter(v.fch)}</td>
        </tr>`).join('');

    return `
        <div class="doc-section">
            <h5 class="doc-section-header">
                <i class="fa-solid fa-code-branch"></i> Historial de Variaciones
            </h5>
            <div class="table-responsive">
                <table class="table table-hover table-sm mb-0">
                    <thead>
                        <tr>
                            <th>#</th>
                            <th>Folio</th>
                            <th>Fecha</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </div>`;
}

function buildTablaRelacionados(encabezadosAnteriores, enc) {
    const rows = encabezadosAnteriores.map(v => {
        const esCurrent = v.id_encabezado === enc.id_encabezado;

        const iconArchivo = (v.path && v.uuid && v.extencion)
            ? `<a class="btn btn-sm green-500" href="/${v.path + v.uuid + v.extencion}" target="_blank">
                   <i class="fa-solid ${getIconFile(v.extencion)}"></i>
               </a>`
            : `<i class="fa-regular fa-file-slash" style="color:var(--text-light);"></i>`;

        const btnPoliza = v.polizas > 0
            ? `<a class="btn btn-sm blue-500" href="/Contabilidad/ConsultarPolizas?q=${v.folio}" target="_blank">
                   <i class="fa-solid fa-arrow-up-right-from-square"></i>
               </a>`
            : '';

        const btnDescargar =
            `<button class="btn btn-sm green-500" onclick="GenerarPDFGeneral(${v.id_encabezado})">
                   <i class="fa-solid fa-file-arrow-down"></i>
               </button>`;

        return `
            <tr class="${esCurrent ? 'table-success' : ''}">
                <td>${v.id_encabezado}</td>
                <td>${v.encabezados_padre}</td>
                <td>${v.folio}</td>
                <td>${iconArchivo}</td>
                <td>${btnPoliza}</td>
                <td>${btnDescargar}</td>
            </tr>`;
    }).join('');

    return `
        <div class="doc-section">
            <h5 class="doc-section-header">
                <i class="fa-solid fa-folder-tree"></i> Documentos Relacionados
            </h5>
            <div class="table-responsive">
                <table class="table table-hover table-sm mb-0" style="min-width:400px;">
                    <thead>
                        <tr>
                            <th>Encabezado</th>
                            <th>Anterior</th>
                            <th>Folio</th>
                            <th>Referencia</th>
                            <th>Poliza</th>
                            <th>PDF</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </div>`;
}

// Helper: calcula diferencia legible entre dos timestamps
function calcDiferencia(fechaAnterior, fechaActual) {
    const diffMs = parseTimestamp(fechaActual) - parseTimestamp(fechaAnterior);
    const totalMinutes = Math.floor(diffMs / 60000);

    const days = Math.floor(totalMinutes / 1440);
    const hours = Math.floor((totalMinutes % 1440) / 60);
    const minutes = totalMinutes % 60;

    return [
        days > 0 ? `${days}d` : '',
        hours > 0 ? `${hours}h` : '',
        minutes > 0 ? `${minutes}m` : '',
    ].filter(Boolean).join(' ') || '< 1m';
}

function buildTablaPartidaConCheckbox(partidas, enc) {
    const rows = Object.values(partidas).map(p => `
        <tr>
            <td class="text-center align-middle">
                <input type="checkbox" class="form-check-input seleccionar-partida" value="${p.id_partidas}">
            </td>
            <td>${p.cantidad}</td>
            <td><code>${p.codigo}</code></td>
            <td>${p.descripcion}</td>
            <td>${p.unidad}</td>
            <td>${p.proveedor ?? ''}</td>
            <td>${Currency.format(p.costounitario)}</td>
            <td>${Currency.format(p.descuento ?? 0)}</td>
            <td class="fw-semibold">${Currency.format(p.totaldescuento ?? p.total)}</td>
        </tr>`).join('');

    return `
        <div class="doc-section mb-4">
            <h5 class="doc-section-header">
                <i class="fa-solid fa-list-check"></i> Partidas — ${enc.folio}
            </h5>
            <div class="table-responsive">
                <table class="table table-hover table-sm mb-0" style="min-width:800px;">
                    <thead>
                        <tr>
                            <th><i class="fa-regular fa-square-check"></i></th>
                            <th>Cant.</th>
                            <th>Código</th>
                            <th>Descripción</th>
                            <th>Unidad</th>
                            <th>Proveedor</th>
                            <th>C/U</th>
                            <th>Descuento</th>
                            <th>Subtotal</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </div>`;
}

function buildTablaPartidas_OCDI(partidas, enc, tieneIVA, totalGlobal) {
    const rows = Object.values(partidas).map(p => `
        <tr>
            <td class="text-center">
                <input type="checkbox"
                    class="form-check-input seleccionar-partida"
                    value="${p.id_partidas}">
            </td>
            <td><strong>${p.cantidad}</strong></td>
            <td><code>${p.codigo}</code></td>
            <td>
                ${p.descripcion}
                ${p.pedimentos ? `<br><small class="text-muted">Pedimento: ${p.pedimentos}</small>` : ''}
            </td>
            <td><span class="badge bg-light text-dark border">${p.unidad}</span></td>
            <td>${Currency.format(p.costounitario)}</td>
            <td class="fw-semibold text-success">${Currency.format(p.total)}</td>
        </tr>`).join('');

    return `
        <div class="doc-section-header">
            <h5 class="doc-section-header">
                <i class="fa-solid fa-list-check"></i> Partidas del Documento ${enc.folio}
            </h5>
            <div class="table-responsive">
                <table class="table table-hover table-sm mb-0" style="min-width:800px;">
                    <thead class="table-light">
                        <tr>
                            <th style="width:50px;"><i class="fa-solid fa-check-square"></i></th>
                            <th>Cant.</th>
                            <th>Código</th>
                            <th>Descripción</th>
                            <th>Unidad</th>
                            <th>C/U</th>
                            <th>Total</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
            <div class="d-flex justify-content-end p-3">
                <table class="total-box">
                    <tbody>
                        <tr>
                            <th><i class="fa-solid fa-calculator me-2"></i>Total Global ${tieneIVA ? '(con IVA)' : ''}</th>
                            <td>${Currency.format(totalGlobal)}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </div>`;
}

function buildTablaMovimientos(movimientos) {
    const rows = movimientos?.length
        ? movimientos.map((mov, i) => `
            <tr>
                <td class="text-center">
                    <span class="step-circle">${i + 1}</span>
                </td>
                <td>
                    <span class="badge-movimiento">
                        <i class="fa-solid fa-arrow-right me-1"></i>${mov.tipo_movimiento}
                    </span>
                </td>
                <td>
                    <i class="fa-solid fa-clock me-1 text-muted"></i>
                    <span class="fw-semibold">${dateFormatter(mov.fecha_registro)}</span>
                </td>
                <td>
                    <i class="fa-solid fa-user me-1 text-muted"></i>
                    <span class="fw-semibold text-primary">${mov.nombreusuario}</span>
                </td>
            </tr>`).join('')
        : `<tr><td colspan="4" class="text-center text-muted py-4">
                <i class="fa-solid fa-clock-rotate-left fa-2x mb-2 d-block opacity-50"></i>
                No hay movimientos registrados
           </td></tr>`;

    return `
        <div class="doc-section mb-4">
            <h5 class="doc-section-header">
                <i class="fa-solid fa-clock-rotate-left"></i> Historial de Movimientos
            </h5>
            <div class="table-responsive">
                <table class="table table-hover table-sm mb-0">
                    <thead class="table-light">
                        <tr>
                            <th style="width:60px;">#</th>
                            <th>Movimiento</th>
                            <th>Fecha</th>
                            <th>Usuario</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        </div>`;
}

// Extrae la lógica de preConfirm que también estaba duplicada
function handleDescargarPartidas(partidas, enc) {
    const checkboxes = document.querySelectorAll('.seleccionar-partida:checked');
    if (checkboxes.length === 0) {
        Swal.showValidationMessage('⚠️ Selecciona al menos una partida para generar el PDF.');
        return false;
    }
    const ids = Array.from(checkboxes).map(cb => parseInt(cb.value));
    const partidasFiltradas = partidas.filter(p => ids.includes(p.id_partidas));
    generarPDFCotizacionPorPartidas(partidasFiltradas, enc);
}

// Calcula total con o sin IVA
function calcTotalGlobal(partidas, tieneIVA) {
    if (!tieneIVA) {
        return partidas.reduce((acc, p) => acc + parseFloat(p.totaldescuento ?? p.total), 0);
    }
    return partidas.reduce((acc, p) => {
        const iva = !isNaN(parseFloat(p.iva)) ? parseFloat(p.iva) : 0;
        return acc + parseFloat(p.total) * (1 + iva);
    }, 0);
}

/*
## Panorama final de la arquitectura

GetInfoGeneral()
  └── DOC_HANDLERS[nat]()
        ├── getDocInfo()             → buildTablaPartidas()
        ├── getDocOptionsInfo()      → htmlOpciones construido inline(es muy específico)
        ├── getDocCotizacionInfo()   → buildTablaPartidaConCheckbox()
        └── getOCDIInfo()            → buildTablaPartidaConCheckbox()(misma base)

Compartidos por todos:
buildTablaFlujo()
buildTablaVariaciones()
buildTablaRelacionados()
calcDiferencia()
calcTotalGlobal()
handleDescargarPartidas()
*/