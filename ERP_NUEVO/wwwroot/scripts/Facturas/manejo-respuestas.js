let modalStack = [];
let ticketsModal;

// ============================================
// UTILIDAD: VALIDAR Y FORMATEAR DATOS
// ============================================
function obtenerValorSeguro(valor, valorPorDefecto = 'N/A') {
    // Verificar si es un objeto
    if (valor !== null && typeof valor === 'object') {
        return valorPorDefecto;
    }

    // Verificar valores inválidos
    if (valor === null ||
        valor === undefined ||
        valor === '' ||
        valor === 'null' ||
        valor === 'undefined' ||
        valor === '[object Object]') {
        return valorPorDefecto;
    }

    // Convertir a string y verificar
    const valorString = String(valor).trim();
    if (valorString === '' || valorString === 'null' || valorString === 'undefined') {
        return valorPorDefecto;
    }

    return valorString;
}

function formatearMoneda(valor) {
    if (!valor || isNaN(parseFloat(valor))) {
        return 'N/A';
    }
    return parseFloat(valor).toLocaleString('es-MX', {
        style: 'currency',
        currency: 'MXN'
    });
}

function construirSerieFolio(serie, folio) {
    const serieSegura = obtenerValorSeguro(serie, '');
    const folioSeguro = obtenerValorSeguro(folio, '');

    if (!serieSegura && !folioSeguro) {
        return 'N/A';
    }

    if (!serieSegura) {
        return folioSeguro;
    }

    if (!folioSeguro) {
        return serieSegura;
    }

    return `${serieSegura}-${folioSeguro}`;
}

function obtenerPropiedadSegura(objeto, propiedad, valorPorDefecto = 'N/A') {
    try {
        if (!objeto || typeof objeto !== 'object') {
            return valorPorDefecto;
        }

        const valor = objeto[propiedad];
        return obtenerValorSeguro(valor, valorPorDefecto);
    } catch (e) {
        console.error(`Error al obtener propiedad ${propiedad}:`, e);
        return valorPorDefecto;
    }
}

// ============================================
// MODAL DE PROGRESO MEJORADO
// ============================================
function mostrarModalProgresoMejorado(total) {
    const modalHTML = `
        <div id="progressModal" style="
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: rgba(0, 0, 0, 0.85);
            backdrop-filter: blur(4px);
            z-index: 10000;
            display: flex;
            justify-content: center;
            align-items: center;
            animation: fadeIn 0.3s ease-in-out;
        ">
            <div style="
                background: white;
                border-radius: 20px;
                padding: 2.5rem;
                max-width: 550px;
                width: 90%;
                box-shadow: 0 25px 80px rgba(0, 0, 0, 0.4);
                animation: slideUp 0.4s ease-out;
            ">
                <!-- Header -->
                <div style="text-align: center; margin-bottom: 2rem;">
                    <div style="
                        width: 80px;
                        height: 80px;
                        margin: 0 auto 1rem;
                        background: linear-gradient(135deg, #7c3aed 0%, #a855f7 100%);
                        border-radius: 50%;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        animation: spin 2s linear infinite;
                    ">
                        <i class="fas fa-cog" style="font-size: 2.5rem; color: white;"></i>
                    </div>
                    <h3 style="
                        color: #1e293b;
                        margin: 0;
                        font-size: 1.5rem;
                        font-weight: 700;
                    ">
                        Procesando Documentos
                    </h3>
                    <p style="color: #64748b; margin-top: 0.5rem; font-size: 0.9rem;">
                        Por favor espere, esto puede tardar unos minutos
                    </p>
                </div>

                <!-- Progress Info -->
                <div style="
                    background: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%);
                    border-radius: 12px;
                    padding: 1.5rem;
                    margin-bottom: 1.5rem;
                    border: 1px solid #e2e8f0;
                ">
                    <div style="
                        display: flex;
                        justify-content: space-between;
                        margin-bottom: 1rem;
                        font-weight: 600;
                        color: #374151;
                    ">
                        <span><i class="fas fa-file-invoice"></i> Total Documentos</span>
                        <span id="progressText" style="color: #7c3aed;">${total}</span>
                    </div>

                    <!-- Progress Bar -->
                    <div style="
                        background: #e5e7eb;
                        height: 8px;
                        border-radius: 10px;
                        overflow: hidden;
                        position: relative;
                    ">
                        <div id="progressBar" style="
                            background: linear-gradient(90deg, #7c3aed 0%, #a855f7 50%, #7c3aed 100%);
                            background-size: 200% 100%;
                            height: 100%;
                            width: 0%;
                            border-radius: 10px;
                            transition: width 0.5s ease-out;
                            animation: shimmer 2s linear infinite;
                        "></div>
                    </div>

                    <!-- Percentage -->
                    <div style="
                        text-align: center;
                        margin-top: 0.75rem;
                        font-size: 0.85rem;
                        color: #64748b;
                        font-weight: 600;
                    " id="progressPercentage">
                        0%
                    </div>
                </div>

                <!-- Current Status -->
                <div style="
                    padding: 1.25rem;
                    background: linear-gradient(135deg, #ede9fe 0%, #ddd6fe 100%);
                    border-radius: 12px;
                    border-left: 4px solid #7c3aed;
                ">
                    <div style="
                        display: flex;
                        align-items: center;
                        gap: 0.75rem;
                        margin-bottom: 0.5rem;
                    ">
                        <i class="fas fa-circle-notch fa-spin" style="color: #7c3aed; font-size: 1rem;"></i>
                        <strong style="color: #5b21b6; font-size: 0.95rem;">Estado actual:</strong>
                    </div>
                    <div id="currentStatus" style="
                        color: #6b21a8;
                        font-weight: 600;
                        font-size: 1rem;
                        margin-left: 1.75rem;
                    ">
                        Preparando solicitud...
                    </div>
                </div>

                <!-- Steps Indicator -->
                <div id="stepsIndicator" style="
                    margin-top: 1.5rem;
                    display: grid;
                    grid-template-columns: repeat(4, 1fr);
                    gap: 0.5rem;
                ">
                    <div class="step-indicator" data-step="1" style="
                        height: 4px;
                        background: #e5e7eb;
                        border-radius: 2px;
                        transition: background 0.3s;
                    "></div>
                    <div class="step-indicator" data-step="2" style="
                        height: 4px;
                        background: #e5e7eb;
                        border-radius: 2px;
                        transition: background 0.3s;
                    "></div>
                    <div class="step-indicator" data-step="3" style="
                        height: 4px;
                        background: #e5e7eb;
                        border-radius: 2px;
                        transition: background 0.3s;
                    "></div>
                    <div class="step-indicator" data-step="4" style="
                        height: 4px;
                        background: #e5e7eb;
                        border-radius: 2px;
                        transition: background 0.3s;
                    "></div>
                </div>

                <!-- Warning -->
                <p style="
                    margin-top: 1.5rem;
                    color: #f59e0b;
                    font-size: 0.85rem;
                    text-align: center;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    gap: 0.5rem;
                ">
                    <i class="fas fa-exclamation-triangle"></i>
                    No cierre esta ventana ni actualice la página
                </p>
            </div>
        </div>
        <style>
            @keyframes fadeIn {
                from { opacity: 0; }
                to { opacity: 1; }
            }
            @keyframes slideUp {
                from {
                    transform: translateY(30px);
                    opacity: 0;
                }
                to {
                    transform: translateY(0);
                    opacity: 1;
                }
            }
            @keyframes spin {
                from { transform: rotate(0deg); }
                to { transform: rotate(360deg); }
            }
            @keyframes shimmer {
                0% { background-position: 200% 0; }
                100% { background-position: -200% 0; }
            }
        </style>
    `;

    document.body.insertAdjacentHTML('beforeend', modalHTML);
    return document.getElementById('progressModal');
}

// ============================================
// ACTUALIZAR PROGRESO DETALLADO
// ============================================
function actualizarProgresoDetallado(modal, paso, mensaje) {
    if (!modal) return;

    const statusElement = modal.querySelector('#currentStatus');
    const progressBar = modal.querySelector('#progressBar');
    const progressPercentage = modal.querySelector('#progressPercentage');
    const stepIndicators = modal.querySelectorAll('.step-indicator');

    if (statusElement) {
        statusElement.textContent = mensaje;
    }

    // Actualizar barra de progreso
    const porcentaje = (paso / 4) * 100;
    if (progressBar) {
        progressBar.style.width = `${porcentaje}%`;
    }
    if (progressPercentage) {
        progressPercentage.textContent = `${Math.round(porcentaje)}%`;
    }

    // Actualizar indicadores de pasos
    stepIndicators.forEach((indicator, index) => {
        if (index < paso) {
            indicator.style.background = 'linear-gradient(90deg, #7c3aed, #a855f7)';
        }
    });
}

// ============================================
// SIMULAR PROGRESO EXITOSO
// ============================================
async function simularProgresoExitoso(modal) {
    const pasos = [
        { paso: 2, mensaje: 'Creando remisiones...' },
        { paso: 3, mensaje: 'Generando facturas...' },
        { paso: 4, mensaje: 'Timbrando documentos...' }
    ];

    for (const { paso, mensaje } of pasos) {
        actualizarProgresoDetallado(modal, paso, mensaje);
        await new Promise(resolve => setTimeout(resolve, 300));
    }

    // Paso final
    actualizarProgresoDetallado(modal, 4, '✓ Proceso completado exitosamente');
    await new Promise(resolve => setTimeout(resolve, 500));
}

// ============================================
// FUNCIONES DE ERROR MEJORADAS
// ============================================
function mostrarErrorServidor(status, errorText) {
    Swal.fire({
        title: '🚨 Error del Servidor',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <div style="background: #fee2e2; padding: 1.5rem; border-radius: 12px; margin-bottom: 1rem; border-left: 4px solid #dc2626;">
                    <div style="font-size: 1.2rem; font-weight: 700; color: #dc2626; margin-bottom: 0.75rem;">
                        <i class="fas fa-server"></i>
                        Error HTTP ${status}
                    </div>
                    <div style="color: #991b1b; font-size: 0.9rem; line-height: 1.6;">
                        El servidor no pudo procesar la solicitud correctamente.
                    </div>
                </div>

                <div style="background: #f3f4f6; padding: 1rem; border-radius: 8px; margin-bottom: 1rem;">
                    <strong style="color: #374151; font-size: 0.9rem;">Detalles técnicos:</strong>
                    <div style="
                        margin-top: 0.5rem;
                        padding: 0.75rem;
                        background: white;
                        border-radius: 6px;
                        color: #6b7280;
                        font-size: 0.85rem;
                        max-height: 150px;
                        overflow-y: auto;
                        font-family: monospace;
                    ">
                        ${errorText || 'Error desconocido del servidor'}
                    </div>
                </div>

                <div style="background: #fef3c7; padding: 1rem; border-radius: 8px;">
                    <div style="color: #92400e; font-size: 0.85rem;">
                        <strong><i class="fas fa-lightbulb"></i> Sugerencias:</strong>
                        <ul style="margin: 0.5rem 0 0 1.25rem; padding: 0;">
                            <li>Intente nuevamente en unos momentos</li>
                            <li>Verifique su conexión a internet</li>
                            <li>Contacte al administrador si el error persiste</li>
                        </ul>
                    </div>
                </div>
            </div>
        `,
        icon: 'error',
        confirmButtonText: 'Entendido',
        confirmButtonColor: '#7c3aed',
        width: '650px'
    });
}

function mostrarErrorFormato() {
    Swal.fire({
        title: '⚠️ Error de Formato',
        html: `
            <div style="text-align: center; padding: 1.5rem;">
                <div style="
                    width: 80px;
                    height: 80px;
                    margin: 0 auto 1.5rem;
                    background: linear-gradient(135deg, #fef3c7, #fde68a);
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                ">
                    <i class="fas fa-file-excel" style="font-size: 2.5rem; color: #d97706;"></i>
                </div>
                <p style="color: #78350f; font-size: 1.1rem; margin-bottom: 1rem;">
                    La respuesta del servidor no tiene el formato esperado
                </p>
                <p style="color: #92400e; font-size: 0.9rem;">
                    Es posible que haya ocurrido un error durante el procesamiento.
                    Por favor, intente nuevamente.
                </p>
            </div>
        `,
        icon: 'warning',
        confirmButtonText: 'Reintentar',
        confirmButtonColor: '#7c3aed',
        showCancelButton: true,
        cancelButtonText: 'Cancelar',
        width: '550px'
    });
}

function mostrarErrorConexion(error) {
    Swal.fire({
        title: '🔌 Error de Conexión',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <div style="background: linear-gradient(135deg, #fee2e2, #fecaca); padding: 1.5rem; border-radius: 12px; margin-bottom: 1.5rem;">
                    <div style="text-align: center;">
                        <i class="fas fa-wifi" style="font-size: 3rem; color: #dc2626; margin-bottom: 1rem;"></i>
                        <div style="font-size: 1.1rem; font-weight: 700; color: #991b1b;">
                            No se pudo conectar con el servidor
                        </div>
                    </div>
                </div>

                <div style="background: #f3f4f6; padding: 1rem; border-radius: 8px; margin-bottom: 1rem;">
                    <strong style="color: #374151;">Detalles del error:</strong>
                    <div style="
                        margin-top: 0.5rem;
                        padding: 0.75rem;
                        background: #1f2937;
                        border-radius: 6px;
                        color: #f3f4f6;
                        font-size: 0.85rem;
                        font-family: monospace;
                    ">
                        ${error.message}
                    </div>
                </div>

                <div style="background: #dbeafe; padding: 1rem; border-radius: 8px;">
                    <div style="color: #1e3a8a; font-size: 0.9rem;">
                        <strong><i class="fas fa-info-circle"></i> Verifique lo siguiente:</strong>
                        <ul style="margin: 0.5rem 0 0 1.25rem; padding: 0; line-height: 1.8;">
                            <li>Su conexión a internet está activa</li>
                            <li>El servidor está disponible</li>
                            <li>No hay problemas de firewall o proxy</li>
                            <li>La VPN (si aplica) está funcionando</li>
                        </ul>
                    </div>
                </div>
            </div>
        `,
        icon: 'error',
        confirmButtonText: 'Reintentar',
        confirmButtonColor: '#7c3aed',
        showCancelButton: true,
        cancelButtonText: 'Cancelar',
        width: '650px'
    });
}

// ============================================
// FUNCIÓN PARA MOSTRAR ÉXITO (MEJORADA CON VALIDACIONES)
// ============================================
function mostrarResumenExitoso(result, totalDocumentos, esRetorno = false) {
    // Validar que result tenga la estructura esperada
    if (!result || typeof result !== 'object') {
        console.error('Error: result no es un objeto válido', result);
        Swal.fire({
            icon: 'error',
            title: 'Error',
            text: 'No se pudo procesar la respuesta del servidor',
            confirmButtonColor: '#7c3aed'
        });
        return;
    }

    const resumen = result.resumen || {};
    let detalles = result.detalles || {};

    // CORRECCIÓN AUTOMÁTICA: Si los IDs vienen como objetos, extraer el valor
    if (detalles.pedidoId && typeof detalles.pedidoId === 'object') {
        console.warn('⚠️ pedidoId es un objeto, extrayendo valor...', detalles.pedidoId);
        detalles.pedidoId = detalles.pedidoId.valor || detalles.pedidoId.id || detalles.pedidoId.Value || null;
    }

    if (detalles.remisionId && typeof detalles.remisionId === 'object') {
        console.warn('⚠️ remisionId es un objeto, extrayendo valor...', detalles.remisionId);
        detalles.remisionId = detalles.remisionId.valor || detalles.remisionId.id || detalles.remisionId.Value || null;
    }

    if (detalles.facturaId && typeof detalles.facturaId === 'object') {
        console.warn('⚠️ facturaId es un objeto, extrayendo valor...', detalles.facturaId);
        detalles.facturaId = detalles.facturaId.valor || detalles.facturaId.id || detalles.facturaId.Value || null;
    }

    // Validar datos con valores seguros usando la función de acceso seguro
    const montoFormateado = formatearMoneda(obtenerPropiedadSegura(detalles, 'total'));
    const serieFolio = construirSerieFolio(
        obtenerPropiedadSegura(detalles, 'serie', ''),
        obtenerPropiedadSegura(detalles, 'folio', '')
    );
    const uuid = obtenerPropiedadSegura(detalles, 'uuid');
    const cantidadProductos = obtenerPropiedadSegura(detalles, 'cantidadProductos', '0');
    const razonSocial = obtenerPropiedadSegura(detalles, 'razonSocial');
    const rfcCliente = obtenerPropiedadSegura(detalles, 'rfcCliente');
    const pedidoId = obtenerPropiedadSegura(detalles, 'pedidoId');
    const remisionId = obtenerPropiedadSegura(detalles, 'remisionId');
    const facturaId = obtenerPropiedadSegura(detalles, 'facturaId');
    const documentosProcesados = obtenerPropiedadSegura(resumen, 'documentosProcesados', totalDocumentos);
    const tiempoFinalizacion = obtenerPropiedadSegura(resumen, 'tiempoFinalizacion', new Date().toLocaleString('es-MX'));
    const mensajeFinal = obtenerPropiedadSegura(detalles, 'mensajeFinal', 'Proceso completado');
    const uuidNotaCredito = obtenerPropiedadSegura(detalles, 'uuidNotaCredito', '');
    // Debug log para verificar valores
    console.log('Valores procesados:', {
        uuid,
        serieFolio,
        montoFormateado,
        cantidadProductos,
        razonSocial,
        rfcCliente,
        pedidoId,
        remisionId,
        facturaId
    });

    // Guardar estado del modal para poder volver (solo si no es un retorno)
    if (!esRetorno) {
        guardarModalEnStack({ result, totalDocumentos });
    }

    Swal.fire({
        title: '✅ Proceso Completado con Éxito',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <!-- Banner de Éxito -->
                <div style="
                    background: linear-gradient(135deg, #d1fae5 0%, #a7f3d0 100%);
                    padding: 1.5rem;
                    border-radius: 12px;
                    margin-bottom: 1.5rem;
                    border-left: 4px solid #059669;
                    animation: slideIn 0.5s ease-out;
                ">
                    <div style="font-size: 1.4rem; font-weight: 700; color: #047857; margin-bottom: 0.5rem;">
                        <i class="fas fa-check-circle"></i>
                        ${documentosProcesados} Documento(s) Facturado(s)
                    </div>
                    <div style="font-size: 1.4rem; font-weight: 700; color: #047857; margin-bottom: 0.5rem;">
                        ${mensajeFinal}
                    </div>
                    <div style="color: #065f46; font-size: 0.95rem;">
                        <i class="fas fa-clock"></i> ${tiempoFinalizacion}
                    </div>
                </div>

                <!-- Información de la Factura -->
                <div style="background: #f8fafc; padding: 1.25rem; border-radius: 10px; margin-bottom: 1rem; border: 1px solid #e2e8f0;">
                    <h4 style="color: #1e293b; margin-bottom: 1rem; display: flex; align-items: center; gap: 0.5rem;">
                        <i class="fas fa-file-invoice-dollar" style="color: #7c3aed;"></i>
                        Comprobante Fiscal
                    </h4>

                    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; font-size: 0.9rem;">
                        ${uuid !== 'N/A' ? `
                        <div style="grid-column: 1 / -1;">
                            <strong style="color: #64748b;">UUID:</strong><br>
                            <span style="color: #1e293b; font-family: monospace; font-size: 0.85rem; word-break: break-all;">${uuid}</span>
                        </div>
                        ` : ''}
                        ${serieFolio !== 'N/A' ? `
                        <div>
                            <strong style="color: #64748b;">Serie-Folio:</strong><br>
                            <span style="color: #1e293b; font-weight: 600;">${serieFolio}</span>
                        </div>
                        ` : ''}
                        ${montoFormateado !== 'N/A' ? `
                        <div>
                            <strong style="color: #64748b;">Total:</strong><br>
                            <span style="color: #059669; font-weight: 700; font-size: 1.1rem;">${montoFormateado}</span>
                        </div>
                        ` : ''}
                        ${cantidadProductos !== '0' && cantidadProductos !== 'N/A' ? `
                        <div>
                            <strong style="color: #64748b;">Productos:</strong><br>
                            <span style="color: #1e293b; font-weight: 600;">${cantidadProductos} partidas</span>
                        </div>
                        ` : ''}
                    </div>
                </div>

                <!-- Información del Cliente -->
                ${razonSocial !== 'N/A' || rfcCliente !== 'N/A' ? `
                <div style="background: #fefce8; padding: 1rem; border-radius: 8px; margin-bottom: 1rem; border-left: 3px solid #eab308;">
                    <h5 style="color: #854d0e; margin-bottom: 0.5rem; font-size: 0.95rem;">
                        <i class="fas fa-user-tie"></i> Cliente
                    </h5>
                    <div style="color: #713f12; font-size: 0.85rem;">
                        ${razonSocial !== 'N/A' ? `<strong>${razonSocial}</strong><br>` : ''}
                        ${rfcCliente !== 'N/A' ? `RFC: ${rfcCliente}` : ''}
                    </div>
                </div>
                ` : ''}

                <!-- Detalles del Proceso -->
                ${pedidoId !== 'N/A' || remisionId !== 'N/A' || facturaId !== 'N/A' ? `
                <div style="background: #ede9fe; padding: 1rem; border-radius: 8px; margin-bottom: 1rem;">
                    <h5 style="color: #5b21b6; margin-bottom: 0.75rem; font-size: 0.95rem;">
                        <i class="fas fa-tasks"></i> IDs Generados
                    </h5>
                    <ul style="color: #6b21a8; margin: 0; padding-left: 1.5rem; font-size: 0.85rem; line-height: 1.8;">
                        ${pedidoId !== 'N/A' ? `<li>Pedido: <strong>#${pedidoId}</strong></li>` : ''}
                        ${remisionId !== 'N/A' ? `<li>Remisión: <strong>#${remisionId}</strong></li>` : ''}
                        ${facturaId !== 'N/A' ? `<li>Factura: <strong>#${facturaId}</strong></li>` : ''}
                    </ul>
                </div>
                ` : ''}
            </div>
        `,
        icon: 'success',
        showCancelButton: true,
        showDenyButton: true,
        showCloseButton: true,
        confirmButtonText: '<i class="fas fa-download"></i> Descargar',
        denyButtonText: '<i class="fas fa-envelope"></i> Enviar correo',
        cancelButtonText: '<i class="fas fa-times"></i> Cerrar',
        confirmButtonColor: '#7c3aed',
        denyButtonColor: '#3b82f6',
        cancelButtonColor: '#6b7280',
        width: '700px',
        allowOutsideClick: false,
        allowEscapeKey: false,
    }).then((modalResult) => {
        if (modalResult.isConfirmed) {
            mostrarOpcionesDescarga(detalles, serieFolio, montoFormateado, result, totalDocumentos, uuidNotaCredito);
        } else if (modalResult.isDenied) {
            mostrarModalEnvioCorreo(detalles, result, totalDocumentos);
        } else {
            // Al cerrar, limpiar el stack y recargar
            limpiarStackModales();
            setTimeout(() => {
                recargarVista();
            }, 500);
        }
    });
}

// ============================================
// FUNCIÓN PARA MOSTRAR OPCIONES DE DESCARGA
// ============================================
function mostrarOpcionesDescarga(detalles, seriefolio, montoFormateado, resultOriginal, totalDocumentos, uuidNotaCredito = '') {
    const pdfUrl = obtenerPropiedadSegura(detalles, 'pdfUrl');
    const xmlUrl = obtenerPropiedadSegura(detalles, 'xmlUrl');
    const uuid = obtenerPropiedadSegura(detalles, 'uuid');
    const isEn = detalles.isEn === true || detalles.IsEn === true;
    const pdfUrlEN = obtenerPropiedadSegura(detalles, 'pdfUrlEN');
    const hayNC = uuidNotaCredito && uuidNotaCredito !== 'N/A' && uuidNotaCredito !== '';
    const pdfUrlNC = hayNC ? `/Facturacion/facturas/${uuidNotaCredito}.pdf` : '';
    const xmlUrlNC = hayNC ? `/Facturacion/xml_timbrados/${uuidNotaCredito}.xml` : '';

    if (pdfUrl === 'N/A' && xmlUrl === 'N/A') {
        Swal.fire({
            icon: 'error',
            title: 'No hay archivos disponibles',
            text: 'No se encontraron archivos PDF o XML para descargar.',
            confirmButtonColor: '#7c3aed'
        }).then(() => {
            modalStack.pop();
            mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
        });
        return;
    }

    Swal.fire({
        title: '📥 Descargar Comprobante',
        html: `
            <div style="padding: 1rem;">
                <p style="color: #6b7280; margin-bottom: 1.5rem; font-size: 0.95rem;">
                    Selecciona el formato que deseas descargar:
                </p>

                <div style="display: grid; gap: 1rem;">
                    ${pdfUrl !== 'N/A' ? `
                    <a href="${pdfUrl}" download target="_blank" style="
                        display: flex; align-items: center; gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #dc2626 0%, #ef4444 100%);
                        color: white; text-decoration: none; border-radius: 12px;
                        font-weight: 600; font-size: 1rem; transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(220, 38, 38, 0.3);"
                        onmouseover="this.style.transform='translateY(-2px)'"
                        onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-pdf" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Descargar PDF</div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">Versión imprimible</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </a>
                    ` : ''}

                    ${ isEn && pdfUrlEN !== 'N/A' ? `
                    <a href="${pdfUrlEN}" download target="_blank" style="
                        display: flex; align-items: center; gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #1d4ed8 0%, #3b82f6 100%);
                        color: white; text-decoration: none; border-radius: 12px;
                        font-weight: 600; font-size: 1rem; transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(29, 78, 216, 0.3);"
                        onmouseover="this.style.transform='translateY(-2px)'"
                        onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-pdf" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Download PDF <span style="font-size:0.75rem; background:rgba(255,255,255,0.25); padding:2px 6px; border-radius:4px; margin-left:4px;">EN</span></div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">English version</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </a>
                    ` : ''}

                    ${xmlUrl !== 'N/A' ? `
                    <a href="${xmlUrl}" download target="_blank" style="
                        display: flex; align-items: center; gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #059669 0%, #10b981 100%);
                        color: white; text-decoration: none; border-radius: 12px;
                        font-weight: 600; font-size: 1rem; transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(5, 150, 105, 0.3);"
                        onmouseover="this.style.transform='translateY(-2px)'"
                        onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-code" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Descargar XML</div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">Archivo oficial del SAT</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </a>
                    ` : ''}
    ${hayNC ? `
    <div style="
        border-top: 2px dashed #e5e7eb;
        padding-top: 1rem;
        margin-top: 0.5rem;
    ">
        <div style="
            font-size: 0.8rem;
            font-weight: 700;
            color: #6b7280;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            margin-bottom: 0.75rem;
        ">
            <i class="fas fa-file-invoice"></i> Nota de Crédito (anticipo)
        </div>

        <a href="${pdfUrlNC}" download target="_blank" style="
            display: flex; align-items: center; gap: 1rem;
            padding: 1rem;
            background: linear-gradient(135deg, #f97316 0%, #fb923c 100%);
            color: white; text-decoration: none; border-radius: 10px;
            font-weight: 600; font-size: 0.95rem; transition: all 0.3s;
            box-shadow: 0 4px 6px rgba(249, 115, 22, 0.3);
            margin-bottom: 0.5rem;"
            onmouseover="this.style.transform='translateY(-2px)'"
            onmouseout="this.style.transform='translateY(0)'">
            <i class="fas fa-file-pdf" style="font-size: 1.75rem;"></i>
            <div style="text-align: left; flex: 1;">
                <div>PDF — Nota de Crédito</div>
                <div style="font-size: 0.75rem; opacity: 0.9;">UUID: ${uuidNotaCredito.substring(0, 8)}...</div>
            </div>
            <i class="fas fa-download"></i>
        </a>

        <a href="${xmlUrlNC}" download target="_blank" style="
            display: flex; align-items: center; gap: 1rem;
            padding: 1rem;
            background: linear-gradient(135deg, #0d9488 0%, #14b8a6 100%);
            color: white; text-decoration: none; border-radius: 10px;
            font-weight: 600; font-size: 0.95rem; transition: all 0.3s;
            box-shadow: 0 4px 6px rgba(13, 148, 136, 0.3);"
            onmouseover="this.style.transform='translateY(-2px)'"
            onmouseout="this.style.transform='translateY(0)'">
            <i class="fas fa-file-code" style="font-size: 1.75rem;"></i>
            <div style="text-align: left; flex: 1;">
                <div>XML — Nota de Crédito</div>
                <div style="font-size: 0.75rem; opacity: 0.9;">Archivo oficial del SAT</div>
            </div>
            <i class="fas fa-download"></i>
        </a>
    </div>
    ` : ''}

                    ${pdfUrl !== 'N/A' && xmlUrl !== 'N/A' ? `
                    <button id="btnDescargarZip" type="button" style="
                        display: flex; align-items: center; gap: 1rem;
                        padding: 1.25rem;
                        background: linear-gradient(135deg, #7c3aed 0%, #a855f7 100%);
                        color: white; border: none; border-radius: 12px;
                        font-weight: 600; font-size: 1rem; cursor: pointer;
                        transition: all 0.3s;
                        box-shadow: 0 4px 6px rgba(124, 58, 237, 0.3);"
                        onmouseover="this.style.transform='translateY(-2px)'"
                        onmouseout="this.style.transform='translateY(0)'">
                        <i class="fas fa-file-archive" style="font-size: 2rem;"></i>
                        <div style="text-align: left; flex: 1;">
                            <div style="font-size: 1.1rem;">Descargar Ambos</div>
                            <div style="font-size: 0.8rem; opacity: 0.9;">PDF + XML en ZIP</div>
                        </div>
                        <i class="fas fa-download"></i>
                    </button>
                    ` : ''}
                </div>
            </div>
        `,
        // botones, width, allowOutsideClick… sin cambios
        showCancelButton: true,
        showDenyButton: true,
        confirmButtonText: '<i class="fas fa-check"></i> Finalizar',
        denyButtonText: '<i class="fas fa-arrow-left"></i> Volver',
        cancelButtonText: '<i class="fas fa-times"></i> Cerrar',
        confirmButtonColor: '#059669',
        denyButtonColor: '#6b7280',
        cancelButtonColor: '#dc2626',
        width: '600px',
        allowOutsideClick: false,
        didOpen: () => {
            const btnZip = document.getElementById('btnDescargarZip');
            if (btnZip) {
                btnZip.addEventListener('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    descargarAmbosArchivos(pdfUrl, xmlUrl, seriefolio, uuid);
                });
            }
        }
    }).then((result) => {
        if (result.isDenied) {
            modalStack.pop();
            mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
        } else if (result.isConfirmed || result.isDismissed) {
            limpiarStackModales();
            setTimeout(() => recargarVista(), 500);
        }
    });
}
// ============================================
// FUNCIÓN PARA DESCARGAR AMBOS ARCHIVOS EN ZIP
// ============================================
async function descargarAmbosArchivos(pdfUrl, xmlUrl, serieFolio = '', uuid = '') {
    try {
        const zip = new JSZip();

        const nombreBase = uuid && uuid !== 'N/A' ? uuid : (serieFolio && serieFolio !== 'N/A' ? serieFolio : 'factura');
        const nombreBaseLimpio = nombreBase.replace(/[^a-zA-Z0-9-]/g, '_');

        const descargas = [];

        if (pdfUrl && pdfUrl !== 'N/A') {
            descargas.push(
                fetch(pdfUrl)
                    .then(res => res.blob())
                    .then(blob => zip.file(`${nombreBaseLimpio}.pdf`, blob))
            );
        }

        if (xmlUrl && xmlUrl !== 'N/A') {
            descargas.push(
                fetch(xmlUrl)
                    .then(res => res.blob())
                    .then(blob => zip.file(`${nombreBaseLimpio}.xml`, blob))
            );
        }

        await Promise.all(descargas);

        const zipBlob = await zip.generateAsync({
            type: "blob",
            compression: "DEFLATE",
            compressionOptions: {
                level: 9
            }
        });

        const nombreZip = serieFolio && serieFolio !== 'N/A'
            ? `factura_${serieFolio.replace(/[^a-zA-Z0-9-]/g, '_')}.zip`
            : `factura_${new Date().getTime()}.zip`;

        const link = document.createElement('a');
        link.href = URL.createObjectURL(zipBlob);
        link.download = nombreZip;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        URL.revokeObjectURL(link.href);

    } catch (error) {
        console.error('Error al crear ZIP:', error);
        Swal.fire({
            icon: 'error',
            title: 'Error al crear ZIP',
            html: `
                <div style="padding: 1rem;">
                    <p style="color: #6b7280; margin-bottom: 1rem;">
                        No se pudo crear el archivo ZIP. Intentando descarga individual...
                    </p>
                    <div style="
                        background: #fef3c7;
                        padding: 0.75rem;
                        border-radius: 8px;
                        font-size: 0.85rem;
                        color: #92400e;
                    ">
                        <strong>Error:</strong> ${error.message}
                    </div>
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: 'Descargar por separado',
            cancelButtonText: 'Cancelar',
            confirmButtonColor: '#7c3aed'
        }).then((result) => {
            if (result.isConfirmed) {
                const nombreBaseLimpio = (uuid && uuid !== 'N/A' ? uuid : (serieFolio && serieFolio !== 'N/A' ? serieFolio : 'factura')).replace(/[^a-zA-Z0-9-]/g, '_');
                if (pdfUrl && pdfUrl !== 'N/A') {
                    descargarArchivo(pdfUrl, `${nombreBaseLimpio}.pdf`);
                }
                if (xmlUrl && xmlUrl !== 'N/A') {
                    setTimeout(() => descargarArchivo(xmlUrl, `${nombreBaseLimpio}.xml`), 500);
                }
            }
        });
    }
}
function descargarArchivo(url, nombre) {
    const a = document.createElement("a");
    a.href = url;
    a.download = nombre;
    document.body.appendChild(a);
    a.click();
    a.remove();
}

// ============================================
// FUNCIÓN CENTRALIZADA PARA RECARGAR LA VISTA
// ============================================
function recargarVista() {
    if (typeof cargarDocumentos === 'function') {
        cargarDocumentos();
    } else {
        location.reload();
    }
}

// ============================================
// FUNCIÓN PARA MOSTRAR ERRORES POR PASO (MEJORADA)
// ============================================
function mostrarErrorPorPaso(result, totalDocumentos) {
    const pasosInfo = {
        'GuardarDesdeDocumentos': {
            nombre: 'Guardar Documentos Base',
            icono: '📄',
            color: '#3b82f6',
            descripcion: 'Error al crear los documentos base en el sistema'
        },
        'GuardarRemisionDesdePedidos': {
            nombre: 'Crear Remisiones',
            icono: '📦',
            color: '#8b5cf6',
            descripcion: 'Error al generar las remisiones desde los pedidos'
        },
        'GuardarFacturaDesdeDocs': {
            nombre: 'Crear Facturas',
            icono: '🧾',
            color: '#ec4899',
            descripcion: 'Error al crear las facturas desde los documentos'
        },
        'GenerarFacturaDesdeDocs': {
            nombre: 'Timbrado de Factura',
            icono: '✉️',
            color: '#f59e0b',
            descripcion: 'Error al timbrar la factura con el PAC (SAT)'
        },
        'Validacion': {
            nombre: 'Validación de Datos',
            icono: '🔍',
            color: '#0ea5e9',
            descripcion: 'Los datos capturados no pasaron las validaciones del sistema'
        },
        'GuardarDocumento': {
            nombre: 'Guardar Documento Base',
            icono: '📄',
            color: '#3b82f6',
            descripcion: 'Error al crear el documento y su folio en el sistema'
        },
        'GuardarFactura': {
            nombre: 'Guardar Factura',
            icono: '📄',
            color: '#3b82f6',
            descripcion: 'Error al registrar el encabezado y las partidas de la factura'
        },
        'PrepararDatos': {
            nombre: 'Preparar Datos del CFDI',
            icono: '🧩',
            color: '#8b5cf6',
            descripcion: 'Error al armar la información que se envía al PAC'
        },
        'Timbrado': {
            nombre: 'Timbrado de Factura',
            icono: '✉️',
            color: '#f59e0b',
            descripcion: 'Error al timbrar la factura con el PAC (SAT)'
        },
        'TimbradoNC': {
            nombre: 'Timbrado de Nota de Crédito',
            icono: '🧾',
            color: '#ec4899',
            descripcion: 'Error al timbrar la nota de crédito del anticipo'
        },
        'Commit': {
            nombre: 'Guardado Final',
            icono: '💾',
            color: '#dc2626',
            descripcion: 'El CFDI se timbró en el SAT pero no se pudo guardar en el sistema'
        }
    };

    // Pasos en los que el CFDI YA existe en el SAT: no hubo reversión limpia
    // y el comprobante debe conciliarse a mano.
    const PASOS_CFDI_TIMBRADO = ['Commit', 'TimbradoNC'];
    const cfdiYaTimbrado = PASOS_CFDI_TIMBRADO.includes(result.step);

    const pasoInfo = pasosInfo[result.step] || {
        nombre: result.step || 'Paso desconocido',
        icono: '⚠️',
        color: '#dc2626',
        descripcion: 'Error en el proceso'
    };

    // Extraer el mensaje de error completo
    const errorCompleto = result.error || result.message || 'Error desconocido';

    // Intentar extraer el código de error del SAT si existe
    let codigoSAT = '';
    let mensajeSAT = '';
    let detallesTecnicos = errorCompleto;

    const matchCFDI = errorCompleto.match(/CFDI\d+/);
    if (matchCFDI) {
        codigoSAT = matchCFDI[0];
    }

    // Extraer el mensaje principal del SAT
    const matchMensaje = errorCompleto.match(/CFDI\d+\s*-\s*([^-]+)/);
    if (matchMensaje) {
        mensajeSAT = matchMensaje[1].trim();
    }

    Swal.fire({
        title: '❌ Error en el Proceso de Facturación',
        html: `
        <div style="text-align: left; padding: 1rem;">
            <!-- Header del Error -->
            <div style="
                background: linear-gradient(135deg, #fee2e2 0%, #fecaca 100%);
                padding: 1.5rem;
                border-radius: 12px;
                margin-bottom: 1.5rem;
                border-left: 4px solid ${pasoInfo.color};
            ">
                <div style="
                    font-size: 2rem;
                    text-align: center;
                    margin-bottom: 0.75rem;
                ">
                    ${pasoInfo.icono}
                </div>
                <div style="
                    font-size: 1.2rem;
                    font-weight: 700;
                    color: #dc2626;
                    margin-bottom: 0.5rem;
                    text-align: center;
                ">
                    Error en: ${pasoInfo.nombre}
                </div>
                <div style="
                    color: #991b1b;
                    font-size: 0.9rem;
                    text-align: center;
                    font-style: italic;
                ">
                    ${pasoInfo.descripcion}
                </div>
            </div>

            ${codigoSAT ? `
            <!-- Código de Error SAT -->
            <div style="
                background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border-left: 4px solid #f59e0b;
                text-align: center;
            ">
                <div style="
                    font-size: 1.5rem;
                    font-weight: 800;
                    color: #92400e;
                    margin-bottom: 0.5rem;
                    font-family: 'Courier New', monospace;
                ">
                    ${codigoSAT}
                </div>
                <div style="
                    color: #78350f;
                    font-size: 0.85rem;
                    font-weight: 600;
                ">
                    Código de error del SAT
                </div>
            </div>
            ` : ''}

            ${mensajeSAT ? `
            <!-- Mensaje Principal del SAT -->
            <div style="
                background: #fff1f2;
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border: 2px solid #fecdd3;
            ">
                <div style="
                    display: flex;
                    align-items: start;
                    gap: 0.75rem;
                ">
                    <i class="fas fa-times-circle" style="
                        color: #dc2626;
                        font-size: 1.5rem;
                        margin-top: 0.125rem;
                    "></i>
                    <div style="flex: 1;">
                        <strong style="
                            color: #991b1b;
                            display: block;
                            margin-bottom: 0.75rem;
                            font-size: 1rem;
                        ">
                            Descripción del error:
                        </strong>
                        <div style="
                            color: #7f1d1d;
                            font-size: 1.05rem;
                            line-height: 1.6;
                            background: white;
                            padding: 1rem;
                            border-radius: 8px;
                            font-weight: 600;
                        ">
                            ${mensajeSAT}
                        </div>
                    </div>
                </div>
            </div>
            ` : ''}

            <!-- Detalles Completos del Error -->
            <div style="
                background: #f9fafb;
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border: 1px solid #e5e7eb;
            ">
                <div style="
                    display: flex;
                    align-items: center;
                    gap: 0.5rem;
                    margin-bottom: 0.75rem;
                ">
                    <i class="fas fa-info-circle" style="color: #6b7280; font-size: 1.1rem;"></i>
                    <strong style="color: #374151; font-size: 0.95rem;">
                        Detalles completos del error:
                    </strong>
                </div>
                <div style="
                    color: #4b5563;
                    font-size: 0.9rem;
                    line-height: 1.8;
                    background: #1f2937;
                    color: #f3f4f6;
                    padding: 1rem;
                    border-radius: 8px;
                    font-family: 'Courier New', monospace;
                    max-height: 200px;
                    overflow-y: auto;
                    white-space: pre-wrap;
                    word-break: break-word;
                ">
${detallesTecnicos}
                </div>
            </div>

            ${cfdiYaTimbrado ? `
            <!-- CFDI timbrado sin guardar: requiere conciliación manual -->
            <div style="
                background: linear-gradient(135deg, #fee2e2 0%, #fecaca 100%);
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border-left: 3px solid #dc2626;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-exclamation-triangle" style="color: #dc2626; font-size: 1.25rem;"></i>
                    <h4 style="color: #7f1d1d; margin: 0; font-size: 1rem;">
                        Atención: el CFDI ya existe en el SAT
                    </h4>
                </div>
                <p style="color: #991b1b; font-size: 0.9rem; margin: 0; line-height: 1.6;">
                    El comprobante <strong>sí fue timbrado</strong>, pero no se pudo guardar en el sistema.
                    <strong>No intente volver a facturar</strong>: se duplicaría el CFDI.
                    Contacte a soporte con el UUID que aparece en el mensaje de error.
                </p>
            </div>
            ` : `
            <!-- Información de Rollback -->
            <div style="
                background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
                border-left: 3px solid #f59e0b;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-undo" style="color: #d97706; font-size: 1.25rem;"></i>
                    <h4 style="color: #92400e; margin: 0; font-size: 1rem;">
                        Reversión Automática
                    </h4>
                </div>
                <p style="color: #78350f; font-size: 0.9rem; margin: 0; line-height: 1.6;">
                    Los registros creados antes del error fueron <strong>revertidos automáticamente</strong>
                    (incluido el consecutivo del folio). No se generaron documentos incompletos.
                </p>
            </div>
            `}

            <!-- Documentos Afectados -->
            <div style="
                background: #f3f4f6;
                padding: 1.25rem;
                border-radius: 10px;
                margin-bottom: 1rem;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-file-invoice" style="color: #6b7280; font-size: 1.25rem;"></i>
                    <h4 style="color: #374151; margin: 0; font-size: 1rem;">
                        Documentos Afectados
                    </h4>
                </div>
                <div style="
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    background: white;
                    padding: 1rem;
                    border-radius: 8px;
                ">
                    <span style="color: #6b7280; font-size: 0.95rem;">
                        Total de documentos no procesados:
                    </span>
                    <span style="
                        color: #dc2626;
                        font-size: 1.5rem;
                        font-weight: 700;
                    ">
                        ${totalDocumentos}
                    </span>
                </div>
            </div>

            <!-- Recomendaciones -->
            <div style="
                background: #dbeafe;
                padding: 1.25rem;
                border-radius: 10px;
                border-left: 3px solid #3b82f6;
            ">
                <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem;">
                    <i class="fas fa-lightbulb" style="color: #2563eb; font-size: 1.25rem;"></i>
                    <h4 style="color: #1e3a8a; margin: 0; font-size: 1rem;">
                        ¿Qué hacer ahora?
                    </h4>
                </div>
                <ul style="
                    color: #1e40af;
                    font-size: 0.9rem;
                    margin: 0;
                    padding-left: 1.5rem;
                    line-height: 1.8;
                ">
                    <li>Revise el mensaje de error para identificar el problema específico</li>
                    <li>Verifique que los datos fiscales del receptor sean correctos</li>
                    ${codigoSAT === 'CFDI40159' ? '<li style="font-weight: 600; color: #dc2626;">Verifique que el Régimen Fiscal del receptor coincida con su tipo de RFC</li>' : ''}
                    <li>Corrija los datos necesarios e intente procesar nuevamente</li>
                    <li>Si el error persiste, contacte al administrador del sistema</li>
                </ul>
            </div>
        </div>
    `,
        icon: 'error',
        confirmButtonText: '<i class="fas fa-check"></i> Entendido',
        confirmButtonColor: '#7c3aed',
        width: '750px',
        showCancelButton: true,
        cancelButtonText: '<i class="fas fa-copy"></i> Copiar error',
        cancelButtonColor: '#6b7280',
        customClass: {
            popup: 'animated-popup'
        },
        allowOutsideClick: false
    }).then((modalResult) => {
        if (modalResult.dismiss === Swal.DismissReason.cancel) {
            // Copiar error al portapapeles
            const textoError = `
=== ERROR EN FACTURACIÓN ===
Paso: ${pasoInfo.nombre} (${result.step})
${codigoSAT ? `Código SAT: ${codigoSAT}` : ''}
${mensajeSAT ? `Mensaje: ${mensajeSAT}` : ''}

Detalles completos:
${detallesTecnicos}

Timestamp: ${new Date().toISOString()}
Documentos afectados: ${totalDocumentos}
            `.trim();

            navigator.clipboard.writeText(textoError).then(() => {
                Swal.fire({
                    icon: 'success',
                    title: 'Error copiado',
                    text: 'El detalle del error ha sido copiado al portapapeles',
                    timer: 2000,
                    showConfirmButton: false,
                    toast: true,
                    position: 'top-end'
                });
            }).catch(() => {
                Swal.fire({
                    icon: 'error',
                    title: 'Error al copiar',
                    text: 'No se pudo copiar al portapapeles',
                    timer: 2000,
                    showConfirmButton: false,
                    toast: true,
                    position: 'top-end'
                });
            });
        }
    });
}

// ============================================
// CERRAR MODAL DE PROGRESO
// ============================================
function cerrarModalProgreso(modal) {
    if (modal) {
        modal.style.animation = 'fadeOut 0.3s ease-in-out';
        setTimeout(() => {
            modal.remove();
        }, 300);
    }
}

function guardarModalEnStack(modalData) {
    modalStack.push(modalData);
}

function volverModalAnterior() {
    if (modalStack.length > 0) {
        const modalAnterior = modalStack.pop();
        mostrarResumenExitoso(modalAnterior.result, modalAnterior.totalDocumentos, true);
    }
}

function limpiarStackModales() {
    modalStack = [];
}

// ============================================
// MODAL PARA ENVÍO POR CORREO (MEJORADO - MÚLTIPLES DESTINATARIOS)
// ============================================
function mostrarModalEnvioCorreo(detalles, resultOriginal, totalDocumentos) {
    const serieFolio = construirSerieFolio(
        obtenerPropiedadSegura(detalles, 'serie', ''),
        obtenerPropiedadSegura(detalles, 'folio', '')
    );
    const totalFormateado = formatearMoneda(obtenerPropiedadSegura(detalles, 'total'));

    // Obtener los correos del cliente desde detalles
    const correosCliente = detalles.CorreosCliente || detalles.correosCliente || [];

    // Preparar opciones para TomSelect
    const opcionesCorreos = correosCliente.map(c => ({
        email: c.correo || c.Correo,
        id: c.id || c.Id
    }));
    const isEn = detalles.isEn === true || detalles.IsEn === true;
    // Si no hay correos, crear array vacío para permitir agregar manualmente
    const hayCorreos = opcionesCorreos.length > 0;

    Swal.fire({
        title: '📧 Enviar Factura por Correo',
        html: `
            <div style="text-align: left; padding: 1rem;">
                <!-- Información del documento -->
                <div style="
                    background: linear-gradient(135deg, #ede9fe 0%, #ddd6fe 100%);
                    padding: 1.25rem;
                    border-radius: 12px;
                    margin-bottom: 1.5rem;
                    border-left: 4px solid #7c3aed;
                ">
                    <div style="font-size: 1.1rem; font-weight: 700; color: #5b21b6; margin-bottom: 0.5rem;">
                        ${serieFolio}
                    </div>
                    ${totalFormateado !== 'N/A' ? `
                    <div style="color: #6b21a8; font-size: 0.9rem;">
                        <strong>Total:</strong> ${totalFormateado}
                    </div>
                    ` : ''}
                </div>

                ${!hayCorreos ? `
                <!-- Alerta: No hay correos registrados -->
                <div style="
                    background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
                    padding: 1rem;
                    border-radius: 10px;
                    margin-bottom: 1.5rem;
                    border-left: 4px solid #f59e0b;
                    display: flex;
                    align-items: center;
                    gap: 0.75rem;
                ">
                    <i class="fas fa-exclamation-triangle" style="color: #d97706; font-size: 1.5rem;"></i>
                    <div>
                        <div style="color: #92400e; font-weight: 600; font-size: 0.95rem; margin-bottom: 0.25rem;">
                            No hay correos registrados
                        </div>
                        <div style="color: #78350f; font-size: 0.85rem;">
                            Puede agregar correos manualmente escribiéndolos abajo
                        </div>
                    </div>
                </div>
                ` : ''}

                <!-- Campo de correos con TomSelect -->
                <div style="margin-bottom: 1.5rem;">
                    <label style="
                        display: block;
                        color: #374151;
                        font-weight: 600;
                        margin-bottom: 0.5rem;
                        font-size: 0.95rem;
                    ">
                        <i class="fas fa-envelope"></i> Correos electrónicos *
                    </label>
                    <select id="emailDestino" multiple placeholder="Seleccione o escriba correos..."></select>
                    <small style="color: #6b7280; font-size: 0.8rem; display: block; margin-top: 0.5rem;">
                        <i class="fas fa-info-circle"></i> 
                        ${hayCorreos
                ? 'Seleccione los correos o escriba nuevos presionando Enter'
                : 'Escriba los correos y presione Enter para agregarlos'}
                    </small>
                </div>

                <!-- Opciones de archivos -->
                <div style="
                    background: #f8fafc;
                    padding: 1rem;
                    border-radius: 10px;
                    margin-bottom: 1rem;
                    border: 1px solid #e2e8f0;
                ">
                    <div style="color: #374151; font-weight: 600; margin-bottom: 0.75rem; font-size: 0.9rem;">
                        <i class="fas fa-paperclip"></i> Archivos adjuntos:
                    </div>
                    <label style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; cursor: pointer;">
                        <input type="checkbox" id="incluirPDF" checked style="width: 18px; height: 18px;">
                        <span style="color: #6b7280; font-size: 0.9rem;">
                            <i class="fas fa-file-pdf" style="color: #dc2626;"></i> PDF
                        </span>
                    </label>
                    <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer;">
                        <input type="checkbox" id="incluirXML" checked style="width: 18px; height: 18px;">
                        <span style="color: #6b7280; font-size: 0.9rem;">
                            <i class="fas fa-file-code" style="color: #059669;"></i> XML
                        </span>
                    </label>
                    <label style="display: flex; align-items: center; gap: 0.5rem; cursor: pointer; ${!isEn ? 'display:none!important' : ''}">
                        <input type="checkbox" id="incluirPDFEN" ${isEn ? 'checked' : ''} style="width: 18px; height: 18px;">
                        <span style="color: #6b7280; font-size: 0.9rem;">
                            <i class="fas fa-file-pdf" style="color: #1d4ed8;"></i> PDF
                            <span style="font-size:0.75rem; background:#dbeafe; color:#1e40af; padding:1px 5px; border-radius:3px; margin-left:3px;">EN</span>
                        </span>
                    </label>
                </div>

                <!-- Mensaje adicional -->
                <div style="margin-bottom: 1rem;">
                    <label style="
                        display: block;
                        color: #374151;
                        font-weight: 600;
                        margin-bottom: 0.5rem;
                        font-size: 0.95rem;
                    ">
                        <i class="fas fa-comment"></i> Mensaje adicional (opcional)
                    </label>
                    <textarea
                        id="mensajeAdicional"
                        rows="3"
                        placeholder="Agregue un mensaje personalizado..."
                        style="
                            width: 100%;
                            padding: 0.75rem;
                            border: 2px solid #e5e7eb;
                            border-radius: 8px;
                            font-size: 0.9rem;
                            resize: vertical;
                            font-family: inherit;
                        "
                    ></textarea>
                </div>
            </div>
        `,
        showCancelButton: true,
        showDenyButton: true,
        confirmButtonText: '<i class="fas fa-paper-plane"></i> Enviar',
        denyButtonText: '<i class="fas fa-arrow-left"></i> Volver',
        cancelButtonText: '<i class="fas fa-times"></i> Cancelar',
        confirmButtonColor: '#3b82f6',
        denyButtonColor: '#6b7280',
        cancelButtonColor: '#dc2626',
        width: '650px',
        focusConfirm: false,
        didOpen: () => {
            // Inicializar TomSelect después de que el modal esté visible
            inicializarTomSelectCorreos(opcionesCorreos);
        },
        preConfirm: () => {
            const tomSelectInstance = document.getElementById('emailDestino').tomselect;
            const emailsSeleccionados = tomSelectInstance.getValue();
            const incluirPDF = document.getElementById('incluirPDF').checked;
            const incluirXML = document.getElementById('incluirXML').checked;
            const incluirPDFEN = document.getElementById('incluirPDFEN')?.checked ?? false; // ← NUEVO
            const mensaje = document.getElementById('mensajeAdicional').value.trim();

            if (!emailsSeleccionados || emailsSeleccionados.length === 0) {
                Swal.showValidationMessage('Seleccione o agregue al menos un correo electrónico');
                return false;
            }

            if (!incluirPDF && !incluirXML && !incluirPDFEN) {
                Swal.showValidationMessage('Seleccione al menos un archivo para adjuntar');
                return false;
            }

            const emails = Array.isArray(emailsSeleccionados) ? emailsSeleccionados : [emailsSeleccionados];
            return { emails, incluirPDF, incluirXML, incluirPDFEN, mensaje }; // ← agrega incluirPDFEN
        }
    }).then((result) => {
        if (result.isConfirmed && result.value) {
            enviarFacturaPorCorreo(detalles, result.value, resultOriginal, totalDocumentos);
        } else if (result.isDenied) {
            modalStack.pop();
            mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
        } else if (result.isDismissed) {
            limpiarStackModales();
            setTimeout(() => {
                recargarVista();
            }, 500);
        }
    });
}
// ============================================
// INICIALIZAR TOMSELECT PARA CORREOS
// ============================================
// ============================================
// INICIALIZAR TOMSELECT PARA CORREOS
// ============================================
function inicializarTomSelectCorreos(opcionesCorreos) {
    const REGEX_EMAIL = '([a-z0-9!#$%&\'*+/=?^_`{|}~-]+(?:\\.[a-z0-9!#$%&\'*+/=?^_`{|}~-]+)*@' +
        '(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)';

    const tomSelectInstance = new TomSelect('#emailDestino', {
        persist: false,
        maxItems: null,
        valueField: 'email',
        labelField: 'email',
        searchField: ['email'],
        placeholder: 'Seleccione o escriba correos...',
        options: opcionesCorreos,
        render: {
            item: function (item, escape) {
                return '<div>' +
                    '<span class="email">' + escape(item.email) + '</span>' +
                    '</div>';
            },
            option: function (item, escape) {
                return '<div>' +
                    '<span class="label">' + escape(item.email) + '</span>' +
                    '</div>';
            }
        },
        createFilter: function (input) {
            const regexpA = new RegExp('^' + REGEX_EMAIL + '$', 'i');
            return regexpA.test(input);
        },
        create: function (input) {
            if ((new RegExp('^' + REGEX_EMAIL + '$', 'i')).test(input)) {
                return { email: input };
            }
            Swal.showValidationMessage('Formato de correo inválido: ' + input);
            return false;
        },
        plugins: ['remove_button'],
        onItemAdd: function () {
            this.setTextboxValue('');
            this.refreshOptions();
        }
    });

    // ✅ SELECCIONAR AUTOMÁTICAMENTE TODOS LOS CORREOS PRECARGADOS
    if (opcionesCorreos && opcionesCorreos.length > 0) {
        opcionesCorreos.forEach(opcion => {
            tomSelectInstance.addItem(opcion.email, true); // El 'true' evita que dispare eventos innecesarios
        });
    }
}
// ============================================
// ENVIAR FACTURA POR CORREO (MÚLTIPLES DESTINATARIOS)
// ============================================
async function enviarFacturaPorCorreo(detalles, opciones, resultOriginal, totalDocumentos) {
    const { emails, incluirPDF, incluirXML, incluirPDFEN, mensaje } = opciones;

    const emailsText = emails.join(', ');
    const cantidadEmails = emails.length;

    Swal.fire({
        title: 'Enviando correo...',
        html: `
            <div style="text-align: center; padding: 1rem;">
                <div style="
                    width: 60px;
                    height: 60px;
                    margin: 0 auto 1rem;
                    border: 4px solid #e5e7eb;
                    border-top-color: #3b82f6;
                    border-radius: 50%;
                    animation: spin 1s linear infinite;
                "></div>
                <p style="color: #6b7280;">
                    Enviando a <strong>${cantidadEmails}</strong> destinatario${cantidadEmails > 1 ? 's' : ''}
                </p>
                <p style="color: #9ca3af; font-size: 0.85rem; margin-top: 0.5rem;">
                    ${emailsText}
                </p>
            </div>
        `,
        showConfirmButton: false,
        allowOutsideClick: false,
    });

    try {
        const formData = new FormData();

        // Enviar emails como string separado por comas
        formData.append('emails', emails.join(','));
        formData.append('serie', obtenerPropiedadSegura(detalles, 'serie', ''));
        formData.append('folio', obtenerPropiedadSegura(detalles, 'folio', ''));
        formData.append('facturaId', obtenerPropiedadSegura(detalles, 'uuid', ''));
        formData.append('pdfUrl', obtenerPropiedadSegura(detalles, 'pdfUrl', ''));
        formData.append('xmlUrl', obtenerPropiedadSegura(detalles, 'xmlUrl', ''));
        formData.append('incluirPDF', incluirPDF);
        formData.append('incluirXML', incluirXML);
        formData.append('mensaje', mensaje);
        formData.append('razonSocial', obtenerPropiedadSegura(detalles, 'razonSocial', ''));
        formData.append('rfcCliente', obtenerPropiedadSegura(detalles, 'rfcCliente', ''));
        formData.append('total', obtenerPropiedadSegura(detalles, 'total', '0'));
        formData.append('asunto', "Factura");
        formData.append('incluirPDFEN', incluirPDFEN)

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        const response = await fetch('/FacturaConsulta/EnviarEmail', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) throw new Error(`Error HTTP: ${response.status}`);

        const result = await response.json();

        if (result.success) {
            Swal.fire({
                icon: 'success',
                title: '✅ Correo enviado',
                html: `
                    <div style="text-align: center; padding: 1rem;">
                        <p style="color: #374151; margin-bottom: 0.5rem;">
                            Factura enviada exitosamente a:
                        </p>
                        <div style="
                            background: #f0fdf4;
                            padding: 1rem;
                            border-radius: 8px;
                            margin-top: 1rem;
                            border-left: 3px solid #10b981;
                        ">
                            ${emails.map(email => `
                                <div style="
                                    color: #059669;
                                    font-weight: 600;
                                    font-size: 0.95rem;
                                    padding: 0.25rem 0;
                                ">
                                    <i class="fas fa-check-circle"></i> ${email}
                                </div>
                            `).join('')}
                        </div>
                        <p style="color: #6b7280; font-size: 0.85rem; margin-top: 1rem;">
                            Total: ${cantidadEmails} destinatario${cantidadEmails > 1 ? 's' : ''}
                        </p>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: '<i class="fas fa-check"></i> Finalizar',
                cancelButtonText: '<i class="fas fa-arrow-left"></i> Volver al resumen',
                confirmButtonColor: '#059669',
                cancelButtonColor: '#6b7280'
            }).then((finalResult) => {
                if (finalResult.isDismissed || finalResult.isConfirmed) {
                    if (finalResult.isDismissed) {
                        // Volver al modal de resumen
                        modalStack.pop();
                        mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
                    } else {
                        limpiarStackModales();
                        recargarVista();
                    }
                }
            });
        } else {
            throw new Error(result.message || 'Error al enviar');
        }

    } catch (error) {
        Swal.fire({
            icon: 'error',
            title: 'Error al enviar',
            text: error.message,
            showCancelButton: true,
            confirmButtonText: 'Reintentar',
            cancelButtonText: 'Volver',
            confirmButtonColor: '#3b82f6'
        }).then((retryResult) => {
            if (retryResult.isConfirmed) {
                mostrarModalEnvioCorreo(detalles, resultOriginal, totalDocumentos);
            } else {
                modalStack.pop();
                mostrarResumenExitoso(resultOriginal, totalDocumentos, true);
            }
        });
    }
}
