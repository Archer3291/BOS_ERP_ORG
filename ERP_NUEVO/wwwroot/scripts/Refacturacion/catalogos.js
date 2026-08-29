// ═══════════════════════════════════════════════════════════════
//  CATÁLOGO DE TIPOS  (sin cambios)
// ═══════════════════════════════════════════════════════════════
const TIPOS_REFACTURACION = [
    { id: 'datos-fiscales', name: 'Error en datos fiscales', desc: 'RFC, razón social, régimen, CP o uso CFDI incorrecto', icon: 'fa-id-card', color: 'blue', risk: 'low', riskLabel: 'Riesgo bajo' },
    { id: 'conceptos', name: 'Error en conceptos', desc: 'Cantidad, precio, unidad, clave SAT o descuento incorrecto', icon: 'fa-list', color: 'orange', risk: 'med', riskLabel: 'Riesgo medio' },
    { id: 'metodo-pago', name: 'Cambio método de pago', desc: 'PUE ↔ PPD, con o sin complementos de pago existentes', icon: 'fa-credit-card', color: 'red', risk: 'high', riskLabel: 'Riesgo alto' },
    { id: 'moneda', name: 'Cambio de moneda', desc: 'USD→MXN, tipo de cambio incorrecto, diferencias cambiarias', icon: 'fa-coins', color: 'orange', risk: 'med', riskLabel: 'Riesgo medio' },
    // { id: 'forma-pago', name: 'Cambio forma de pago', desc: 'Transferencia→efectivo, tarjeta→transferencia', icon: 'fa-wallet', color: 'blue', risk: 'low', riskLabel: 'Riesgo bajo' },
    // { id: 'parcial', name: 'Refacturación parcial', desc: 'Dividir o fusionar facturas, redistribuir partidas', icon: 'fa-scissors', color: 'purple', risk: 'med', riskLabel: 'Riesgo medio' },
    // { id: 'devolucion', name: 'Devolución / Nota crédito', desc: 'Devolución de mercancía, cancelación parcial, NC', icon: 'fa-rotate-left', color: 'orange', risk: 'med', riskLabel: 'Riesgo medio' },
    // { id: 'anticipo', name: 'Anticipo incorrecto', desc: 'Anticipo mal aplicado, CFDI incorrecto, cliente distinto', icon: 'fa-hand-holding-dollar', color: 'red', risk: 'very-high', riskLabel: 'Riesgo muy alto' },
    // { id: 'factura-pagada', name: 'Factura ya pagada', desc: 'CFDI cobrado y conciliado, cancelar pagos primero', icon: 'fa-money-check-dollar', color: 'red', risk: 'high', riskLabel: 'Riesgo alto' },
    // { id: 'complemento-pago', name: 'Complemento de pago', desc: 'Pago mal aplicado, UUID incorrecto, parcialidad errónea', icon: 'fa-file-invoice-dollar', color: 'orange', risk: 'high', riskLabel: 'Riesgo alto' },
    // { id: 'impuestos', name: 'Error en impuestos', desc: 'IVA 16→0, exento incorrecto, retenciones faltantes', icon: 'fa-percent', color: 'red', risk: 'very-high', riskLabel: 'Riesgo muy alto' },
    // { id: 'global', name: 'CFDI Global / Ticket', desc: 'Ticket incorrecto, cliente solicita factura nominativa', icon: 'fa-receipt', color: 'purple', risk: 'med', riskLabel: 'Riesgo medio' },
    // { id: 'sustitucion-diferida', name: 'Sustitución diferida', desc: 'CFDI relacionado emitido, cancelación posterior SAT', icon: 'fa-clock-rotate-left', color: 'purple', risk: 'med', riskLabel: 'Riesgo medio' },
    // { id: 'masiva', name: 'Refacturación masiva', desc: 'Cambio fiscal masivo, error de sistema, colas async', icon: 'fa-layer-group', color: 'purple', risk: 'high', riskLabel: 'Riesgo alto' },
    {
        id: 'adenda',
        name: 'Corrección de Adenda',
        desc: 'El CFDI es correcto, pero la Adenda tiene datos erróneos o faltantes para el ERP del cliente.',
        icon: 'fa-file-code',
        color: 'purple',
        risk: 'low',
        riskLabel: 'Riesgo bajo'
    },
];
const CAT_REGIMEN_FISCAL = [
    { clave: '601', desc: '601 · General de Ley Personas Morales', persona: 'M' },
    { clave: '603', desc: '603 · Personas Morales con Fines no Lucrativos', persona: 'M' },
    { clave: '605', desc: '605 · Sueldos y Salarios e Ingresos Asimilados', persona: 'F' },
    { clave: '606', desc: '606 · Arrendamiento', persona: 'F' },
    { clave: '607', desc: '607 · Régimen de Enajenación o Adquisición de Bienes', persona: 'F' },
    { clave: '608', desc: '608 · Demás ingresos', persona: 'F' },
    { clave: '610', desc: '610 · Residentes en el Extranjero sin Establecimiento Permanente', persona: 'FM' },
    { clave: '611', desc: '611 · Ingresos por Dividendos', persona: 'F' },
    { clave: '612', desc: '612 · Personas Físicas con Actividades Empresariales y Profesionales', persona: 'F' },
    { clave: '614', desc: '614 · Ingresos por intereses', persona: 'F' },
    { clave: '615', desc: '615 · Régimen de los ingresos por obtención de premios', persona: 'F' },
    { clave: '616', desc: '616 · Sin obligaciones fiscales', persona: 'F' },
    { clave: '620', desc: '620 · Sociedades Cooperativas de Producción', persona: 'M' },
    { clave: '621', desc: '621 · Incorporación Fiscal', persona: 'F' },
    { clave: '622', desc: '622 · Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras', persona: 'FM' },
    { clave: '623', desc: '623 · Opcional para Grupos de Sociedades', persona: 'M' },
    { clave: '624', desc: '624 · Coordinados', persona: 'M' },
    { clave: '625', desc: '625 · Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas', persona: 'F' },
    { clave: '626', desc: '626 · Régimen Simplificado de Confianza RESICO', persona: 'FM' },
];

const CAT_USO_CFDI = [
    { clave: 'G01', desc: 'G01 · Adquisición de mercancias', persona: 'FM' },
    { clave: 'G02', desc: 'G02 · Devoluciones, descuentos o bonificaciones', persona: 'FM' },
    { clave: 'G03', desc: 'G03 · Gastos en general', persona: 'FM' },
    { clave: 'I01', desc: 'I01 · Construcciones', persona: 'FM' },
    { clave: 'I02', desc: 'I02 · Mobilario y equipo de oficina por inversiones', persona: 'FM' },
    { clave: 'I03', desc: 'I03 · Equipo de transporte', persona: 'FM' },
    { clave: 'I04', desc: 'I04 · Equipo de computo y accesorios', persona: 'FM' },
    { clave: 'I05', desc: 'I05 · Dados, troqueles, moldes, matrices y herramental', persona: 'FM' },
    { clave: 'I06', desc: 'I06 · Comunicaciones telefónicas', persona: 'FM' },
    { clave: 'I07', desc: 'I07 · Comunicaciones satelitales', persona: 'FM' },
    { clave: 'I08', desc: 'I08 · Otra maquinaria y equipo', persona: 'FM' },
    { clave: 'D01', desc: 'D01 · Honorarios médicos, dentales y gastos hospitalarios', persona: 'F' },
    { clave: 'D02', desc: 'D02 · Gastos médicos por incapacidad o discapacidad', persona: 'F' },
    { clave: 'D03', desc: 'D03 · Gastos funerales', persona: 'F' },
    { clave: 'D04', desc: 'D04 · Donativos', persona: 'F' },
    { clave: 'D05', desc: 'D05 · Intereses reales efectivamente pagados por créditos hipotecarios (casa habitación)', persona: 'F' },
    { clave: 'D06', desc: 'D06 · Aportaciones voluntarias al SAR', persona: 'F' },
    { clave: 'D07', desc: 'D07 · Primas por seguros de gastos médicos', persona: 'F' },
    { clave: 'D08', desc: 'D08 · Gastos de transportación escolar obligatoria', persona: 'F' },
    { clave: 'D09', desc: 'D09 · Depósitos en cuentas para el ahorro, primas que tengan como base planes de pensiones', persona: 'F' },
    { clave: 'D10', desc: 'D10 · Pagos por servicios educativos (colegiaturas)', persona: 'F' },
    { clave: 'CP01', desc: 'CP01 · Pagos', persona: 'FM' },
    { clave: 'CN01', desc: 'CN01 · Nómina', persona: 'F' },
    { clave: 'S01', desc: 'S01 · Sin efectos fiscales', persona: 'FM' },
];

const CAT_FORMA_PAGO = [
    { clave: '01', desc: '01 · Efectivo' },
    { clave: '02', desc: '02 · Cheque nominativo' },
    { clave: '03', desc: '03 · Transferencia electrónica de fondos' },
    { clave: '04', desc: '04 · Tarjeta de crédito' },
    { clave: '05', desc: '05 · Monedero electrónico' },
    { clave: '06', desc: '06 · Dinero electrónico' },
    { clave: '08', desc: '08 · Vales de despensa' },
    { clave: '12', desc: '12 · Dación en pago' },
    { clave: '13', desc: '13 · Pago por subrogación' },
    { clave: '14', desc: '14 · Pago por consignación' },
    { clave: '15', desc: '15 · Condonación' },
    { clave: '17', desc: '17 · Compensación' },
    { clave: '23', desc: '23 · Novación' },
    { clave: '24', desc: '24 · Confusión' },
    { clave: '25', desc: '25 · Remisión de deuda' },
    { clave: '26', desc: '26 · Prescripción o caducidad' },
    { clave: '27', desc: '27 · A satisfacción del acreedor' },
    { clave: '28', desc: '28 · Tarjeta de débito' },
    { clave: '29', desc: '29 · Tarjeta de servicios' },
    { clave: '30', desc: '30 · Aplicación de anticipos' },
    { clave: '31', desc: '31 · Intermediario pagos' },
    { clave: '99', desc: '99 · Por definir' },
];

const CAT_MONEDA = [
    { clave: 'MXN', desc: 'MXN · Peso mexicano', tc: false },
    { clave: 'USD', desc: 'USD · Dólar americano', tc: true },
    { clave: 'EUR', desc: 'EUR · Euro', tc: true },
    { clave: 'CAD', desc: 'CAD · Dólar canadiense', tc: true },
    { clave: 'GBP', desc: 'GBP · Libra esterlina', tc: true },
    { clave: 'JPY', desc: 'JPY · Yen japonés', tc: true },
    { clave: 'CHF', desc: 'CHF · Franco suizo', tc: true },
    { clave: 'XXX', desc: 'XXX · Los fondos no son en divisas', tc: false },
];

const CAT_EXPORTACION = [
    { clave: '01', desc: '01 · No aplica' },
    { clave: '02', desc: '02 · Definitiva' },
    { clave: '03', desc: '03 · Temporal' },
    { clave: '04', desc: '04 · Definitiva con clave A1' },
];

const CAT_TIPO_RELACION = [
    { clave: '01', desc: '01 · Nota de crédito de los documentos relacionados' },
    { clave: '02', desc: '02 · Nota de débito de los documentos relacionados' },
    { clave: '03', desc: '03 · Devolución de mercancía sobre facturas o traslados previos' },
    { clave: '04', desc: '04 · Sustitución de los CFDI previos' },
    { clave: '05', desc: '05 · Traslados de mercancias facturados previamente' },
    { clave: '06', desc: '06 · Factura generada por los traslados previos' },
    { clave: '07', desc: '07 · CFDI por aplicación de anticipo' },
    { clave: '08', desc: '08 · Facturas generadas por pagos en parcialidades' },
    { clave: '09', desc: '09 · Factura generada por pagos diferidos' },
];

const CAT_PERIODICIDAD = [
    { clave: '01', desc: '01 · Diario' },
    { clave: '02', desc: '02 · Semanal' },
    { clave: '03', desc: '03 · Quincenal' },
    { clave: '04', desc: '04 · Mensual' },
    { clave: '05', desc: '05 · Bimestral' },
];

const CAT_MESES = [
    { clave: '01', desc: 'Enero' }, { clave: '02', desc: 'Febrero' },
    { clave: '03', desc: 'Marzo' }, { clave: '04', desc: 'Abril' },
    { clave: '05', desc: 'Mayo' }, { clave: '06', desc: 'Junio' },
    { clave: '07', desc: 'Julio' }, { clave: '08', desc: 'Agosto' },
    { clave: '09', desc: 'Septiembre' }, { clave: '10', desc: 'Octubre' },
    { clave: '11', desc: 'Noviembre' }, { clave: '12', desc: 'Diciembre' },
    { clave: '13', desc: 'Enero-Febrero' }, { clave: '14', desc: 'Marzo-Abril' },
    { clave: '15', desc: 'Mayo-Junio' }, { clave: '16', desc: 'Julio-Agosto' },
    { clave: '17', desc: 'Septiembre-Octubre' }, { clave: '18', desc: 'Noviembre-Diciembre' },
];