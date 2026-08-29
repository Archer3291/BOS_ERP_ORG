/* ──────────────────────────────────────────────────────────────────────────
   Tablero del Home — área de COBRANZA (cartera, vencimientos y pagos).
   Se registra en el núcleo (dashboard/core.js); el núcleo decide cuándo
   pedir los datos del área y cuándo redibujar.
   ────────────────────────────────────────────────────────────────────────── */
(function (D) {
    'use strict';

    const {
        rows, rowsAll, num, text, trunc, esc, fecha, aFecha, claveDia, el, estado, totales,
        money, moneyShort, units, unitsShort, fmtInt, fmtDec1, tokens, tooltipBase,
        filaTooltip, tituloTooltip, etiquetaEje, ejeValor, ejeCategoriaY, rejillaBarras,
        relleno, alpha, FONT, FONT_NUM, make, soltar, tabla, vacio, altoPorFilas,
        view
    } = D;

    // Claves del SAT: la factura guarda el código, no el nombre.
    const PAGOS_SAT = {
        '01': 'Efectivo', '02': 'Cheque nominativo', '03': 'Transferencia',
        '04': 'Tarjeta de crédito', '05': 'Monedero electrónico', '06': 'Dinero electrónico',
        '08': 'Vales de despensa', '12': 'Dación en pago', '13': 'Pago por subrogación',
        '14': 'Pago por consignación', '15': 'Condonación', '17': 'Compensación',
        '23': 'Novación', '24': 'Confusión', '25': 'Remisión de deuda',
        '26': 'Prescripción o caducidad', '27': 'A satisfacción del acreedor',
        '28': 'Tarjeta de débito', '29': 'Tarjeta de servicios',
        '30': 'Aplicación de anticipos', '31': 'Intermediario de pagos', '99': 'Por definir',
        'PUE': 'Pago en una exhibición', 'PPD': 'Pago en parcialidades', 'ND': 'Sin dato'
    };

    function nombrePago(v) {
        let k = text(v).toUpperCase();
        if (/^\d$/.test(k)) k = '0' + k;
        return PAGOS_SAT[k] || (k || 'Sin dato');
    }

    const RANGOS = ['Por vencer', '1 a 30 días', '31 a 60 días', '61 a 90 días', 'Más de 90 días'];

    // 2 · Medidor de cobranza (una razón contra un límite → medidor)
    function renderCobertura(t) {
        const k = totales();
        const pct = k.facturado ? (k.cobrado / k.facturado) * 100 : 0;
        const caption = el('#coberturaCaption');

        const estado = pct >= 90
            ? { color: t.good, icono: 'fa-circle-check', texto: 'Cobranza al corriente' }
            : (pct >= 60
                ? { color: t.warning, icono: 'fa-triangle-exclamation', texto: 'Cobranza por debajo' }
                : { color: t.critical, icono: 'fa-circle-exclamation', texto: 'Cobranza rezagada' });

        if (!vacio('cobertura', k.facturado > 0 || k.cobrado > 0, 'Sin facturación ni cobros hoy')) {
            caption.innerHTML = '';
            soltar('chartCobertura');
            return;
        }

        // El estado nunca se apoya sólo en el color: lleva icono y texto.
        caption.innerHTML =
            '<div class="dash-caption-row"><span class="dash-dot" style="background:' + t.s3 + '"></span>' +
            'Cobrado hoy <b>' + money(k.cobrado) + '</b></div>' +
            '<div class="dash-caption-row"><span class="dash-dot" style="background:' + t.s1 + '"></span>' +
            'Facturado hoy <b>' + money(k.facturado) + '</b></div>' +
            '<span class="dash-state" style="color:' + estado.color + '">' +
            '<i class="fas ' + estado.icono + '"></i>' + estado.texto + '</span>';

        make('cobertura', 'chartCobertura', {
            animationDuration: 700,
            series: [{
                type: 'gauge',
                startAngle: 200, endAngle: -20,
                min: 0, max: 100,
                radius: '100%',
                center: ['50%', '72%'],
                progress: {
                    show: true, width: 16, roundCap: true,
                    itemStyle: { color: relleno(estado.color, true) }
                },
                axisLine: { roundCap: true, lineStyle: { width: 16, color: [[1, t.rail]] } },
                pointer: { show: false },
                axisTick: { show: false },
                splitLine: { show: false },
                axisLabel: { show: false },
                anchor: { show: false },
                title: { show: false },
                detail: {
                    valueAnimation: true,
                    offsetCenter: [0, '-18%'],
                    fontFamily: FONT, fontSize: 30, fontWeight: 700, color: t.ink,
                    formatter: () => fmtDec1.format(pct) + '%'
                },
                data: [{ value: Math.max(0, Math.min(pct, 100)) }]
            }]
        });
    }

    function renderAging(t) {
        const mapa = new Map();
        rows('aging').forEach(r => {
            const s = text(r.descripcion) || 'Sin sucursal';
            if (!mapa.has(s)) mapa.set(s, { sucursal: s, saldos: [0, 0, 0, 0, 0], docs: [0, 0, 0, 0, 0], total: 0 });
            const it = mapa.get(s);
            const i = Math.max(0, Math.min(4, Math.round(num(r.orden))));
            it.saldos[i] += num(r.saldo);
            it.docs[i] += num(r.facturas);
            it.total += num(r.saldo);
        });

        const datos = Array.from(mapa.values()).sort((a, b) => a.total - b.total);

        const filas = [];
        datos.slice().reverse().forEach(d => {
            RANGOS.forEach((rango, i) => {
                if (d.saldos[i] > 0) filas.push({ sucursal: d.sucursal, rango, docs: d.docs[i], saldo: d.saldos[i] });
            });
        });

        tabla('aging', [
            { label: 'Sucursal', value: d => d.sucursal },
            { label: 'Antigüedad', value: d => d.rango },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.docs) },
            { label: 'Saldo pendiente', num: true, value: d => money(d.saldo) }
        ], filas, 'Sin saldos pendientes');

        if (!vacio('aging', datos.length, 'No hay cartera con saldo pendiente')) {
            soltar('chartAging');
            return;
        }

        // barras apiladas: renglones más gruesos y con más piso que un ranking
        altoPorFilas('aging', datos.length, 52, 240);

        make('aging', 'chartAging', {
            animationDuration: 500,
            grid: { top: 44, left: 172, right: 24, bottom: 28, containLabel: false },
            legend: {
                type: 'scroll', top: 4, left: 'center',
                icon: 'circle', itemWidth: 8, itemHeight: 8, itemGap: 14,
                textStyle: { color: t.ink2, fontFamily: FONT, fontSize: 10.5 }
            },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'shadow', shadowStyle: { color: t.grid, opacity: 0.35 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    const filas = params
                        .filter(p => num(p.value) > 0)
                        .map(p => filaTooltip(p.marker, p.seriesName + ' · ' +
                            fmtInt.format(d.docs[p.seriesIndex]) + ' fact.', money(p.value)));
                    return tituloTooltip(d.sucursal) + filas.join('') +
                        filaTooltip('', 'Saldo total', money(d.total));
                }
            }),
            xAxis: ejeValor(t, moneyShort),
            yAxis: ejeCategoriaY(t, datos.map(d => d.sucursal), 150),
            series: RANGOS.map((rango, i) => ({
                name: rango,
                type: 'bar',
                stack: 'saldo',
                barMaxWidth: 22,
                itemStyle: {
                    color: t.ord[i],
                    // separador del color de la superficie entre segmentos
                    borderColor: t.surface, borderWidth: 1
                },
                data: datos.map(d => d.saldos[i])
            }))
        });
    }

    // 4c · Formas de pago del mes (parte-todo con pocas categorías → dona)
    function renderPagos(t) {
        const porForma = new Map();
        const detalle = new Map();
        rows('pagos').forEach(r => {
            const forma = nombrePago(r.forma_pago);
            const metodo = nombrePago(r.metodo);
            if (!porForma.has(forma)) porForma.set(forma, { nombre: forma, monto: 0, docs: 0 });
            const it = porForma.get(forma);
            it.monto += num(r.monto);
            it.docs += num(r.facturas);

            const clave = forma + '|' + metodo;
            if (!detalle.has(clave)) detalle.set(clave, { forma, metodo, monto: 0, docs: 0 });
            const dt = detalle.get(clave);
            dt.monto += num(r.monto);
            dt.docs += num(r.facturas);
        });

        let datos = Array.from(porForma.values()).sort((a, b) => b.monto - a.monto);
        if (datos.length > 6) {
            const resto = datos.slice(5);
            datos = datos.slice(0, 5).concat([{
                nombre: 'Otras formas',
                monto: resto.reduce((s, d) => s + d.monto, 0),
                docs: resto.reduce((s, d) => s + d.docs, 0),
                agrupado: true
            }]);
        }

        const paleta = [t.s1, t.s2, t.s3, t.s4, t.s5, t.s6];
        datos.forEach((d, i) => { d.color = d.agrupado ? t.muted : paleta[i % paleta.length]; });

        const total = datos.reduce((s, d) => s + d.monto, 0);
        const legend = el('#pagosLegend');

        tabla('pagos', [
            { label: 'Forma de pago', value: d => d.forma },
            { label: 'Método', value: d => d.metodo },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.docs) },
            { label: 'Importe', num: true, value: d => money(d.monto) }
        ], Array.from(detalle.values()).sort((a, b) => b.monto - a.monto), 'Sin facturación este mes');

        if (!vacio('pagos', total > 0, 'Sin facturación este mes')) {
            legend.innerHTML = '';
            soltar('chartPagos');
            return;
        }

        legend.innerHTML = datos.map(d =>
            '<div class="dash-legend-row">' +
            '<span class="dash-dot" style="background:' + d.color + '"></span>' +
            '<span class="dash-legend-name">' + esc(d.nombre) + '</span>' +
            '<span class="dash-legend-val">' + moneyShort(d.monto) + '</span>' +
            '<span class="dash-legend-pct">' + fmtDec1.format(d.monto / total * 100) + '%</span>' +
            '</div>').join('');

        make('pagos', 'chartPagos', {
            animationDuration: 600,
            title: {
                text: moneyShort(total), subtext: 'del mes',
                left: '50%', top: '38%', textAlign: 'center',
                textStyle: { fontFamily: FONT, fontSize: 20, fontWeight: 700, color: t.ink },
                subtextStyle: { fontFamily: FONT, fontSize: 11, color: t.muted }
            },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const d = datos[p.dataIndex];
                    return tituloTooltip(d.nombre) +
                        filaTooltip(p.marker, fmtInt.format(d.docs) + ' factura' + (d.docs === 1 ? '' : 's'), money(d.monto)) +
                        filaTooltip('', 'Participación', fmtDec1.format(d.monto / total * 100) + '%');
                }
            }),
            series: [{
                type: 'pie',
                radius: ['62%', '88%'],
                center: ['50%', '50%'],
                label: { show: false },
                labelLine: { show: false },
                itemStyle: { borderColor: t.surface, borderWidth: 2, borderRadius: 4 },
                emphasis: { scaleSize: 6 },
                data: datos.map(d => ({ name: d.nombre, value: d.monto, itemStyle: { color: d.color } }))
            }]
        });
    }

    // 8 · Crédito que vence hoy
    function renderVence(t) {
        const datos = rows('vence')
            .slice()
            .sort((a, b) => num(b.saldo_pendiente) - num(a.saldo_pendiente));

        tabla('vence', [
            { label: 'Folio', value: d => text(d.folio) || '—' },
            { label: 'Cliente', value: d => text(d.rsocliente) || '—' },
            { label: 'Sucursal', value: d => text(d.descripcion) || '—' },
            { label: 'Emitida', value: d => fecha(d.fecha) },
            { label: 'Moneda', value: d => text(d.moneda) || '—' },
            { label: 'Total', num: true, value: d => money(d.total) },
            { label: 'Saldo pendiente', num: true, value: d => money(d.saldo_pendiente) }
        ], datos, 'Hoy no vence ninguna factura de crédito');

        if (!vacio('vence', datos.length, 'Hoy no vence ninguna factura de crédito')) {
            soltar('chartVence');
            return;
        }

        const top = datos.slice(0, 10);
        altoPorFilas('vence', top.length);

        make('vence', 'chartVence', {
            animationDuration: 400,
            grid: rejillaBarras(210, 72),
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const d = top[p.dataIndex];
                    return tituloTooltip(text(d.rsocliente)) +
                        filaTooltip(p.marker, 'Saldo pendiente', money(d.saldo_pendiente)) +
                        filaTooltip('', 'Folio ' + text(d.folio), money(d.total) + ' ' + text(d.moneda)) +
                        '<div style="opacity:.7;margin-top:4px;">' + esc(text(d.descripcion)) + '</div>';
                }
            }),
            xAxis: ejeValor(t, moneyShort),
            yAxis: ejeCategoriaY(t, top.map(d => text(d.folio) + ' · ' + text(d.rsocliente)), 210),
            series: [{
                type: 'bar',
                barMaxWidth: 18,
                itemStyle: { color: relleno(t.warning, true), borderRadius: [0, 4, 4, 0] },
                showBackground: true,
                backgroundStyle: { color: t.rail, borderRadius: [0, 4, 4, 0] },
                label: {
                    show: false, position: 'right', distance: 8,
                    color: t.ink2, fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                    formatter: p => moneyShort(p.value)
                },
                animationDelay: i => i * 45,
                data: top.map((d, i) => ({
                    value: num(d.saldo_pendiente),
                    label: { show: i === 0 }   // ya viene ordenado de mayor a menor
                }))
            }]
        });
    }

    // 8b · Clientes con saldo ya vencido
    function renderRiesgo(t) {
        const mapa = new Map();
        rows('riesgo').forEach(r => {
            const cliente = text(r.rsocliente) || 'Sin cliente';
            if (!mapa.has(cliente)) {
                mapa.set(cliente, { cliente, saldo: 0, docs: 0, dias: 0, sucursales: new Set() });
            }
            const it = mapa.get(cliente);
            it.saldo += num(r.saldo_vencido);
            it.docs += num(r.facturas);
            it.dias = Math.max(it.dias, num(r.dias_atraso));
            if (text(r.descripcion)) it.sucursales.add(text(r.descripcion));
        });

        const todos = Array.from(mapa.values()).sort((a, b) => b.saldo - a.saldo);
        const datos = todos.slice(0, 10);

        tabla('riesgo', [
            { label: 'Cliente', value: d => d.cliente },
            { label: 'Sucursales', value: d => Array.from(d.sucursales).join(', ') || '—' },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.docs) },
            { label: 'Saldo vencido', num: true, value: d => money(d.saldo) },
            { label: 'Días de atraso', num: true, value: d => fmtInt.format(d.dias) }
        ], todos, 'Ningún cliente con saldo vencido');

        if (!vacio('riesgo', datos.length, 'Ningún cliente trae saldo vencido')) {
            soltar('chartRiesgo');
            return;
        }

        altoPorFilas('riesgo', datos.length);

        make('riesgo', 'chartRiesgo', {
            animationDuration: 500,
            grid: rejillaBarras(150, 64),
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const d = datos[p.dataIndex];
                    return tituloTooltip(d.cliente) +
                        filaTooltip(p.marker, 'Saldo vencido', money(d.saldo)) +
                        filaTooltip('', fmtInt.format(d.docs) + ' factura' + (d.docs === 1 ? '' : 's'),
                            fmtInt.format(d.dias) + ' días de atraso') +
                        (d.sucursales.size ? '<div style="opacity:.7;margin-top:4px;max-width:280px;white-space:normal;">' +
                            esc(Array.from(d.sucursales).join(', ')) + '</div>' : '');
                }
            }),
            xAxis: Object.assign(ejeValor(t, moneyShort), { beginAtZero: true, grace: '10%' }),
            yAxis: ejeCategoriaY(t, datos.map(d => d.cliente), 150),
            series: [{
                type: 'bar',
                barMaxWidth: 18,
                itemStyle: { color: relleno(t.critical, true), borderRadius: [0, 4, 4, 0] },
                showBackground: true,
                backgroundStyle: { color: t.rail, borderRadius: [0, 4, 4, 0] },
                label: {
                    show: false, position: 'right', distance: 8,
                    color: t.ink2, fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                    formatter: p => moneyShort(p.value)
                },
                animationDelay: i => i * 45,
                data: datos.map((d, i) => ({ value: d.saldo, label: { show: i === 0 } }))
            }]
        });
    }
    D.registrar({ id: 'cobertura', area: 'cobranza', render: renderCobertura });
    D.registrar({ id: 'aging', area: 'cobranza', render: renderAging });
    D.registrar({ id: 'pagos', area: 'cobranza', render: renderPagos });
    D.registrar({ id: 'vence', area: 'cobranza', render: renderVence });
    D.registrar({ id: 'riesgo', area: 'cobranza', render: renderRiesgo });
})(window.Dashboard);
