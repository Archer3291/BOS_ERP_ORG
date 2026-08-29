/* ──────────────────────────────────────────────────────────────────────────
   Tablero del Home — área de VENTAS (facturación del día y del periodo).
   Se registra en el núcleo (dashboard/core.js); el núcleo decide cuándo
   pedir los datos del área y cuándo redibujar.
   ────────────────────────────────────────────────────────────────────────── */
(function (D) {
    'use strict';

    const {
        rows, rowsAll, num, text, trunc, esc, fecha, aFecha, claveDia, el, estado, ALL,
        money, moneyShort, units, unitsShort, fmtInt, fmtDec1, tokens, tooltipBase,
        filaTooltip, tituloTooltip, etiquetaEje, ejeValor, ejeCategoriaY, rejillaBarras,
        relleno, alpha, FONT, FONT_NUM, make, soltar, tabla, vacio, altoPorFilas,
        view
    } = D;

    // 0 · Tendencia de 30 días (dos medidas del mismo tipo → dos líneas, un eje)
    function renderTendencia(t) {
        const dias = new Map();
        rows('tendencia').forEach(r => {
            const d = aFecha(r.dia);
            if (!d) return;
            const clave = claveDia(d);
            if (!dias.has(clave)) dias.set(clave, { fecha: new Date(d), facturado: 0, cobrado: 0, facturas: 0 });
            const it = dias.get(clave);
            it.facturado += num(r.facturado);
            it.cobrado += num(r.cobrado);
            it.facturas += num(r.facturas);
        });

        // se rellenan los días sin movimiento: si no, la línea se salta huecos
        const claves = Array.from(dias.keys()).sort();
        const datos = [];
        if (claves.length) {
            const cursor = new Date(claves[0] + 'T00:00:00');
            const fin = new Date(claves[claves.length - 1] + 'T00:00:00');
            while (cursor <= fin) {
                const clave = claveDia(cursor);
                datos.push(dias.get(clave) || {
                    fecha: new Date(cursor), facturado: 0, cobrado: 0, facturas: 0
                });
                cursor.setDate(cursor.getDate() + 1);
            }
        }

        const dm = d => d.getDate() + '/' + (d.getMonth() + 1);

        tabla('tendencia', [
            { label: 'Día', value: d => fecha(d.fecha) },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.facturas) },
            { label: 'Facturado', num: true, value: d => money(d.facturado) },
            { label: 'Cobrado', num: true, value: d => money(d.cobrado) }
        ], datos.slice().reverse(), 'Sin movimiento en los últimos 30 días');

        if (!vacio('tendencia', datos.length, 'Sin facturación ni cobros en los últimos 30 días')) {
            soltar('chartTendencia');
            return;
        }

        make('tendencia', 'chartTendencia', {
            animationDuration: 700,
            grid: { top: 44, left: 66, right: 84, bottom: 30, containLabel: false },
            legend: {
                top: 6, right: 0, icon: 'circle', itemWidth: 8, itemHeight: 8, itemGap: 18,
                textStyle: { color: t.ink2, fontFamily: FONT, fontSize: 11 }
            },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'line', lineStyle: { color: t.grid, width: 1 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    return tituloTooltip(fecha(d.fecha) + ' · ' + fmtInt.format(d.facturas) + ' fact.') +
                        params.map(p => filaTooltip(p.marker, p.seriesName,
                            money(p.seriesName === 'Cobrado' ? d.cobrado : d.facturado))).join('');
                }
            }),
            xAxis: {
                type: 'category',
                boundaryGap: false,
                data: datos.map(d => dm(d.fecha)),
                axisLine: { lineStyle: { color: t.grid } },
                axisTick: { show: false },
                axisLabel: {
                    color: t.muted, fontFamily: FONT_NUM, fontSize: 10.5,
                    interval: datos.length > 20 ? 2 : (datos.length > 12 ? 1 : 0)
                }
            },
            yAxis: ejeValor(t, moneyShort),
            series: [
                {
                    name: 'Facturado', type: 'line', smooth: 0.25, smoothMonotone: 'x',
                    showSymbol: false, symbolSize: 8,
                    itemStyle: { color: t.s1, borderColor: t.surface, borderWidth: 2 },
                    lineStyle: { width: 2, color: t.s1 },
                    // el valor del último día va rotulado al final de la línea
                    endLabel: {
                        show: true, distance: 6, color: t.ink2,
                        fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                        formatter: p => moneyShort(p.value)
                    },
                    data: datos.map(d => d.facturado)
                },
                {
                    name: 'Cobrado', type: 'line', smooth: 0.25, smoothMonotone: 'x',
                    showSymbol: false, symbolSize: 8,
                    itemStyle: { color: t.s3, borderColor: t.surface, borderWidth: 2 },
                    lineStyle: { width: 2, color: t.s3 },
                    endLabel: {
                        show: true, distance: 6, color: t.ink2,
                        fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                        formatter: p => moneyShort(p.value)
                    },
                    data: datos.map(d => d.cobrado)
                }
            ]
        });
    }

    // 1 · Ritmo de facturación por hora (serie de tiempo → línea con área)
    function renderHoras(t) {
        const cubos = new Map();
        rows('ventas').forEach(r => {
            const d = aFecha(r.fecha);
            if (!d) return;
            const h = d.getHours();
            if (!cubos.has(h)) cubos.set(h, { hora: h, monto: 0, docs: 0 });
            const c = cubos.get(h);
            c.monto += num(r.total);
            c.docs++;
        });

        // se rellenan las horas intermedias sin ventas para no mentir con la pendiente
        const conDatos = Array.from(cubos.keys()).sort((a, b) => a - b);
        const datos = [];
        if (conDatos.length) {
            for (let h = conDatos[0]; h <= conDatos[conDatos.length - 1]; h++) {
                datos.push(cubos.get(h) || { hora: h, monto: 0, docs: 0 });
            }
        }
        let acum = 0;
        datos.forEach(d => { acum += d.monto; d.acumulado = acum; });

        const hhmm = h => String(h).padStart(2, '0') + ':00';

        tabla('horas', [
            { label: 'Hora', value: d => hhmm(d.hora) + ' – ' + hhmm((d.hora + 1) % 24) },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.docs) },
            { label: 'Importe', num: true, value: d => money(d.monto) },
            { label: 'Acumulado del día', num: true, value: d => money(d.acumulado) }
        ], datos, 'Hoy no se han emitido facturas');

        if (!vacio('horas', datos.length, 'Hoy no se han emitido facturas')) {
            soltar('chartHoras');
            return;
        }

        let pico = 0;
        datos.forEach((d, i) => { if (d.monto > datos[pico].monto) pico = i; });

        make('horas', 'chartHoras', {
            animationDuration: 600,
            grid: { top: 22, left: 66, right: 24, bottom: 30, containLabel: false },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'line', lineStyle: { color: t.grid, width: 1 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    return tituloTooltip(hhmm(d.hora) + ' – ' + hhmm((d.hora + 1) % 24)) +
                        filaTooltip(params[0].marker, fmtInt.format(d.docs) + ' factura' + (d.docs === 1 ? '' : 's'), money(d.monto)) +
                        filaTooltip('', 'Acumulado del día', money(d.acumulado));
                }
            }),
            xAxis: {
                type: 'category',
                boundaryGap: false,
                data: datos.map(d => hhmm(d.hora)),
                axisLine: { lineStyle: { color: t.grid } },
                axisTick: { show: false },
                axisLabel: { color: t.muted, fontFamily: FONT_NUM, fontSize: 10.5 }
            },
            yAxis: ejeValor(t, moneyShort),
            series: [{
                name: 'Facturado',
                type: 'line',
                smooth: 0.3,
                smoothMonotone: 'x',   // sin rebotes por debajo del dato real
                symbol: 'circle',
                symbolSize: 8,
                showSymbol: datos.length <= 16,
                // anillo del color de la superficie: el punto se lee sobre la línea
                itemStyle: { color: t.s1, borderColor: t.surface, borderWidth: 2 },
                lineStyle: { width: 2, color: t.s1 },
                areaStyle: {
                    color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                        { offset: 0, color: alpha(t.s1, 0.26) },
                        { offset: 1, color: alpha(t.s1, 0) }
                    ])
                },
                label: {
                    show: false, distance: 10,
                    // en los extremos la etiqueta se sale del área: se recuesta
                    position: pico === 0 ? 'right' : (pico === datos.length - 1 ? 'left' : 'top'),
                    color: t.ink2, fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                    formatter: p => moneyShort(p.value)
                },
                data: datos.map((d, i) => ({ value: d.monto, label: { show: i === pico } }))
            }]
        });
    }

    // 4 · Facturado vs cobrado por sucursal (dos series → leyenda + tabla)
    function renderSucursales(t) {
        const mapa = new Map();
        const upsert = nombre => {
            const key = nombre || 'Sin sucursal';
            if (!mapa.has(key)) mapa.set(key, { sucursal: key, facturado: 0, facturas: 0, cobrado: 0, cobros: 0 });
            return mapa.get(key);
        };

        rows('totales').forEach(r => {
            const it = upsert(text(r.descripcion));
            it.facturado += num(r.monto_total);
            it.facturas += num(r.total_facturas);
        });
        rows('cobros').forEach(r => {
            const it = upsert(text(r.descripcion));
            it.cobrado += num(r.total_cobrado);
            it.cobros += num(r.total_cobros);
        });

        const datos = Array.from(mapa.values()).sort((a, b) => b.facturado - a.facturado);

        tabla('sucursal', [
            { label: 'Sucursal', value: d => d.sucursal },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.facturas) },
            { label: 'Facturado', num: true, value: d => money(d.facturado) },
            { label: 'Cobros', num: true, value: d => fmtInt.format(d.cobros) },
            { label: 'Cobrado', num: true, value: d => money(d.cobrado) }
        ], datos, 'Sin movimiento hoy');

        if (!vacio('sucursal', datos.length, 'Hoy no hay facturación ni cobros registrados')) {
            soltar('chartSucursal');
            return;
        }

        make('sucursal', 'chartSucursal', {
            animationDuration: 400,
            grid: { top: 44, left: 66, right: 10, bottom: 30, containLabel: false },
            legend: {
                top: 6, right: 0, icon: 'circle', itemWidth: 8, itemHeight: 8, itemGap: 18,
                textStyle: { color: t.ink2, fontFamily: FONT, fontSize: 11 }
            },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'shadow', shadowStyle: { color: t.grid, opacity: 0.35 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    return tituloTooltip(d.sucursal) +
                        filaTooltip(params[0].marker, 'Facturado · ' + fmtInt.format(d.facturas) + ' fact.', money(d.facturado)) +
                        filaTooltip(params[1].marker, 'Cobrado · ' + fmtInt.format(d.cobros) + ' cobros', money(d.cobrado));
                }
            }),
            xAxis: {
                type: 'category',
                data: datos.map(d => d.sucursal),
                axisLine: { lineStyle: { color: t.grid } },
                axisTick: { show: false },
                axisLabel: Object.assign(etiquetaEje(t), {
                    interval: 0, width: 130, overflow: 'truncate', ellipsis: '…'
                })
            },
            yAxis: ejeValor(t, moneyShort),
            series: [
                {
                    name: 'Facturado', type: 'bar',
                    data: datos.map(d => d.facturado),
                    itemStyle: { color: relleno(t.s1), borderRadius: [4, 4, 0, 0] },
                    barMaxWidth: 24, barGap: '25%', barCategoryGap: '50%',
                    animationDelay: i => i * 60
                },
                {
                    name: 'Cobrado', type: 'bar',
                    data: datos.map(d => d.cobrado),
                    itemStyle: { color: relleno(t.s3), borderRadius: [4, 4, 0, 0] },
                    barMaxWidth: 24,
                    animationDelay: i => i * 60 + 30
                }
            ]
        });
    }

    // 3 · Participación por sucursal (parte-todo con pocas categorías → dona)
    function renderReparto(t) {
        // El reparto es ENTRE sucursales, así que siempre se calcula con todas;
        // si hay filtro, la elegida se resalta y las demás se atenúan.
        const mapa = new Map();
        (estado.data.totales || []).forEach(r => {
            const s = text(r.descripcion) || 'Sin sucursal';
            if (!mapa.has(s)) mapa.set(s, { nombre: s, monto: 0, docs: 0 });
            const it = mapa.get(s);
            it.monto += num(r.monto_total);
            it.docs += num(r.total_facturas);
        });

        let datos = Array.from(mapa.values()).sort((a, b) => b.monto - a.monto);
        if (datos.length > 6) {   // la cola se agrupa: más de 6 tonos ya no se distinguen
            const resto = datos.slice(5);
            datos = datos.slice(0, 5).concat([{
                nombre: 'Otras sucursales',
                monto: resto.reduce((s, d) => s + d.monto, 0),
                docs: resto.reduce((s, d) => s + d.docs, 0),
                agrupado: true
            }]);
        }

        const paleta = [t.s1, t.s2, t.s3, t.s4, t.s5, t.s6];
        datos.forEach((d, i) => { d.color = d.agrupado ? t.muted : paleta[i % paleta.length]; });

        const total = datos.reduce((s, d) => s + d.monto, 0);
        const legend = el('#repartoLegend');

        tabla('reparto', [
            { label: 'Sucursal', value: d => d.nombre },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.docs) },
            { label: 'Facturado', num: true, value: d => money(d.monto) },
            { label: 'Participación', num: true, value: d => total ? fmtDec1.format(d.monto / total * 100) + '%' : '—' }
        ], datos, 'Sin facturación hoy');

        if (!vacio('reparto', total > 0, 'Hoy no se ha facturado en ninguna sucursal')) {
            legend.innerHTML = '';
            soltar('chartReparto');
            return;
        }

        const filtrada = estado.sucursal !== ALL ? estado.sucursal : null;
        const elegida = filtrada ? datos.find(d => d.nombre === filtrada) : null;

        legend.innerHTML = datos.map(d =>
            '<div class="dash-legend-row' + (elegida && d === elegida ? ' is-selected' : '') + '">' +
            '<span class="dash-dot" style="background:' + d.color + '"></span>' +
            '<span class="dash-legend-name">' + esc(d.nombre) + '</span>' +
            '<span class="dash-legend-val">' + money(d.monto) + '</span>' +
            '<span class="dash-legend-pct">' + fmtDec1.format(d.monto / total * 100) + '%</span>' +
            '</div>').join('');

        const centroValor = elegida ? fmtDec1.format(elegida.monto / total * 100) + '%' : moneyShort(total);
        const centroTexto = elegida ? trunc(elegida.nombre, 22) : 'facturado hoy';

        make('reparto', 'chartReparto', {
            animationDuration: 600,
            title: {
                text: centroValor,
                subtext: centroTexto,
                left: '50%', top: '38%',
                textAlign: 'center',
                textStyle: { fontFamily: FONT, fontSize: 22, fontWeight: 700, color: t.ink },
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
                avoidLabelOverlap: true,
                label: { show: false },
                labelLine: { show: false },
                itemStyle: {
                    // separador del color de la superficie entre rebanadas
                    borderColor: t.surface, borderWidth: 2, borderRadius: 4
                },
                emphasis: { scaleSize: 6 },
                data: datos.map(d => ({
                    name: d.nombre,
                    value: d.monto,
                    itemStyle: {
                        color: d.color,
                        opacity: elegida && d !== elegida ? 0.3 : 1
                    }
                }))
            }]
        });
    }

    // 5 · Mezcla contado / crédito (parte-todo → barra apilada + etiquetas HTML)
    function renderMezcla(t) {
        let contado = 0, credito = 0, nContado = 0, nCredito = 0;
        rows('ventas').forEach(r => {
            if (text(r.tipo).toUpperCase() === 'CONTADO') { contado += num(r.total); nContado++; }
            else { credito += num(r.total); nCredito++; }
        });

        const total = contado + credito;
        const items = [
            { nombre: 'Contado', color: t.s1, monto: contado, docs: nContado },
            { nombre: 'Crédito', color: t.s2, monto: credito, docs: nCredito }
        ];

        tabla('mezcla', [
            { label: 'Tipo', value: d => d.nombre },
            { label: 'Facturas', num: true, value: d => fmtInt.format(d.docs) },
            { label: 'Importe', num: true, value: d => money(d.monto) },
            { label: 'Participación', num: true, value: d => total ? fmtDec1.format(d.monto / total * 100) + '%' : '—' }
        ], items, 'Sin facturas hoy');

        const legend = el('#mezclaLegend');
        if (!vacio('mezcla', total > 0, 'Hoy no se ha facturado')) {
            legend.innerHTML = '';
            soltar('chartMezcla');
            return;
        }

        // Etiqueta directa fuera de la barra: nada queda sólo en el tooltip.
        legend.innerHTML = items.map(i =>
            '<div class="dash-legend-row">' +
            '<span class="dash-dot" style="background:' + i.color + '"></span>' +
            '<span class="dash-legend-name">' + i.nombre + ' · ' + fmtInt.format(i.docs) + ' fact.</span>' +
            '<span class="dash-legend-val">' + money(i.monto) + '</span>' +
            '<span class="dash-legend-pct">' + fmtDec1.format(i.monto / total * 100) + '%</span>' +
            '</div>').join('');

        make('mezcla', 'chartMezcla', {
            animationDuration: 400,
            grid: { top: 10, bottom: 10, left: 2, right: 2 },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const i = items[p.seriesIndex];
                    return tituloTooltip('Facturado hoy') +
                        filaTooltip(p.marker, i.nombre + ' · ' + fmtInt.format(i.docs) + ' fact.',
                            money(i.monto) + ' (' + fmtDec1.format(i.monto / total * 100) + '%)');
                }
            }),
            xAxis: { type: 'value', show: false, min: 0, max: total },
            yAxis: { type: 'category', show: false, data: ['Hoy'] },
            series: items.map(i => ({
                name: i.nombre, type: 'bar', stack: 'mezcla', data: [i.monto], barWidth: 26,
                itemStyle: {
                    color: relleno(i.color, true), borderRadius: 6,
                    // separador del color de la superficie entre segmentos
                    borderColor: t.surface, borderWidth: 1
                }
            }))
        });
    }

    // 6 y 7 · Rankings de productos y clientes (una serie → sin leyenda)
    function renderRanking(nombre, t) {
        const esProducto = nombre === 'productos';
        const modo = estado.rank[nombre];
        const porImporte = esProducto && estado.metric.productos === 'importe';
        const enUnidades = esProducto && !porImporte;

        let filas;
        if (!esProducto) {
            filas = rows(modo === 'top' ? 'cliTop' : 'cliBottom');
        } else if (porImporte) {
            // el mismo endpoint trae los dos extremos, marcados en "orden"
            const orden = modo === 'top' ? 'mas' : 'menos';
            filas = rows('prodImporte').filter(r => text(r.orden) === orden);
        } else {
            filas = rows(modo === 'top' ? 'prodTop' : 'prodBottom');
        }

        if (esProducto) {
            el('#productosTitulo').textContent = porImporte
                ? 'Productos por importe facturado'
                : 'Productos por unidades vendidas';
        }

        // El SQL agrupa por sucursal: con "Todas" se reagrupa por entidad para
        // no repetir el mismo producto/cliente una vez por sucursal.
        const mapa = new Map();
        filas.forEach(r => {
            const key = esProducto
                ? (text(r.cve_prod) || text(r.descr_prod))
                : text(r.rsocliente);
            if (!key) return;
            if (!mapa.has(key)) {
                mapa.set(key, {
                    key,
                    nombre: esProducto ? (text(r.descr_prod) || key) : key,
                    ud: text(r.ud),
                    total: 0,
                    unidades: 0,
                    sucursales: new Set()
                });
            }
            const it = mapa.get(key);
            it.total += num(porImporte ? r.importe : r.total);
            it.unidades += num(porImporte ? r.unidades : r.total);
            if (text(r.descripcion)) it.sucursales.add(text(r.descripcion));
        });

        const datos = Array.from(mapa.values())
            .sort((a, b) => modo === 'top' ? b.total - a.total : a.total - b.total)
            .slice(0, 10);

        const colsProducto = [
            { label: 'Clave', value: d => d.key },
            { label: 'Producto', value: d => d.nombre },
            { label: 'Unidad', value: d => d.ud || '—' },
            { label: 'Sucursales', value: d => Array.from(d.sucursales).join(', ') || '—' }
        ];

        tabla(nombre, esProducto
            ? colsProducto.concat(porImporte
                ? [
                    { label: 'Unidades', num: true, value: d => units(d.unidades) },
                    { label: 'Importe', num: true, value: d => money(d.total) }
                ]
                : [{ label: 'Unidades', num: true, value: d => units(d.total) }])
            : [
                { label: 'Cliente', value: d => d.nombre },
                { label: 'Sucursales', value: d => Array.from(d.sucursales).join(', ') || '—' },
                { label: 'Facturado', num: true, value: d => money(d.total) }
            ], datos, 'Sin movimiento en el periodo');

        const id = esProducto ? 'chartProductos' : 'chartClientes';

        if (!vacio(nombre, datos.length, 'Sin ventas en los últimos 60 días')) {
            soltar(id);
            return;
        }

        const fmt = enUnidades ? unitsShort : moneyShort;
        altoPorFilas(nombre, datos.length);

        // Etiqueta directa sólo en la barra mayor: el resto lo cargan el eje,
        // el tooltip y la tabla gemela.
        let mayor = 0;
        datos.forEach((d, i) => { if (Math.abs(d.total) > Math.abs(datos[mayor].total)) mayor = i; });

        make(nombre, id, {
            animationDuration: 400,
            grid: rejillaBarras(150, 64),
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const d = datos[p.dataIndex];
                    const sucs = Array.from(d.sucursales).join(', ');
                    let cuerpo;
                    if (!esProducto) {
                        cuerpo = filaTooltip(p.marker, 'Facturado', money(d.total));
                    } else if (porImporte) {
                        cuerpo = filaTooltip(p.marker, 'Clave ' + d.key, money(d.total)) +
                            filaTooltip('', 'Unidades', units(d.unidades) + ' ' + (d.ud || 'ud'));
                    } else {
                        cuerpo = filaTooltip(p.marker, 'Clave ' + d.key,
                            units(d.total) + ' ' + (d.ud || 'ud'));
                    }
                    return tituloTooltip(d.nombre) + cuerpo +
                        (sucs ? '<div style="opacity:.7;margin-top:4px;max-width:280px;white-space:normal;">' +
                            esc(sucs) + '</div>' : '');
                }
            }),
            xAxis: ejeValor(t, fmt),
            yAxis: ejeCategoriaY(t, datos.map(d => d.nombre), 150),
            series: [{
                type: 'bar',
                barMaxWidth: 18,
                itemStyle: { color: relleno(t.s1, true), borderRadius: [0, 4, 4, 0] },
                showBackground: true,
                backgroundStyle: { color: t.rail, borderRadius: [0, 4, 4, 0] },
                label: {
                    show: false, position: 'right', distance: 8,
                    color: t.ink2, fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                    formatter: p => fmt(p.value)
                },
                animationDelay: i => i * 45,
                data: datos.map((d, i) => ({ value: d.total, label: { show: i === mayor } }))
            }]
        });
    }

    // 7b · Mes en curso contra mes anterior (mismo dato, dos periodos →
    //      un tono con la referencia en gris: la comparación es el mensaje)
    function renderMeses(t) {
        const mesesSet = new Set();
        const mapa = new Map();

        rows('meses').forEach(r => {
            const d = aFecha(r.mes);
            if (!d) return;
            const clave = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0');
            mesesSet.add(clave);
            const s = text(r.descripcion) || 'Sin sucursal';
            if (!mapa.has(s)) mapa.set(s, { sucursal: s, monto: {}, docs: {} });
            const it = mapa.get(s);
            it.monto[clave] = (it.monto[clave] || 0) + num(r.monto);
            it.docs[clave] = (it.docs[clave] || 0) + num(r.facturas);
        });

        const claves = Array.from(mesesSet).sort();
        const actual = claves[claves.length - 1];
        const previo = claves.length > 1 ? claves[claves.length - 2] : null;

        const nombreMes = clave => {
            if (!clave) return '';
            const [a, m] = clave.split('-');
            const txt = new Intl.DateTimeFormat('es-MX', { month: 'long' }).format(new Date(+a, +m - 1, 1));
            return txt.charAt(0).toUpperCase() + txt.slice(1);
        };

        const datos = Array.from(mapa.values())
            .map(d => {
                d.actual = d.monto[actual] || 0;
                d.previo = previo ? (d.monto[previo] || 0) : 0;
                d.delta = d.previo ? (d.actual - d.previo) / d.previo * 100 : null;
                return d;
            })
            .sort((a, b) => b.actual - a.actual);

        tabla('meses', [
            { label: 'Sucursal', value: d => d.sucursal },
            { label: nombreMes(previo) || 'Mes anterior', num: true, value: d => previo ? money(d.previo) : '—' },
            { label: nombreMes(actual) || 'Mes en curso', num: true, value: d => money(d.actual) },
            {
                label: 'Variación', num: true,
                value: d => d.delta === null ? '—' : (d.delta >= 0 ? '+' : '') + fmtDec1.format(d.delta) + '%'
            }
        ], datos, 'Sin facturación en el periodo');

        if (!vacio('meses', datos.length && actual, 'Sin facturación este mes ni el anterior')) {
            soltar('chartMeses');
            return;
        }

        const series = [];
        if (previo) {
            series.push({
                name: nombreMes(previo), type: 'bar',
                data: datos.map(d => d.previo),
                // el mes anterior es contexto: va en gris para que resalte el actual
                itemStyle: { color: relleno(t.muted), borderRadius: [4, 4, 0, 0] },
                barMaxWidth: 24, barGap: '25%', barCategoryGap: '50%',
                animationDelay: i => i * 60
            });
        }
        series.push({
            name: nombreMes(actual), type: 'bar',
            data: datos.map(d => d.actual),
            itemStyle: { color: relleno(t.s1), borderRadius: [4, 4, 0, 0] },
            barMaxWidth: 24, barGap: '25%', barCategoryGap: '50%',
            animationDelay: i => i * 60 + 30
        });

        make('meses', 'chartMeses', {
            animationDuration: 500,
            grid: { top: 44, left: 66, right: 10, bottom: 30, containLabel: false },
            legend: {
                top: 6, right: 0, icon: 'circle', itemWidth: 8, itemHeight: 8, itemGap: 18,
                textStyle: { color: t.ink2, fontFamily: FONT, fontSize: 11 }
            },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'shadow', shadowStyle: { color: t.grid, opacity: 0.35 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    const delta = d.delta === null ? 'sin comparativo'
                        : (d.delta >= 0 ? '+' : '') + fmtDec1.format(d.delta) + '% contra el mes anterior';
                    return tituloTooltip(d.sucursal) +
                        params.map(p => filaTooltip(p.marker, p.seriesName, money(p.value))).join('') +
                        '<div style="opacity:.7;margin-top:5px;">' + esc(delta) + '</div>';
                }
            }),
            xAxis: {
                type: 'category',
                data: datos.map(d => d.sucursal),
                axisLine: { lineStyle: { color: t.grid } },
                axisTick: { show: false },
                axisLabel: Object.assign(etiquetaEje(t), {
                    interval: 0, width: 120, overflow: 'truncate', ellipsis: '…'
                })
            },
            yAxis: ejeValor(t, moneyShort),
            series: series
        });
    }

    // 9 · Detalle del día (la forma correcta para este dato es una tabla)
    function renderFacturas() {
        const datos = rows('ventas').slice().sort((a, b) => num(b.total) - num(a.total));

        tabla('facturas', [
            { label: 'Folio', value: d => text(d.folio) || '—' },
            { label: 'Cliente', value: d => text(d.rsocliente) || '—' },
            { label: 'Sucursal', value: d => text(d.descripcion) || '—' },
            {
                label: 'Tipo', html: true,
                value: d => {
                    const tipo = text(d.tipo).toUpperCase();
                    const clase = tipo === 'CONTADO' ? ' is-contado' : (tipo === 'CREDITO' ? ' is-credito' : '');
                    return '<span class="dash-tag' + clase + '">' + esc(tipo) + '</span>';
                }
            },
            { label: 'Hora', value: d => fecha(d.fecha, true) },
            { label: 'Moneda', value: d => text(d.moneda) || '—' },
            { label: 'Total', num: true, value: d => money(d.total) }
        ], datos, 'Hoy no se han emitido facturas');
    }
    D.registrar({ id: 'tendencia', area: 'ventas', render: renderTendencia });
    D.registrar({ id: 'horas', area: 'ventas', render: renderHoras });
    D.registrar({ id: 'sucursal', area: 'ventas', render: renderSucursales });
    D.registrar({ id: 'reparto', area: 'ventas', render: renderReparto });
    D.registrar({ id: 'mezcla', area: 'ventas', render: renderMezcla });
    D.registrar({ id: 'productos', area: 'ventas', render: t => renderRanking('productos', t) });
    D.registrar({ id: 'clientes', area: 'ventas', render: t => renderRanking('clientes', t) });
    D.registrar({ id: 'meses', area: 'ventas', render: renderMeses });
    D.registrar({ id: 'facturas', area: 'ventas', render: renderFacturas });
})(window.Dashboard);
