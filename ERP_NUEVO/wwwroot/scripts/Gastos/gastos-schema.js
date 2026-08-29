/**
 * gastos-schema.js
 * ─────────────────────────────────────────────────────────────────────────────
 * CAPA DE DATOS PURA — sin referencias al DOM.
 * Para agregar una nueva categoría: añade una entrada al objeto SCHEMA.
 * Para agregar una subcategoría: añade una entrada al array `subs` de su categoría.
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * TIPOS DE CAMPO SOPORTADOS:
 *   text        → <input type="text">
 *   number      → <input type="number">
 *   date        → <input type="date">
 *   currency    → <input type="number"> con prefijo $ y reglas de monto
 *   select      → <select> con lista de opciones
 *   radio-grid  → grid de tarjetas seleccionables (una sola opción)
 *   textarea    → <textarea>
 *   person      → <input type="text"> con chip de avatar (nombre de persona)
 *   email       → <input type="email">
 *   tag-input   → lista de valores separados por Enter (chips)
 *   date-range  → dos dates (desde / hasta) en una sola fila
 *   counter     → spinner +/- con valor mínimo
 *
 * REGLAS DE CAMPO (campo.rules):
 *   required    → boolean
 *   min / max   → para number / currency
 *   warn        → umbral de advertencia visual (no bloquea)
 *   pattern     → regex string para texto
 *   maxLength   → para text / textarea
 *
 * VISIBILIDAD CONDICIONAL (campo.showIf):
 *   { field, op, value }
 *   ops: '==', '!=', '>', '<', 'includes'
 *
 * REGLAS DE NEGOCIO POR SUBCATEGORÍA (sub.businessRules):
 *   montoMax        → monto máximo permitido (bloquea)
 *   montoWarn       → umbral de advertencia (muestra aviso, no bloquea)
 *   aprobadorSugerido(monto) → función que devuelve string del aprobador
 *   alertas         → array de { condition(fields, monto), message, level:'warn'|'error' }
 *
 * ─────────────────────────────────────────────────────────────────────────────
 */
let tomManager;
tomManager = new TomSelectManager();

const SCHEMA = {

    /* ══════════════════════════════════════════════════════════════════════════
       OPERATIVOS
    ══════════════════════════════════════════════════════════════════════════ */
    operativos: {
        label: 'Gastos Operativos',
        color: '#1d4ed8',
        icon: 'fa-solid fa-gear',
        desc: 'Costos ligados a la operación diaria del negocio.',
        subs: [

            /* ── Mantenimiento ── */
            {
                id: 'mant',
                name: 'Mantenimiento y reparaciones',
                desc: 'Servicios preventivos o correctivos de equipos e instalaciones.',
                reqs: ['Cotización o factura', 'Orden de trabajo', 'Evidencia fotográfica'],
                businessRules: {
                    montoMax: 250_000,
                    montoWarn: 80_000,
                    aprobadorSugerido: m => m >= 80_000 ? 'Director de Finanzas' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => f['ef-tipo-mant'] === 'Correctivo' && !f['ef-falla-num'],
                            message: 'Mantenimiento correctivo requiere número de reporte de falla.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-activo', label: 'Activo a mantener', type: 'text',
                        placeholder: 'Ej: Servidor rack 01 / Compresor línea B',
                        rules: { required: true, maxLength: 100 }
                    },
                    {
                        id: 'ef-tipo-mant', label: 'Tipo de mantenimiento', type: 'radio-grid',
                        options: [
                            { value: 'Preventivo', icon: 'fa-solid fa-shield-check', label: 'Preventivo', hint: 'Programado, sin falla activa' },
                            { value: 'Correctivo', icon: 'fa-solid fa-wrench', label: 'Correctivo', hint: 'Falla en curso o reciente' },
                            { value: 'Predictivo', icon: 'fa-solid fa-chart-line', label: 'Predictivo', hint: 'Basado en análisis de condición' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-falla-num', label: 'Número de reporte de falla', type: 'text',
                        placeholder: 'REP-2025-XXXX',
                        showIf: { field: 'ef-tipo-mant', op: '==', value: 'Correctivo' }
                    },
                    {
                        id: 'ef-programa', label: 'Número de programa / OT', type: 'text',
                        placeholder: 'OT-2025-XXXX',
                        showIf: { field: 'ef-tipo-mant', op: '==', value: 'Preventivo' }
                    },
                    {
                        id: 'ef-proveedor-tec', label: 'Proveedor técnico', type: 'text',
                        placeholder: 'Empresa o técnico que realiza el servicio'
                    },
                    {
                        id: 'ef-tiempo-paro', label: 'Tiempo de paro estimado (hrs)', type: 'counter',
                        rules: { min: 0, max: 720 }, defaultValue: 0,
                        showIf: { field: 'ef-tipo-mant', op: '==', value: 'Correctivo' }
                    },
                ],
            },

            /* ── Insumos ── */
            {
                id: 'insumos',
                name: 'Insumos y materiales',
                desc: 'Materiales de consumo para la operación diaria.',
                reqs: ['Factura o nota de venta', 'Lista de materiales'],
                businessRules: {
                    montoMax: 50_000,
                    montoWarn: 15_000,
                    aprobadorSugerido: m => m >= 15_000 ? 'Gerente de Área' : 'Coordinador de Compras',
                },
                fields: [
                    {
                        id: 'ef-almacen', label: 'Almacén destino', type: 'select',
                        options: ['Almacén Central', 'Almacén Producción A', 'Almacén Producción B', 'Almacén Mantenimiento', 'Oficinas'],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-items', label: 'Artículos solicitados', type: 'textarea',
                        placeholder: 'Línea por artículo: cantidad · unidad · descripción\nEj: 10 pzas · Tornillo M8 · Galvanizado',
                        rules: { required: true, maxLength: 2000 }
                    },
                    {
                        id: 'ef-urgencia-insumo', label: '¿Es compra urgente?', type: 'radio-grid',
                        options: [
                            { value: 'no', icon: 'fa-solid fa-clock', label: 'No urgente', hint: 'Puede esperar ciclo normal' },
                            { value: 'si', icon: 'fa-solid fa-triangle-exclamation', label: 'Urgente', hint: 'Paro de línea / entrega crítica' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-proveedor-ref', label: 'Proveedor sugerido', type: 'text',
                        placeholder: 'Opcional: proveedor que ya ha cotizado'
                    },
                ],
            },

            /* ── Servicios externos ── */
            {
                id: 'servicios',
                name: 'Servicios externos',
                desc: 'Proveedores externos: seguridad, limpieza, mensajería.',
                reqs: ['Contrato o cotización', 'Factura del servicio'],
                businessRules: {
                    montoMax: 120_000,
                    montoWarn: 40_000,
                    aprobadorSugerido: m => m >= 40_000 ? 'Director de Finanzas' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => f['ef-serv-periodicidad'] === 'Mensual recurrente' && !f['ef-contrato-num'],
                            message: 'Servicios recurrentes deben tener número de contrato vigente.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-serv-tipo', label: 'Tipo de servicio', type: 'select',
                        options: ['Limpieza', 'Seguridad / Vigilancia', 'Mensajería / Paquetería', 'Jardinería', 'Plomería', 'Electricidad', 'Otro'],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-serv-otro', label: 'Especifica el servicio', type: 'text',
                        placeholder: 'Describir tipo de servicio',
                        showIf: { field: 'ef-serv-tipo', op: '==', value: 'Otro' }
                    },
                    {
                        id: 'ef-serv-periodicidad', label: 'Periodicidad', type: 'select',
                        options: ['Única vez', 'Mensual recurrente', 'Trimestral', 'Anual'],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-contrato-num', label: 'Número de contrato', type: 'text',
                        placeholder: 'CONT-2025-XXXX (si aplica)'
                    },
                    { id: 'ef-fecha-servicio', label: 'Fecha programada del servicio', type: 'date' },
                ],
            },

            /* ── Transporte ── */
            {
                id: 'transporte',
                name: 'Transporte y logística',
                desc: 'Fletes, paquetería y transporte de mercancías.',
                reqs: ['Guía o comprobante', 'Factura del transportista'],
                businessRules: {
                    montoMax: 200_000,
                    montoWarn: 60_000,
                    aprobadorSugerido: m => m >= 60_000 ? 'VP de Operaciones' : 'Gerente de Área',
                },
                fields: [
                    { id: 'ef-origen', label: 'Ciudad origen', type: 'text', placeholder: 'Querétaro, QRO', rules: { required: true } },
                    { id: 'ef-destino', label: 'Ciudad destino', type: 'text', placeholder: 'CDMX', rules: { required: true } },
                    {
                        id: 'ef-tipo-flete', label: 'Tipo de flete', type: 'radio-grid',
                        options: [
                            { value: 'Terrestre', icon: 'fa-solid fa-truck', label: 'Terrestre', hint: 'Camión, van, camioneta' },
                            { value: 'Aéreo', icon: 'fa-solid fa-plane', label: 'Aéreo', hint: 'Carga aérea urgente' },
                            { value: 'Paquetería', icon: 'fa-solid fa-box', label: 'Paquetería', hint: 'FedEx, DHL, Estafeta' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-mercancias', label: 'Descripción de mercancías', type: 'textarea',
                        placeholder: 'Tipo, cantidad y peso aproximado de lo enviado',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-peso-kg', label: 'Peso total (kg)', type: 'number',
                        placeholder: '0', rules: { min: 0 }
                    },
                ],
            },

            /* ── Infraestructura TI ── */
            {
                id: 'it',
                name: 'Infraestructura TI',
                desc: 'Hardware, software, licencias y servicios en la nube.',
                reqs: ['Cotización técnica', 'Factura', 'Alta de activo TI'],
                businessRules: {
                    montoMax: 500_000,
                    montoWarn: 100_000,
                    aprobadorSugerido: m => m >= 100_000 ? 'Director General' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => f['ef-tipo-it'] === 'Software / Licencia' && !f['ef-n-usuarios'],
                            message: 'Indica el número de usuarios para la licencia.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-tipo-it', label: 'Tipo de adquisición', type: 'radio-grid',
                        options: [
                            { value: 'Hardware', icon: 'fa-solid fa-server', label: 'Hardware', hint: 'Equipos físicos' },
                            { value: 'Software / Licencia', icon: 'fa-solid fa-key', label: 'Licencia', hint: 'Software o suscripción' },
                            { value: 'Servicio cloud', icon: 'fa-solid fa-cloud', label: 'Cloud', hint: 'AWS, Azure, GCP…' },
                            { value: 'Soporte técnico', icon: 'fa-solid fa-headset', label: 'Soporte', hint: 'Servicio de asistencia' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-item-it', label: 'Descripción del ítem', type: 'text',
                        placeholder: 'Ej: Licencia Microsoft 365 Business x10', rules: { required: true }
                    },
                    {
                        id: 'ef-n-usuarios', label: 'Número de usuarios/sedes', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1,
                        showIf: { field: 'ef-tipo-it', op: '==', value: 'Software / Licencia' }
                    },
                    {
                        id: 'ef-vigencia-lic', label: 'Vigencia de la licencia', type: 'select',
                        options: ['Mensual', 'Anual', 'Perpetua', 'Por evento'],
                        showIf: { field: 'ef-tipo-it', op: '==', value: 'Software / Licencia' }
                    },
                    {
                        id: 'ef-it-serial', label: 'Número de serie / SKU', type: 'text',
                        placeholder: 'Identificador del fabricante',
                        showIf: { field: 'ef-tipo-it', op: '==', value: 'Hardware' }
                    },
                    {
                        id: 'ef-it-area', label: 'Área / usuario destino', type: 'text',
                        placeholder: 'Ej: Almacén / Juan Pérez'
                    },
                ],
            },
        ],
    },

    /* ══════════════════════════════════════════════════════════════════════════
       ADMINISTRATIVOS
    ══════════════════════════════════════════════════════════════════════════ */
    administrativos: {
        label: 'Gastos Administrativos',
        color: '#7c3aed',
        icon: 'fa-solid fa-briefcase',
        desc: 'Erogaciones para el funcionamiento de áreas de soporte.',
        subs: [

            /* ── Papelería ── */
            {
                id: 'papeleria',
                name: 'Papelería y útiles',
                desc: 'Material de escritura, impresión y artículos de oficina.',
                reqs: ['Factura o ticket', 'Lista de materiales'],
                businessRules: {
                    montoMax: 8_000,
                    montoWarn: 3_000,
                    aprobadorSugerido: () => 'Coordinador Administrativo',
                },
                fields: [
                    {
                        id: 'ef-area-pap', label: 'Área solicitante', type: 'select',
                        options: ['Dirección General', 'Finanzas', 'RH', 'Ventas', 'Operaciones', 'TI', 'Legal'],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-pap', label: 'Artículos requeridos', type: 'textarea',
                        placeholder: '5 pzas · Bolígrafo azul\n2 paq · Hojas carta 500 uds',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-pap-urgente', label: 'Compra urgente', type: 'radio-grid',
                        options: [
                            { value: 'No', icon: 'fa-regular fa-clock', label: 'No', hint: 'Reposición normal' },
                            { value: 'Si', icon: 'fa-solid fa-bolt', label: 'Sí', hint: 'Se requiere hoy' },
                        ]
                    },
                ],
            },

            /* ── Legal ── */
            {
                id: 'legal',
                name: 'Servicios legales y notariales',
                desc: 'Honorarios de abogados, notarios y gestores.',
                reqs: ['Convenio u orden de servicio', 'Factura del despacho'],
                businessRules: {
                    montoMax: 300_000,
                    montoWarn: 80_000,
                    aprobadorSugerido: m => m >= 80_000 ? 'Director General' : 'Director de Finanzas',
                },
                fields: [
                    {
                        id: 'ef-legal-tipo', label: 'Tipo de servicio legal', type: 'radio-grid',
                        options: [
                            { value: 'Asesoría', icon: 'fa-solid fa-scale-balanced', label: 'Asesoría', hint: 'Consulta o dictamen' },
                            { value: 'Contrato / Escrituración', icon: 'fa-solid fa-file-signature', label: 'Escrituración', hint: 'Contrato o acto notarial' },
                            { value: 'Trámite', icon: 'fa-solid fa-stamp', label: 'Trámite', hint: 'Gestión ante autoridad' },
                            { value: 'Litigio', icon: 'fa-solid fa-gavel', label: 'Litigio', hint: 'Representación en juicio' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-legal-asunto', label: 'Descripción del asunto', type: 'textarea',
                        placeholder: 'Describe el asunto legal brevemente',
                        rules: { required: true, maxLength: 500 }
                    },
                    {
                        id: 'ef-legal-despacho', label: 'Nombre del despacho / notaría', type: 'text',
                        placeholder: 'Ej: Despacho García & Asociados', rules: { required: true }
                    },
                    {
                        id: 'ef-legal-exp', label: 'Número de expediente', type: 'text',
                        placeholder: 'EXP-2025-XXXX (si ya existe)',
                        showIf: { field: 'ef-legal-tipo', op: '==', value: 'Litigio' }
                    },
                ],
            },

            /* ── Capacitación ── */
            {
                id: 'capacitacion',
                name: 'Capacitación y desarrollo',
                desc: 'Cursos, talleres y certificaciones.',
                reqs: ['Programa del curso', 'Factura del proveedor'],
                businessRules: {
                    montoMax: 80_000,
                    montoWarn: 25_000,
                    aprobadorSugerido: m => m >= 25_000 ? 'Director de RH' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => parseInt(f['ef-cap-n'] || 0, 10) > 10,
                            message: 'Más de 10 participantes requiere aprobación de RH adicional.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-cap-nombre', label: 'Nombre del curso / taller', type: 'text',
                        placeholder: 'Diplomado en Finanzas Corporativas', rules: { required: true }
                    },
                    {
                        id: 'ef-cap-proveedor', label: 'Proveedor / institución', type: 'text',
                        placeholder: 'ITESM, Instituto X, plataforma online…', rules: { required: true }
                    },
                    {
                        id: 'ef-cap-modalidad', label: 'Modalidad', type: 'radio-grid',
                        options: [
                            { value: 'Presencial', icon: 'fa-solid fa-users', label: 'Presencial', hint: 'Asistencia física' },
                            { value: 'En línea', icon: 'fa-solid fa-laptop', label: 'En línea', hint: 'Plataforma virtual' },
                            { value: 'Híbrido', icon: 'fa-solid fa-arrows-split-up-and-left', label: 'Híbrido', hint: 'Mixto' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-cap-fechas', label: 'Fechas del curso', type: 'date-range',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-cap-n', label: 'Número de participantes', type: 'counter',
                        rules: { min: 1, max: 200 }, defaultValue: 1
                    },
                    {
                        id: 'ef-cap-participantes', label: 'Participantes (nombres)', type: 'tag-input',
                        placeholder: 'Escribe un nombre y presiona Enter'
                    },
                    {
                        id: 'ef-cap-lugar', label: 'Lugar (si es presencial)', type: 'text',
                        placeholder: 'Ciudad, instalación',
                        showIf: { field: 'ef-cap-modalidad', op: '!=', value: 'En línea' }
                    },
                ],
            },

            /* ── Renta ── */
            {
                id: 'renta',
                name: 'Renta de espacios',
                desc: 'Arrendamiento de oficinas, bodegas o salas.',
                reqs: ['Contrato de arrendamiento', 'Factura mensual'],
                businessRules: {
                    montoMax: 500_000,
                    montoWarn: 120_000,
                    aprobadorSugerido: m => m >= 120_000 ? 'Director General' : 'Director de Finanzas',
                },
                fields: [
                    {
                        id: 'ef-rent-tipo', label: 'Tipo de espacio', type: 'radio-grid',
                        options: [
                            { value: 'Oficina', icon: 'fa-solid fa-building', label: 'Oficina', hint: '' },
                            { value: 'Bodega', icon: 'fa-solid fa-warehouse', label: 'Bodega', hint: '' },
                            { value: 'Sala de juntas', icon: 'fa-solid fa-chalkboard-user', label: 'Sala', hint: 'Solo uso eventual' },
                            { value: 'Coworking', icon: 'fa-solid fa-people-roof', label: 'Coworking', hint: 'Espacio compartido' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-rent-ubic', label: 'Dirección / ubicación', type: 'text',
                        placeholder: 'Calle, colonia, ciudad, CP', rules: { required: true }
                    },
                    {
                        id: 'ef-rent-m2', label: 'Superficie (m²)', type: 'number',
                        placeholder: '0', rules: { min: 0 }
                    },
                    {
                        id: 'ef-rent-contrato', label: 'Número de contrato', type: 'text',
                        placeholder: 'ARR-2025-XXXX'
                    },
                    { id: 'ef-rent-vigencia', label: 'Vigencia del contrato', type: 'date-range' },
                ],
            },

            /* ── Seguros ── */
            {
                id: 'seguros',
                name: 'Seguros y pólizas',
                desc: 'Pago de primas de seguros.',
                reqs: ['Carátula de póliza', 'Factura o recibo de prima'],
                businessRules: {
                    montoMax: 400_000,
                    montoWarn: 80_000,
                    aprobadorSugerido: m => m >= 80_000 ? 'Director de Finanzas' : 'Gerente de Área',
                },
                fields: [
                    {
                        id: 'ef-seg-tipo', label: 'Tipo de seguro', type: 'radio-grid',
                        options: [
                            { value: 'Activos fijos', icon: 'fa-solid fa-industry', label: 'Activos', hint: 'Maquinaria, instalaciones' },
                            { value: 'Flotilla vehicular', icon: 'fa-solid fa-car', label: 'Flotilla', hint: 'Vehículos de la empresa' },
                            { value: 'Responsabilidad civil', icon: 'fa-solid fa-shield', label: 'RC', hint: 'Daños a terceros' },
                            { value: 'Vida / GMM colaboradores', icon: 'fa-solid fa-heart-pulse', label: 'Vida / GMM', hint: 'Personal asegurado' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-seg-poliza', label: 'Número de póliza', type: 'text',
                        placeholder: 'POL-2025-XXXX', rules: { required: true }
                    },
                    {
                        id: 'ef-seg-aseguradora', label: 'Aseguradora', type: 'text',
                        placeholder: 'GNP, AXA, Mapfre…', rules: { required: true }
                    },
                    {
                        id: 'ef-seg-vig', label: 'Vigencia de la póliza', type: 'date-range',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-seg-n-aseg', label: 'Número de asegurados', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1,
                        showIf: { field: 'ef-seg-tipo', op: '==', value: 'Vida / GMM colaboradores' }
                    },
                ],
            },
        ],
    },

    /* ══════════════════════════════════════════════════════════════════════════
       VIÁTICOS
    ══════════════════════════════════════════════════════════════════════════ */
    viaticos: {
        label: 'Viáticos',
        color: '#d97706',
        icon: 'fa-solid fa-plane',
        desc: 'Desplazamiento, hospedaje y alimentación en viajes.',
        subs: [

            /* ── Hospedaje ── */
            {
                id: 'hospedaje',
                name: 'Hospedaje',
                desc: 'Hotel o alojamiento temporal durante viajes.',
                reqs: ['Factura del hotel', 'Folio de reservación'],
                businessRules: {
                    montoWarn: 5_000,    // por noche × noches
                    montoMax: 80_000,
                    aprobadorSugerido: m => m >= 10_000 ? 'Director de Finanzas' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => {
                                const ci = f['ef-checkin'], co = f['ef-checkout'];
                                if (!ci || !co) return false;
                                const noches = (new Date(co) - new Date(ci)) / 86_400_000;
                                return noches > 30;
                            },
                            message: 'Estancias mayores a 30 noches requieren autorización de Dirección General.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-hotel', label: 'Nombre del hotel', type: 'text',
                        placeholder: 'Marriott Querétaro', rules: { required: true }
                    },
                    {
                        id: 'ef-hotel-ciudad', label: 'Ciudad', type: 'text',
                        placeholder: 'Ciudad del alojamiento', rules: { required: true }
                    },
                    {
                        id: 'ef-checkin', label: 'Check-in', type: 'date',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-checkout', label: 'Check-out', type: 'date',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-noches', label: 'Noches', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                    {
                        id: 'ef-tarifa-noche', label: 'Tarifa por noche ($)', type: 'number',
                        placeholder: '0.00', rules: { min: 0 }
                    },
                    {
                        id: 'ef-motivo-viaje', label: 'Motivo del viaje', type: 'text',
                        placeholder: 'Reunión con cliente / capacitación…', rules: { required: true }
                    },
                ],
            },

            /* ── Transporte viático ── */
            {
                id: 'transporte-via',
                name: 'Transporte (vuelos / terrestre)',
                desc: 'Boletos de avión, autobús, tren o renta de vehículo.',
                reqs: ['Boleto o itinerario', 'Factura'],
                businessRules: {
                    montoMax: 60_000,
                    montoWarn: 15_000,
                    aprobadorSugerido: m => m >= 15_000 ? 'Director de Finanzas' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => f['ef-via-clase'] === 'Business / Primera',
                            message: 'Clase Business requiere aprobación previa de Dirección General.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-via-origen', label: 'Ciudad origen', type: 'text',
                        placeholder: 'Querétaro, QRO', rules: { required: true }
                    },
                    {
                        id: 'ef-via-destino', label: 'Ciudad destino', type: 'text',
                        placeholder: 'CDMX', rules: { required: true }
                    },
                    {
                        id: 'ef-via-tipo', label: 'Medio de transporte', type: 'radio-grid',
                        options: [
                            { value: 'Vuelo', icon: 'fa-solid fa-plane-departure', label: 'Vuelo', hint: '' },
                            { value: 'Autobús', icon: 'fa-solid fa-bus', label: 'Autobús', hint: '' },
                            { value: 'Renta de auto', icon: 'fa-solid fa-car-side', label: 'Renta auto', hint: '' },
                            { value: 'Tren', icon: 'fa-solid fa-train', label: 'Tren', hint: '' },
                            { value: 'Rideshare', icon: 'fa-solid fa-taxi', label: 'Rideshare', hint: 'Uber, DiDi…' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-via-clase', label: 'Clase de servicio', type: 'select',
                        options: ['Económica / Turista', 'Business / Primera'],
                        showIf: { field: 'ef-via-tipo', op: '==', value: 'Vuelo' },
                        rules: { required: true }
                    },
                    {
                        id: 'ef-via-ir', label: 'Tipo de viaje', type: 'radio-grid',
                        options: [
                            { value: 'Solo ida', icon: 'fa-solid fa-arrow-right', label: 'Solo ida', hint: '' },
                            { value: 'Ida y vuelta', icon: 'fa-solid fa-arrows-left-right', label: 'Ida y vuelta', hint: '' },
                        ],
                        showIf: { field: 'ef-via-tipo', op: '==', value: 'Vuelo' }
                    },
                    {
                        id: 'ef-via-viajeros', label: 'Número de viajeros', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                    {
                        id: 'ef-via-fecha', label: 'Fecha de salida', type: 'date',
                        rules: { required: true }
                    },
                ],
            },

            /* ── Alimentos ── */
            {
                id: 'alimentos',
                name: 'Alimentos y bebidas',
                desc: 'Gastos de alimentación durante comisiones.',
                reqs: ['Ticket o factura', 'Relación de comidas'],
                businessRules: {
                    montoMax: 15_000,
                    montoWarn: 5_000,
                    aprobadorSugerido: m => m >= 5_000 ? 'Gerente de Área' : 'Coordinador de RH',
                    alertas: [
                        {
                            condition: (f, monto) => {
                                const dias = parseInt(f['ef-ali-dias'] || 1, 10);
                                const personas = parseInt(f['ef-ali-personas'] || 1, 10);
                                if (!dias || !personas) return false;
                                return (monto / dias / personas) > 800;
                            },
                            message: 'El gasto por persona/día supera $800. Verifica o justifica el importe.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-ali-dest', label: 'Destino / ciudad', type: 'text',
                        placeholder: 'Monterrey, NL', rules: { required: true }
                    },
                    {
                        id: 'ef-ali-dias', label: 'Días de comisión', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                    {
                        id: 'ef-ali-personas', label: 'Personas', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                    {
                        id: 'ef-ali-tipo', label: 'Tipo de gasto', type: 'radio-grid',
                        options: [
                            { value: 'Comidas propias', icon: 'fa-solid fa-utensils', label: 'Comidas propias', hint: 'Solo el colaborador' },
                            { value: 'Comida con clientes', icon: 'fa-solid fa-handshake', label: 'Con clientes', hint: 'Atención de negocio' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-ali-clientes', label: 'Clientes o invitados', type: 'tag-input',
                        placeholder: 'Nombre del cliente, Enter para agregar',
                        showIf: { field: 'ef-ali-tipo', op: '==', value: 'Comida con clientes' }
                    },
                ],
            },

            /* ── Eventos ── */
            {
                id: 'eventos',
                name: 'Asistencia a eventos',
                desc: 'Inscripción en conferencias, ferias y exposiciones.',
                reqs: ['Convocatoria del evento', 'Factura de inscripción'],
                businessRules: {
                    montoMax: 40_000,
                    montoWarn: 12_000,
                    aprobadorSugerido: m => m >= 12_000 ? 'Director de Finanzas' : 'Gerente de Área',
                },
                fields: [
                    {
                        id: 'ef-evento', label: 'Nombre del evento', type: 'text',
                        placeholder: 'ExpoManufactura 2025', rules: { required: true }
                    },
                    {
                        id: 'ef-evento-tipo', label: 'Tipo de evento', type: 'radio-grid',
                        options: [
                            { value: 'Conferencia', icon: 'fa-solid fa-microphone', label: 'Conferencia', hint: '' },
                            { value: 'Feria / Expo', icon: 'fa-solid fa-store', label: 'Feria / Expo', hint: '' },
                            { value: 'Taller', icon: 'fa-solid fa-chalkboard', label: 'Taller', hint: '' },
                            { value: 'Cumbre', icon: 'fa-solid fa-people-group', label: 'Cumbre', hint: '' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-evento-lugar', label: 'Lugar y ciudad', type: 'text',
                        placeholder: 'Centro Banamex, Monterrey', rules: { required: true }
                    },
                    {
                        id: 'ef-evento-fechas', label: 'Fechas del evento', type: 'date-range',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-evento-rol', label: 'Rol de participación', type: 'radio-grid',
                        options: [
                            { value: 'Asistente', icon: 'fa-solid fa-eye', label: 'Asistente', hint: '' },
                            { value: 'Ponente', icon: 'fa-solid fa-person-chalkboard', label: 'Ponente', hint: '' },
                            { value: 'Representante', icon: 'fa-solid fa-building-flag', label: 'Representante', hint: '' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-evento-n', label: 'Número de asistentes', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                ],
            },

            /* ── Representación ── */
            {
                id: 'representacion',
                name: 'Gastos de representación',
                desc: 'Comidas de negocios y atención a clientes.',
                reqs: ['Factura a nombre de la empresa', 'Lista de asistentes'],
                businessRules: {
                    montoMax: 25_000,
                    montoWarn: 8_000,
                    aprobadorSugerido: m => m >= 8_000 ? 'Director General' : 'Director de Finanzas',
                    alertas: [
                        {
                            condition: (f, monto) => {
                                const asistentes = (f['ef-rep-asist'] || '').split('\n').filter(Boolean).length + 1;
                                return asistentes > 0 && (monto / asistentes) > 2000;
                            },
                            message: 'El gasto por asistente supera $2,000. Puede requerir justificación adicional.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-rep-lugar', label: 'Restaurante / lugar', type: 'text',
                        placeholder: 'Nombre del establecimiento', rules: { required: true }
                    },
                    {
                        id: 'ef-rep-fecha', label: 'Fecha del evento', type: 'date',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-rep-obj', label: 'Objetivo del encuentro', type: 'text',
                        placeholder: 'Negociación contrato anual / presentación de propuesta', rules: { required: true }
                    },
                    {
                        id: 'ef-rep-asist', label: 'Invitados externos (uno por línea)', type: 'tag-input',
                        placeholder: 'Nombre · Empresa · Cargo', rules: { required: true }
                    },
                    {
                        id: 'ef-rep-int', label: 'Colaboradores internos', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                ],
            },
        ],
    },

    /* ══════════════════════════════════════════════════════════════════════════
       DIRECTOS
    ══════════════════════════════════════════════════════════════════════════ */
    directos: {
        label: 'Gastos Directos',
        color: '#059669',
        icon: 'fa-solid fa-bolt',
        desc: 'Egresos vinculados directamente a proyectos o clientes.',
        subs: [

            /* ── Materiales de proyecto ── */
            {
                id: 'materiales-proy',
                name: 'Materiales de proyecto',
                desc: 'Materiales asignados a un proyecto o cliente.',
                reqs: ['Orden de compra', 'Factura', 'Entrada a almacén'],
                businessRules: {
                    montoMax: 600_000,
                    montoWarn: 150_000,
                    aprobadorSugerido: m => m >= 150_000 ? 'VP de Operaciones' : 'Gerente de Área',
                },
                fields: [
                    {
                        id: 'ef-proj-id', label: 'ID / nombre del proyecto', type: 'text',
                        placeholder: 'PRY-2025-045', rules: { required: true }
                    },
                    {
                        id: 'ef-proj-cliente', label: 'Cliente del proyecto', type: 'text',
                        placeholder: 'Empresa ABC, S.A.'
                    },
                    {
                        id: 'ef-proj-mat', label: 'Lista de materiales', type: 'textarea',
                        placeholder: 'Cantidad · unidad · material\n10 pzas · Viga IPR 4"',
                        rules: { required: true, maxLength: 3000 }
                    },
                    {
                        id: 'ef-proj-oc', label: 'Número de orden de compra', type: 'text',
                        placeholder: 'OC-2025-XXXX', rules: { required: true }
                    },
                    {
                        id: 'ef-proj-entrega', label: 'Fecha requerida de entrega', type: 'date',
                        rules: { required: true }
                    },
                ],
            },

            /* ── Subcontrato ── */
            {
                id: 'subcontrato',
                name: 'Subcontratación de mano de obra',
                desc: 'Personal externo para un proyecto.',
                reqs: ['Contrato o pedido', 'Factura'],
                businessRules: {
                    montoMax: 400_000,
                    montoWarn: 100_000,
                    aprobadorSugerido: m => m >= 100_000 ? 'VP de Operaciones' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => !f['ef-sub-id'],
                            message: 'Debes indicar el proyecto al que se asigna la mano de obra.',
                            level: 'error',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-sub-serv', label: 'Servicio a subcontratar', type: 'text',
                        placeholder: 'Instalación eléctrica / Pintura industrial', rules: { required: true }
                    },
                    {
                        id: 'ef-sub-id', label: 'ID / nombre del proyecto', type: 'text',
                        placeholder: 'PRY-2025-045', rules: { required: true }
                    },
                    {
                        id: 'ef-sub-empresa', label: 'Empresa subcontratada', type: 'text',
                        placeholder: 'Razón social o nombre comercial', rules: { required: true }
                    },
                    {
                        id: 'ef-sub-plazo', label: 'Plazo de ejecución', type: 'text',
                        placeholder: '2 semanas / 5 días hábiles', rules: { required: true }
                    },
                    {
                        id: 'ef-sub-n-pers', label: 'Número de personas', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                    {
                        id: 'ef-sub-tipo-contrato', label: 'Tipo de contrato', type: 'select',
                        options: ['Pedido / PO', 'Contrato de obra', 'Contrato de servicios', 'Orden de trabajo']
                    },
                ],
            },

            /* ── Equipo ── */
            {
                id: 'equipo',
                name: 'Equipamiento y herramientas',
                desc: 'Herramientas o maquinaria para proyectos.',
                reqs: ['Cotización técnica', 'Factura', 'Alta de activo'],
                businessRules: {
                    montoMax: 1_000_000,
                    montoWarn: 200_000,
                    aprobadorSugerido: m => m >= 200_000 ? 'Director General' : 'VP de Operaciones',
                },
                fields: [
                    {
                        id: 'ef-eq-desc', label: 'Descripción del equipo', type: 'text',
                        placeholder: 'Fresadora CNC X5 / Taladro industrial', rules: { required: true }
                    },
                    {
                        id: 'ef-eq-destino', label: 'Área / proyecto destino', type: 'text',
                        placeholder: 'Línea de producción B', rules: { required: true }
                    },
                    {
                        id: 'ef-eq-tipo-adq', label: 'Tipo de adquisición', type: 'radio-grid',
                        options: [
                            { value: 'Compra definitiva', icon: 'fa-solid fa-circle-dollar-to-slot', label: 'Compra', hint: 'Activo fijo' },
                            { value: 'Renta / leasing', icon: 'fa-solid fa-rotate', label: 'Renta', hint: 'Uso temporal' },
                            { value: 'Leasing financiero', icon: 'fa-solid fa-file-contract', label: 'Leasing', hint: 'Arrendamiento financiero' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-eq-marca', label: 'Marca / modelo', type: 'text',
                        placeholder: 'Ej: Haas VF-2'
                    },
                    {
                        id: 'ef-eq-serie', label: 'Número de serie (si conocido)', type: 'text',
                        placeholder: 'SN-XXXX'
                    },
                ],
            },

            /* ── Honorarios ── */
            {
                id: 'honorarios',
                name: 'Honorarios de consultoría',
                desc: 'Consultores o freelancers por entregable.',
                reqs: ['Propuesta / SOW firmada', 'Factura'],
                businessRules: {
                    montoMax: 300_000,
                    montoWarn: 80_000,
                    aprobadorSugerido: m => m >= 80_000 ? 'Director General' : 'Director de Finanzas',
                },
                fields: [
                    {
                        id: 'ef-hon-nombre', label: 'Nombre del consultor/a', type: 'text',
                        placeholder: 'Ing. María López / Firma XYZ', rules: { required: true }
                    },
                    {
                        id: 'ef-hon-tipo', label: 'Tipo de tarifa', type: 'radio-grid',
                        options: [
                            { value: 'Por hora', icon: 'fa-regular fa-clock', label: 'Por hora', hint: '' },
                            { value: 'Por entregable', icon: 'fa-solid fa-file-check', label: 'Por entregable', hint: '' },
                            { value: 'Monto fijo', icon: 'fa-solid fa-tag', label: 'Monto fijo', hint: 'Fee mensual / proyecto' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-hon-tarifa-valor', label: 'Tarifa ($/hr o monto por entregable)', type: 'number',
                        placeholder: '0.00', rules: { min: 0 },
                        showIf: { field: 'ef-hon-tipo', op: '!=', value: 'Monto fijo' }
                    },
                    {
                        id: 'ef-hon-horas', label: 'Horas estimadas', type: 'counter',
                        rules: { min: 1 }, defaultValue: 8,
                        showIf: { field: 'ef-hon-tipo', op: '==', value: 'Por hora' }
                    },
                    {
                        id: 'ef-hon-alcance', label: 'Alcance / entregables', type: 'textarea',
                        placeholder: 'Describe los entregables acordados o el SOW de referencia',
                        rules: { required: true, maxLength: 1500 }
                    },
                    {
                        id: 'ef-hon-proyecto', label: 'Proyecto relacionado', type: 'text',
                        placeholder: 'PRY-2025-XX o nombre del proyecto'
                    },
                ],
            },

            /* ── Reembolso cliente ── */
            {
                id: 'reembolso-cli',
                name: 'Gastos reembolsables por cliente',
                desc: 'Erogaciones que serán facturadas al cliente.',
                reqs: ['Cláusula contractual', 'Factura original', 'Pre-aprobación cliente'],
                businessRules: {
                    montoMax: 200_000,
                    montoWarn: 50_000,
                    aprobadorSugerido: m => m >= 50_000 ? 'Director General' : 'Director de Finanzas',
                    alertas: [
                        {
                            condition: (f) => !f['ef-cli-cont'],
                            message: 'Este tipo de gasto requiere número de contrato con el cliente.',
                            level: 'error',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-cli', label: 'Nombre del cliente', type: 'text',
                        placeholder: 'Empresa ABC, S.A. de C.V.', rules: { required: true }
                    },
                    {
                        id: 'ef-cli-cont', label: 'Número de contrato', type: 'text',
                        placeholder: 'CONT-2025-XXXX', rules: { required: true }
                    },
                    {
                        id: 'ef-cli-conc', label: 'Concepto a reembolsar', type: 'text',
                        placeholder: 'Vuelos y hospedaje / materiales especificados', rules: { required: true }
                    },
                    {
                        id: 'ef-cli-preapro', label: '¿Tiene pre-aprobación del cliente?', type: 'radio-grid',
                        options: [
                            { value: 'Si, por escrito', icon: 'fa-solid fa-circle-check', label: 'Sí, por escrito', hint: 'Email o documento' },
                            { value: 'Si, verbal', icon: 'fa-solid fa-phone', label: 'Sí, verbal', hint: 'Confirmar por escrito' },
                            { value: 'No aún', icon: 'fa-solid fa-clock', label: 'No aún', hint: 'Pendiente de obtener' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-cli-ref', label: 'Referencia de pre-aprobación', type: 'text',
                        placeholder: 'Email del — / referencia del documento',
                        showIf: { field: 'ef-cli-preapro', op: '==', value: 'Si, por escrito' }
                    },
                ],
            },
        ],
    },

    /* ══════════════════════════════════════════════════════════════════════════
   FISCAL Y HONORARIOS
══════════════════════════════════════════════════════════════════════════ */
    fiscal: {
        label: 'Fiscal y Honorarios',
        color: '#dc2626',
        icon: 'fa-solid fa-landmark',
        desc: 'Pagos de impuestos, cuotas patronales y honorarios contables.',
        subs: [

            /* ── Impuestos federales ── */
            {
                id: 'impuestos-fed',
                name: 'Impuestos federales (SAT)',
                desc: 'ISR, IVA, retenciones y declaraciones ante el SAT.',
                reqs: ['Acuse de declaración SAT', 'Línea de captura', 'Comprobante de pago bancario'],
                businessRules: {
                    montoMax: 5_000_000,
                    montoWarn: 500_000,
                    aprobadorSugerido: m => m >= 500_000 ? 'Director General' : 'Director de Finanzas',
                    alertas: [
                        {
                            condition: (f) => !f['ef-fisc-linea-captura'],
                            message: 'Se requiere la línea de captura del SAT para procesar el pago.',
                            level: 'error',
                        },
                        {
                            condition: (f) => f['ef-fisc-tipo-imp'] === 'IVA' && !f['ef-fisc-periodo'],
                            message: 'Indica el período fiscal correspondiente a este pago de IVA.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-fisc-tipo-imp', label: 'Tipo de impuesto', type: 'radio-grid',
                        options: [
                            { value: 'ISR', icon: 'fa-solid fa-percent', label: 'ISR', hint: 'Impuesto sobre la renta' },
                            { value: 'IVA', icon: 'fa-solid fa-receipt', label: 'IVA', hint: 'Impuesto al valor agregado' },
                            { value: 'IEPS', icon: 'fa-solid fa-gas-pump', label: 'IEPS', hint: 'Especial prod. y servicios' },
                            { value: 'Retenciones', icon: 'fa-solid fa-hand-holding-dollar', label: 'Retenciones', hint: 'ISR / IVA retenido' },
                            { value: 'Otros', icon: 'fa-solid fa-ellipsis', label: 'Otros', hint: 'Derechos, aprovechamientos' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-fisc-periodo', label: 'Período fiscal', type: 'select',
                        options: [
                            'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
                            'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
                            '1er Bimestre', '2do Bimestre', '3er Bimestre',
                            '4to Bimestre', '5to Bimestre', '6to Bimestre',
                            'Anual',
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-fisc-ejercicio', label: 'Ejercicio fiscal (año)', type: 'text',
                        placeholder: '2025', rules: { required: true, maxLength: 4 }
                    },
                    {
                        id: 'ef-fisc-linea-captura', label: 'Línea de captura SAT', type: 'text',
                        placeholder: '09999900000012345678901', rules: { required: true, maxLength: 50 }
                    },
                    {
                        id: 'ef-fisc-fecha-limite', label: 'Fecha límite de pago', type: 'date',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-fisc-banco', label: 'Banco / cuenta de pago', type: 'text',
                        placeholder: 'BBVA Cuenta 012XXXXXXX'
                    },
                ],
            },

            /* ── Cuotas IMSS / INFONAVIT ── */
            {
                id: 'cuotas-patronales',
                name: 'Cuotas patronales IMSS / INFONAVIT',
                desc: 'Aportaciones de seguridad social y vivienda.',
                reqs: ['SUA / SIPARE generado', 'Línea de captura', 'Comprobante de pago'],
                businessRules: {
                    montoMax: 3_000_000,
                    montoWarn: 300_000,
                    aprobadorSugerido: () => 'Director de Finanzas',
                    alertas: [
                        {
                            condition: (f) => !f['ef-cp-periodo'],
                            message: 'Selecciona el período bimestral o mensual correspondiente.',
                            level: 'error',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-cp-tipo', label: 'Tipo de cuota', type: 'radio-grid',
                        options: [
                            { value: 'IMSS', icon: 'fa-solid fa-hospital', label: 'IMSS', hint: 'Cuotas obrero-patronales' },
                            { value: 'INFONAVIT', icon: 'fa-solid fa-house', label: 'INFONAVIT', hint: 'Aportación vivienda 5%' },
                            { value: 'IMSS + INFONAVIT', icon: 'fa-solid fa-layer-group', label: 'Ambos', hint: 'Pago conjunto bimestral' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-cp-periodo', label: 'Período bimestral / mensual', type: 'select',
                        options: [
                            'Enero (mensual)', 'Febrero (mensual)', 'Marzo (mensual)',
                            'Abril (mensual)', 'Mayo (mensual)', 'Junio (mensual)',
                            'Julio (mensual)', 'Agosto (mensual)', 'Septiembre (mensual)',
                            'Octubre (mensual)', 'Noviembre (mensual)', 'Diciembre (mensual)',
                            'Bimestre 1 (Ene-Feb)', 'Bimestre 2 (Mar-Abr)',
                            'Bimestre 3 (May-Jun)', 'Bimestre 4 (Jul-Ago)',
                            'Bimestre 5 (Sep-Oct)', 'Bimestre 6 (Nov-Dic)',
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-cp-trabajadores', label: 'Número de trabajadores', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1
                    },
                    {
                        id: 'ef-cp-linea', label: 'Línea de captura SIPARE/SUA', type: 'text',
                        placeholder: 'Número de referencia de pago', rules: { required: true }
                    },
                    {
                        id: 'ef-cp-fecha-limite', label: 'Fecha límite de pago', type: 'date',
                        rules: { required: true }
                    },
                ],
            },

            /* ── Honorarios contables / auditoría ── */
            {
                id: 'honorarios-fiscales',
                name: 'Honorarios contables y auditoría',
                desc: 'Despacho contable, auditor externo, declaraciones anuales.',
                reqs: ['Convenio u orden de servicio', 'Factura del despacho'],
                businessRules: {
                    montoMax: 400_000,
                    montoWarn: 80_000,
                    aprobadorSugerido: m => m >= 80_000 ? 'Director General' : 'Director de Finanzas',
                },
                fields: [
                    {
                        id: 'ef-hf-tipo', label: 'Tipo de servicio', type: 'radio-grid',
                        options: [
                            { value: 'Contabilidad mensual', icon: 'fa-solid fa-book', label: 'Contabilidad', hint: 'Servicio mensual recurrente' },
                            { value: 'Declaración anual', icon: 'fa-solid fa-calendar-check', label: 'Declaración anual', hint: 'ISR personas morales/físicas' },
                            { value: 'Auditoría externa', icon: 'fa-solid fa-magnifying-glass-chart', label: 'Auditoría', hint: 'Revisión de estados financieros' },
                            { value: 'Asesoría fiscal', icon: 'fa-solid fa-comments-dollar', label: 'Asesoría', hint: 'Consulta o defensa fiscal' },
                            { value: 'Dictamen fiscal', icon: 'fa-solid fa-file-invoice', label: 'Dictamen', hint: 'SIPIAD / CFF 32-A' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-hf-despacho', label: 'Despacho / contador', type: 'text',
                        placeholder: 'Nombre del despacho o CP externo', rules: { required: true }
                    },
                    {
                        id: 'ef-hf-periodo', label: 'Período que cubre', type: 'date-range',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-hf-contrato', label: 'Número de contrato / convenio', type: 'text',
                        placeholder: 'CONT-2025-XXXX'
                    },
                    {
                        id: 'ef-hf-recurrente', label: 'Pago recurrente', type: 'radio-grid',
                        options: [
                            { value: 'Mensual', icon: 'fa-solid fa-rotate', label: 'Mensual', hint: '' },
                            { value: 'Único', icon: 'fa-solid fa-circle-dot', label: 'Único', hint: 'Pago por evento' },
                        ]
                    },
                ],
            },

            /* ── Multas y recargos ── */
            {
                id: 'multas-sat',
                name: 'Multas, recargos y actualizaciones',
                desc: 'Créditos fiscales, multas SAT/IMSS y pagos extemporáneos.',
                reqs: ['Resolución o crédito fiscal', 'Acuse de notificación', 'Comprobante de pago'],
                businessRules: {
                    montoMax: 2_000_000,
                    montoWarn: 50_000,
                    aprobadorSugerido: m => m >= 50_000 ? 'Director General' : 'Director de Finanzas',
                    alertas: [
                        {
                            condition: () => true,
                            message: 'Los pagos de multas deben estar acompañados del número de crédito fiscal emitido por la autoridad.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-mul-autoridad', label: 'Autoridad emisora', type: 'radio-grid',
                        options: [
                            { value: 'SAT', icon: 'fa-solid fa-landmark', label: 'SAT', hint: '' },
                            { value: 'IMSS', icon: 'fa-solid fa-hospital', label: 'IMSS', hint: '' },
                            { value: 'INFONAVIT', icon: 'fa-solid fa-house', label: 'INFONAVIT', hint: '' },
                            { value: 'Otra', icon: 'fa-solid fa-building-columns', label: 'Otra', hint: 'Municipal, estatal…' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-mul-num-credito', label: 'Número de crédito fiscal', type: 'text',
                        placeholder: 'CF-XXXX-XXXXXXXX', rules: { required: true }
                    },
                    {
                        id: 'ef-mul-concepto', label: 'Concepto de la multa', type: 'textarea',
                        placeholder: 'Ej: Recargo por presentación extemporánea de IVA Enero 2024',
                        rules: { required: true, maxLength: 500 }
                    },
                    {
                        id: 'ef-mul-monto-original', label: 'Monto original del adeudo ($)', type: 'number',
                        placeholder: '0.00', rules: { min: 0 }
                    },
                    {
                        id: 'ef-mul-recargos', label: 'Recargos y actualizaciones ($)', type: 'number',
                        placeholder: '0.00', rules: { min: 0 }
                    },
                    {
                        id: 'ef-mul-fecha-limite', label: 'Fecha límite de pago', type: 'date',
                        rules: { required: true }
                    },
                ],
            },
        ],
    },

    /* ══════════════════════════════════════════════════════════════════════════
       COMERCIO EXTERIOR
    ══════════════════════════════════════════════════════════════════════════ */
    comercio_exterior: {
        label: 'Comercio Exterior',
        color: '#0891b2',
        icon: 'fa-solid fa-ship',
        desc: 'Importaciones, exportaciones, seguros de carga y gastos aduanales.',
        subs: [

            /* ── Gastos aduanales ── */
            {
                id: 'aduanal',
                name: 'Gastos aduanales y agente',
                desc: 'Honorarios del agente aduanal, DTA, prevalidación y trámites.',
                reqs: ['Pedimento aduanal', 'Factura del agente aduanal', 'Factura comercial de la mercancía'],
                businessRules: {
                    montoMax: 500_000,
                    montoWarn: 100_000,
                    aprobadorSugerido: m => m >= 100_000 ? 'VP de Operaciones' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => !f['ef-adua-pedimento'],
                            message: 'Se requiere el número de pedimento para registrar este gasto.',
                            level: 'error',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-adua-tipo-op', label: 'Tipo de operación', type: 'radio-grid',
                        options: [
                            { value: 'Importación definitiva', icon: 'fa-solid fa-arrow-down-to-line', label: 'Importación', hint: 'Clave A1' },
                            { value: 'Exportación definitiva', icon: 'fa-solid fa-arrow-up-from-line', label: 'Exportación', hint: 'Clave A1' },
                            { value: 'Importación temporal', icon: 'fa-solid fa-clock-rotate-left', label: 'Imp. temporal', hint: 'IMMEX / IT' },
                            { value: 'Retorno', icon: 'fa-solid fa-rotate-left', label: 'Retorno', hint: 'Devolución al extranjero' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-adua-pedimento', label: 'Número de pedimento', type: 'text',
                        placeholder: 'AA  XXXX  1234567', rules: { required: true }
                    },
                    {
                        id: 'ef-adua-agente', label: 'Agente aduanal / despacho', type: 'text',
                        placeholder: 'Razón social del agente aduanal', rules: { required: true }
                    },
                    {
                        id: 'ef-adua-aduana', label: 'Aduana de despacho', type: 'select',
                        options: [
                            'Nuevo Laredo', 'Lázaro Cárdenas', 'Veracruz', 'Altamira',
                            'Ciudad Juárez', 'Tijuana', 'Manzanillo', 'Querétaro (Aeropuerto)',
                            'AICM — Ciudad de México', 'El Paso / Ciudad Juárez', 'Otra',
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-adua-mercancias', label: 'Descripción de mercancías', type: 'textarea',
                        placeholder: 'Descripción general, cantidad y fracción arancelaria si se conoce',
                        rules: { required: true, maxLength: 1000 }
                    },
                    {
                        id: 'ef-adua-incoterm', label: 'Incoterm pactado', type: 'select',
                        options: ['EXW', 'FCA', 'FAS', 'FOB', 'CFR', 'CIF', 'CPT', 'CIP', 'DAP', 'DPU', 'DDP'],
                    },
                    {
                        id: 'ef-adua-pais-origen', label: 'País de origen / destino', type: 'text',
                        placeholder: 'EUA, China, Alemania…', rules: { required: true }
                    },
                    {
                        id: 'ef-adua-valor-comer', label: 'Valor comercial de la mercancía (USD)', type: 'number',
                        placeholder: '0.00', rules: { min: 0 }
                    },
                ],
            },

            /* ── Impuestos de importación ── */
            {
                id: 'impuestos-import',
                name: 'Impuestos y derechos de importación',
                desc: 'IGI (arancel), DTA, IVA de importación, cuotas compensatorias.',
                reqs: ['Pedimento aduanal', 'Comprobante de pago', 'Hoja de cálculo de contribuciones'],
                businessRules: {
                    montoMax: 2_000_000,
                    montoWarn: 200_000,
                    aprobadorSugerido: m => m >= 200_000 ? 'Director General' : 'Director de Finanzas',
                },
                fields: [
                    {
                        id: 'ef-imp-ped', label: 'Número de pedimento', type: 'text',
                        placeholder: 'AA  XXXX  1234567', rules: { required: true }
                    },
                    {
                        id: 'ef-imp-contribuciones', label: 'Contribuciones a pagar', type: 'radio-grid',
                        options: [
                            { value: 'IGI (Arancel)', icon: 'fa-solid fa-percent', label: 'IGI', hint: 'Impuesto general de importación' },
                            { value: 'DTA', icon: 'fa-solid fa-file-invoice-dollar', label: 'DTA', hint: 'Derecho de trámite aduanero' },
                            { value: 'IVA importación', icon: 'fa-solid fa-receipt', label: 'IVA imp.', hint: '16% sobre valor en aduana' },
                            { value: 'Cuota compensatoria', icon: 'fa-solid fa-shield-halved', label: 'C. Comp.', hint: 'Antidumping o compensatoria' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-imp-fraccion', label: 'Fracción arancelaria', type: 'text',
                        placeholder: '8471.30.01', rules: { maxLength: 15 }
                    },
                    {
                        id: 'ef-imp-tasa', label: 'Tasa arancelaria (%)', type: 'number',
                        placeholder: '0', rules: { min: 0, max: 100 }
                    },
                    {
                        id: 'ef-imp-valor-aduana', label: 'Valor en aduana (MXN)', type: 'number',
                        placeholder: '0.00', rules: { min: 0, required: true }
                    },
                ],
            },

            /* ── Seguro de carga ── */
            {
                id: 'seguro-carga',
                name: 'Seguro de carga internacional',
                desc: 'Prima de seguro para mercancías en tránsito.',
                reqs: ['Certificado de seguro', 'Factura de la aseguradora', 'Lista de empaque / packing list'],
                businessRules: {
                    montoMax: 300_000,
                    montoWarn: 60_000,
                    aprobadorSugerido: m => m >= 60_000 ? 'Director de Finanzas' : 'Gerente de Área',
                },
                fields: [
                    {
                        id: 'ef-sc-aseguradora', label: 'Aseguradora', type: 'text',
                        placeholder: 'GNP, AXA, Mapfre, HDI…', rules: { required: true }
                    },
                    {
                        id: 'ef-sc-poliza', label: 'Número de póliza / certificado', type: 'text',
                        placeholder: 'POL-INT-2025-XXXX', rules: { required: true }
                    },
                    {
                        id: 'ef-sc-modalidad', label: 'Modalidad de cobertura', type: 'radio-grid',
                        options: [
                            { value: 'Todo riesgo', icon: 'fa-solid fa-shield', label: 'Todo riesgo', hint: 'Cobertura amplia (ICC-A)' },
                            { value: 'Riesgos básicos', icon: 'fa-solid fa-shield-halved', label: 'Riesgos básicos', hint: 'ICC-C (naufragio, incendio)' },
                            { value: 'Solo robo', icon: 'fa-solid fa-user-shield', label: 'Solo robo', hint: '' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-sc-medio', label: 'Medio de transporte asegurado', type: 'radio-grid',
                        options: [
                            { value: 'Marítimo', icon: 'fa-solid fa-ship', label: 'Marítimo', hint: '' },
                            { value: 'Aéreo', icon: 'fa-solid fa-plane', label: 'Aéreo', hint: '' },
                            { value: 'Terrestre', icon: 'fa-solid fa-truck', label: 'Terrestre', hint: '' },
                            { value: 'Multimodal', icon: 'fa-solid fa-route', label: 'Multimodal', hint: '' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-sc-origen', label: 'Puerto / ciudad de origen', type: 'text',
                        placeholder: 'Shanghai, China', rules: { required: true }
                    },
                    {
                        id: 'ef-sc-destino', label: 'Puerto / ciudad de destino', type: 'text',
                        placeholder: 'Lázaro Cárdenas, MX', rules: { required: true }
                    },
                    {
                        id: 'ef-sc-valor-aseg', label: 'Valor asegurado de la carga (USD)', type: 'number',
                        placeholder: '0.00', rules: { required: true, min: 0 }
                    },
                    {
                        id: 'ef-sc-vigencia', label: 'Vigencia del certificado', type: 'date-range',
                        rules: { required: true }
                    },
                ],
            },

            /* ── Flete internacional ── */
            {
                id: 'flete-internacional',
                name: 'Flete internacional',
                desc: 'Marítimo, aéreo o terrestre transfronterizo.',
                reqs: ['Bill of Lading / AWB / Carta porte', 'Factura del naviero o agente de carga'],
                businessRules: {
                    montoMax: 800_000,
                    montoWarn: 150_000,
                    aprobadorSugerido: m => m >= 150_000 ? 'VP de Operaciones' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => !f['ef-fl-bl'],
                            message: 'Registra el número de BL, AWB o carta porte internacional.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-fl-tipo', label: 'Modalidad de flete', type: 'radio-grid',
                        options: [
                            { value: 'Marítimo FCL', icon: 'fa-solid fa-ship', label: 'Marítimo FCL', hint: 'Contenedor completo' },
                            { value: 'Marítimo LCL', icon: 'fa-solid fa-boxes-stacked', label: 'Marítimo LCL', hint: 'Carga consolidada' },
                            { value: 'Aéreo', icon: 'fa-solid fa-plane-departure', label: 'Aéreo', hint: 'Air freight' },
                            { value: 'Terrestre frontera', icon: 'fa-solid fa-truck-moving', label: 'Terrestre', hint: 'Cruce fronterizo' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-fl-bl', label: 'BL / AWB / Carta porte', type: 'text',
                        placeholder: 'Número de documento de transporte', rules: { required: true }
                    },
                    {
                        id: 'ef-fl-naviero', label: 'Naviero / aerolínea / carrier', type: 'text',
                        placeholder: 'MSC, Maersk, DHL Express…', rules: { required: true }
                    },
                    {
                        id: 'ef-fl-pol', label: 'Puerto de carga (POL)', type: 'text',
                        placeholder: 'Shanghai / Rotterdam / LAX'
                    },
                    {
                        id: 'ef-fl-pod', label: 'Puerto de descarga (POD)', type: 'text',
                        placeholder: 'Lázaro Cárdenas / Veracruz / AICM'
                    },
                    {
                        id: 'ef-fl-contenedor', label: 'Número de contenedor', type: 'text',
                        placeholder: 'MSCU1234567',
                        showIf: { field: 'ef-fl-tipo', op: 'includes', value: 'Marítimo' }
                    },
                    {
                        id: 'ef-fl-eta', label: 'ETA (fecha estimada de arribo)', type: 'date',
                        rules: { required: true }
                    },
                    {
                        id: 'ef-fl-peso', label: 'Peso total (kg)', type: 'number',
                        placeholder: '0', rules: { min: 0 }
                    },
                    {
                        id: 'ef-fl-pedimento', label: 'Pedimento relacionado', type: 'text',
                        placeholder: 'AA  XXXX  1234567 (si ya existe)'
                    },
                ],
            },

            /* ── Gastos portuarios / almacenaje ── */
            {
                id: 'gastos-portuarios',
                name: 'Gastos portuarios y almacenaje',
                desc: 'Demoras, almacenaje en aduana, maniobras y gastos locales.',
                reqs: ['Nota de cargo / invoice del terminal', 'Pedimento relacionado'],
                businessRules: {
                    montoMax: 200_000,
                    montoWarn: 40_000,
                    aprobadorSugerido: m => m >= 40_000 ? 'Director de Finanzas' : 'Gerente de Área',
                    alertas: [
                        {
                            condition: (f) => f['ef-gp-tipo'] === 'Demora (demurrage)' || f['ef-gp-tipo'] === 'Sobreestadía (detention)',
                            message: 'Las demoras generan costos evitables. Adjunta justificación de la causa.',
                            level: 'warn',
                        },
                    ],
                },
                fields: [
                    {
                        id: 'ef-gp-tipo', label: 'Tipo de gasto', type: 'radio-grid',
                        options: [
                            { value: 'Almacenaje en aduana', icon: 'fa-solid fa-warehouse', label: 'Almacenaje', hint: 'Días en recinto fiscal' },
                            { value: 'Demora (demurrage)', icon: 'fa-solid fa-hourglass-half', label: 'Demurrage', hint: 'Días extra de contenedor' },
                            { value: 'Sobreestadía (detention)', icon: 'fa-solid fa-calendar-xmark', label: 'Detention', hint: 'Uso prolongado de equipo' },
                            { value: 'Maniobras', icon: 'fa-solid fa-forklift', label: 'Maniobras', hint: 'Carga, descarga, reestiba' },
                            { value: 'Gastos locales', icon: 'fa-solid fa-location-dot', label: 'Gastos locales', hint: 'THC, B/L, D.O.' },
                        ],
                        rules: { required: true }
                    },
                    {
                        id: 'ef-gp-proveedor', label: 'Terminal / proveedor', type: 'text',
                        placeholder: 'Terminal Lázaro Cárdenas / SSA México', rules: { required: true }
                    },
                    {
                        id: 'ef-gp-dias', label: 'Días incurridos', type: 'counter',
                        rules: { min: 1 }, defaultValue: 1,
                        showIf: { field: 'ef-gp-tipo', op: '!=', value: 'Maniobras' }
                    },
                    {
                        id: 'ef-gp-contenedor', label: 'Número de contenedor', type: 'text',
                        placeholder: 'MSCU1234567'
                    },
                    {
                        id: 'ef-gp-pedimento', label: 'Pedimento relacionado', type: 'text',
                        placeholder: 'AA  XXXX  1234567'
                    },
                    {
                        id: 'ef-gp-justif', label: 'Justificación de la demora', type: 'textarea',
                        placeholder: 'Explica la causa (retraso en documentos, inspección SAT, etc.)',
                        showIf: { field: 'ef-gp-tipo', op: 'includes', value: 'demurrage' }
                    },
                ],
            },
        ],
    },



};

/* ══════════════════════════════════════════════════════════════════════════
   REGLAS GLOBALES DE APROBACIÓN
   (se evalúan SIEMPRE, independientemente de la subcategoría)
══════════════════════════════════════════════════════════════════════════ */
const APPROVAL_TIERS = [
    { upTo: 5_000, label: 'Coordinador / Jefe directo' },
    { upTo: 20_000, label: 'Gerente de Área' },
    { upTo: 80_000, label: 'Director de Finanzas' },
    { upTo: 200_000, label: 'VP de Operaciones' },
    { upTo: Infinity, label: 'Director General' },
];

function getGlobalAprobador(monto) {
    return APPROVAL_TIERS.find(t => monto <= t.upTo)?.label ?? 'Director General';
}

/* ══════════════════════════════════════════════════════════════════════════
   CENTROS DE COSTO
══════════════════════════════════════════════════════════════════════════ */
const CENTROS_COSTO = [
    'Administración General',
    'Ventas y Comercial',
    'Operaciones',
    'Tecnología',
    'Recursos Humanos',
    'Finanzas',
    'Proyectos Especiales',
    'Producción Línea A',
    'Producción Línea B',
    'Calidad',
    'Logística',
];

/* ══════════════════════════════════════════════════════════════════════════
   APROBADORES
══════════════════════════════════════════════════════════════════════════ */
const APROBADORES = [
    'Coordinador Administrativo',
    'Coordinador de Compras',
    'Coordinador de RH',
    'Gerente de Área',
    'Director de RH',
    'Director de Finanzas',
    'Director de Operaciones',
    'VP de Operaciones',
    'Director General',
];
