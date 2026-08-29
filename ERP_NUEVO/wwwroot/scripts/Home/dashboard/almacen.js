/* ──────────────────────────────────────────────────────────────────────────
   Tablero del Home — área de ALMACÉN (cortes de material sobre tarima).

   Consume /Home/TodosCortes (detalle) y /Home/CantidadCortes (agregado por
   producto). Ninguna de las dos trae sucursal, así que estas tarjetas leen
   con rowsAll: el filtro de sucursal de arriba no les aplica y el panel lo
   dice de frente.
   ────────────────────────────────────────────────────────────────────────── */
(function (D) {
    'use strict';

    const {
        rowsAll, num, text, esc, fecha, aFecha, claveDia, el,
        units, unitsShort, fmtInt, fmtDec1, tooltipBase, filaTooltip, tituloTooltip,
        etiquetaEje, ejeValor, ejeCategoriaY, rejillaBarras, relleno, alpha,
        FONT, FONT_NUM, make, soltar, tabla, vacio, altoPorFilas
    } = D;

    const metros = v => units(v) + ' m';
    const piezas = v => fmtInt.format(num(v)) + (num(v) === 1 ? ' corte' : ' cortes');

    // ── Resumen del área (no es tarjeta: sólo rellena los tres indicadores) ──
    function renderResumen() {
        let totalPiezas = 0, totalMetros = 0;
        const productos = new Set(), ubicaciones = new Set();

        rowsAll('cortes').forEach(r => {
            totalPiezas += num(r.cantidad_cortes);
            totalMetros += num(r.suma_cortes);
            if (text(r.cve_prod)) productos.add(text(r.cve_prod));
            if (text(r.ulocation)) ubicaciones.add(text(r.ulocation));
        });

        el('#kpiCortes').textContent = fmtInt.format(totalPiezas);
        el('#kpiCortesFoot').textContent = productos.size + ' producto' +
            (productos.size === 1 ? '' : 's') + ' con cortes';

        el('#kpiMetros').textContent = units(totalMetros) + ' m';
        el('#kpiMetrosFoot').textContent = totalPiezas
            ? 'Promedio de ' + units(totalMetros / totalPiezas) + ' m por corte'
            : 'Sin cortes registrados';

        el('#kpiUbicaciones').textContent = fmtInt.format(ubicaciones.size);
        el('#kpiUbicacionesFoot').textContent = 'Ubicaciones con material cortado';
    }

    // ── 1 · Metros cortados por producto (magnitud → barras horizontales) ──
    function renderCortesProducto(t) {
        const mapa = new Map();
        rowsAll('cortesProd').forEach(r => {
            const clave = text(r.cve_prod) || text(r.descr_prod);
            if (!clave) return;
            if (!mapa.has(clave)) {
                mapa.set(clave, {
                    clave, nombre: text(r.descr_prod) || clave,
                    metros: 0, cortes: 0, tarima: 0
                });
            }
            const it = mapa.get(clave);
            it.metros += num(r.longitud);          // SUM(longitud * cantidad) del query
            it.cortes += num(r.cantidad_cortes);
            it.tarima += num(r.metros_totales);
        });

        const todos = Array.from(mapa.values()).sort((a, b) => b.metros - a.metros);
        const datos = todos.slice(0, 10);

        tabla('cortesProducto', [
            { label: 'Clave', value: d => d.clave },
            { label: 'Producto', value: d => d.nombre },
            { label: 'Cortes', num: true, value: d => fmtInt.format(d.cortes) },
            { label: 'Metros cortados', num: true, value: d => metros(d.metros) },
            { label: 'Metros totales', num: true, value: d => units(d.tarima) }
        ], todos, 'Sin cortes registrados');

        if (!vacio('cortesProducto', datos.length, 'Todavía no hay cortes registrados')) {
            soltar('chartCortesProducto');
            return;
        }

        altoPorFilas('cortesProducto', datos.length);

        let mayor = 0;
        datos.forEach((d, i) => { if (d.metros > datos[mayor].metros) mayor = i; });

        make('cortesProducto', 'chartCortesProducto', {
            animationDuration: 500,
            grid: rejillaBarras(150, 64),
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const d = datos[p.dataIndex];
                    return tituloTooltip(d.nombre) +
                        filaTooltip(p.marker, 'Clave ' + d.clave, metros(d.metros)) +
                        filaTooltip('', 'Cortes', fmtInt.format(d.cortes));
                }
            }),
            xAxis: Object.assign(ejeValor(t, unitsShort), { grace: '10%' }),
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
                    formatter: p => unitsShort(p.value) + ' m'
                },
                animationDelay: i => i * 45,
                data: datos.map((d, i) => ({ value: d.metros, label: { show: i === mayor } }))
            }]
        });
    }

    // ── 2 · Cortes por medida (categorías ordenadas por longitud) ──
    function renderCortesMedida(t) {
        const mapa = new Map();
        rowsAll('cortes').forEach(r => {
            const largo = num(r.longitud);
            if (!largo) return;
            const clave = largo.toFixed(2);
            if (!mapa.has(clave)) mapa.set(clave, { largo, cortes: 0, metros: 0, productos: new Set() });
            const it = mapa.get(clave);
            it.cortes += num(r.cantidad_cortes);
            it.metros += num(r.suma_cortes);
            if (text(r.cve_prod)) it.productos.add(text(r.cve_prod));
        });

        // si hay muchas medidas se quedan las 14 con más piezas, y se vuelven
        // a ordenar por longitud: el eje tiene que leerse de corto a largo
        let datos = Array.from(mapa.values());
        if (datos.length > 14) {
            datos = datos.sort((a, b) => b.cortes - a.cortes).slice(0, 14);
        }
        datos.sort((a, b) => a.largo - b.largo);

        tabla('cortesMedida', [
            { label: 'Medida', value: d => units(d.largo) + ' m' },
            { label: 'Cortes', num: true, value: d => fmtInt.format(d.cortes) },
            { label: 'Metros', num: true, value: d => metros(d.metros) },
            { label: 'Productos', num: true, value: d => fmtInt.format(d.productos.size) }
        ], Array.from(mapa.values()).sort((a, b) => a.largo - b.largo), 'Sin cortes registrados');

        if (!vacio('cortesMedida', datos.length, 'Todavía no hay cortes registrados')) {
            soltar('chartCortesMedida');
            return;
        }

        let mayor = 0;
        datos.forEach((d, i) => { if (d.cortes > datos[mayor].cortes) mayor = i; });

        make('cortesMedida', 'chartCortesMedida', {
            animationDuration: 500,
            grid: { top: 24, left: 56, right: 16, bottom: 30, containLabel: false },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'shadow', shadowStyle: { color: t.grid, opacity: 0.35 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    return tituloTooltip('Medida de ' + units(d.largo) + ' m') +
                        filaTooltip(params[0].marker, 'Cortes', fmtInt.format(d.cortes)) +
                        filaTooltip('', 'Metros', metros(d.metros)) +
                        filaTooltip('', 'Productos', fmtInt.format(d.productos.size));
                }
            }),
            xAxis: {
                type: 'category',
                data: datos.map(d => units(d.largo) + ' m'),
                axisLine: { lineStyle: { color: t.grid } },
                axisTick: { show: false },
                axisLabel: { color: t.muted, fontFamily: FONT_NUM, fontSize: 10.5 }
            },
            yAxis: ejeValor(t, unitsShort),
            series: [{
                type: 'bar',
                barMaxWidth: 24,
                barCategoryGap: '35%',
                itemStyle: { color: relleno(t.s1), borderRadius: [4, 4, 0, 0] },
                label: {
                    show: false, position: 'top', distance: 8,
                    color: t.ink2, fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                    formatter: p => fmtInt.format(p.value)
                },
                animationDelay: i => i * 40,
                data: datos.map((d, i) => ({ value: d.cortes, label: { show: i === mayor } }))
            }]
        });
    }

    // ── 3 · Cortes por ubicación (parte-todo con pocas categorías → dona) ──
    function renderCortesUbicacion(t) {
        const mapa = new Map();
        rowsAll('cortes').forEach(r => {
            const donde = text(r.ulocation) || 'Sin ubicación';
            if (!mapa.has(donde)) mapa.set(donde, { nombre: donde, cortes: 0, metros: 0 });
            const it = mapa.get(donde);
            it.cortes += num(r.cantidad_cortes);
            it.metros += num(r.suma_cortes);
        });

        let datos = Array.from(mapa.values()).sort((a, b) => b.cortes - a.cortes);
        const completo = datos.slice();
        if (datos.length > 6) {
            const resto = datos.slice(5);
            datos = datos.slice(0, 5).concat([{
                nombre: 'Otras ubicaciones',
                cortes: resto.reduce((s, d) => s + d.cortes, 0),
                metros: resto.reduce((s, d) => s + d.metros, 0),
                agrupado: true
            }]);
        }

        const paleta = [t.s1, t.s2, t.s3, t.s4, t.s5, t.s6];
        datos.forEach((d, i) => { d.color = d.agrupado ? t.muted : paleta[i % paleta.length]; });

        const total = datos.reduce((s, d) => s + d.cortes, 0);
        const legend = el('#ubicacionLegend');

        tabla('cortesUbicacion', [
            { label: 'Ubicación', value: d => d.nombre },
            { label: 'Cortes', num: true, value: d => fmtInt.format(d.cortes) },
            { label: 'Metros', num: true, value: d => metros(d.metros) },
            { label: 'Participación', num: true, value: d => total ? fmtDec1.format(d.cortes / total * 100) + '%' : '—' }
        ], completo, 'Sin cortes registrados');

        if (!vacio('cortesUbicacion', total > 0, 'Todavía no hay cortes registrados')) {
            legend.innerHTML = '';
            soltar('chartCortesUbicacion');
            return;
        }

        legend.innerHTML = datos.map(d =>
            '<div class="dash-legend-row">' +
            '<span class="dash-dot" style="background:' + d.color + '"></span>' +
            '<span class="dash-legend-name">' + esc(d.nombre) + '</span>' +
            '<span class="dash-legend-val">' + fmtInt.format(d.cortes) + '</span>' +
            '<span class="dash-legend-pct">' + fmtDec1.format(d.cortes / total * 100) + '%</span>' +
            '</div>').join('');

        make('cortesUbicacion', 'chartCortesUbicacion', {
            animationDuration: 600,
            title: {
                text: fmtInt.format(total), subtext: 'cortes',
                left: '50%', top: '38%', textAlign: 'center',
                textStyle: { fontFamily: FONT, fontSize: 20, fontWeight: 700, color: t.ink },
                subtextStyle: { fontFamily: FONT, fontSize: 11, color: t.muted }
            },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'item',
                formatter: p => {
                    const d = datos[p.dataIndex];
                    return tituloTooltip(d.nombre) +
                        filaTooltip(p.marker, 'Cortes', fmtInt.format(d.cortes)) +
                        filaTooltip('', 'Metros', metros(d.metros)) +
                        filaTooltip('', 'Participación', fmtDec1.format(d.cortes / total * 100) + '%');
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
                data: datos.map(d => ({ name: d.nombre, value: d.cortes, itemStyle: { color: d.color } }))
            }]
        });
    }

    // ── 4 · Cortes por día (serie de tiempo → línea con área) ──
    function renderCortesDia(t) {
        const dias = new Map();
        rowsAll('cortes').forEach(r => {
            const d = aFecha(r.fecha_creacion);
            if (!d) return;
            const clave = claveDia(d);
            if (!dias.has(clave)) dias.set(clave, { fecha: new Date(d), cortes: 0, metros: 0 });
            const it = dias.get(clave);
            it.cortes += num(r.cantidad_cortes);
            it.metros += num(r.suma_cortes);
        });

        const claves = Array.from(dias.keys()).sort();
        // sólo los últimos 30 días con registro, rellenando los huecos
        const ultimos = claves.slice(-30);
        const datos = [];
        if (ultimos.length) {
            const cursor = new Date(ultimos[0] + 'T00:00:00');
            const fin = new Date(ultimos[ultimos.length - 1] + 'T00:00:00');
            while (cursor <= fin) {
                const clave = claveDia(cursor);
                datos.push(dias.get(clave) || { fecha: new Date(cursor), cortes: 0, metros: 0 });
                cursor.setDate(cursor.getDate() + 1);
            }
        }

        tabla('cortesDia', [
            { label: 'Día', value: d => fecha(d.fecha) },
            { label: 'Cortes', num: true, value: d => fmtInt.format(d.cortes) },
            { label: 'Metros', num: true, value: d => metros(d.metros) }
        ], datos.slice().reverse(), 'Sin cortes con fecha registrada');

        if (!vacio('cortesDia', datos.length, 'Sin cortes con fecha registrada')) {
            soltar('chartCortesDia');
            return;
        }

        let pico = 0;
        datos.forEach((d, i) => { if (d.cortes > datos[pico].cortes) pico = i; });
        const dm = d => d.getDate() + '/' + (d.getMonth() + 1);

        make('cortesDia', 'chartCortesDia', {
            animationDuration: 600,
            grid: { top: 24, left: 56, right: 28, bottom: 30, containLabel: false },
            tooltip: Object.assign(tooltipBase(t), {
                trigger: 'axis',
                axisPointer: { type: 'line', lineStyle: { color: t.grid, width: 1 } },
                formatter: params => {
                    const d = datos[params[0].dataIndex];
                    return tituloTooltip(fecha(d.fecha)) +
                        filaTooltip(params[0].marker, 'Cortes', fmtInt.format(d.cortes)) +
                        filaTooltip('', 'Metros', metros(d.metros));
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
            yAxis: ejeValor(t, unitsShort),
            series: [{
                name: 'Cortes', type: 'line', smooth: 0.3, smoothMonotone: 'x',
                symbol: 'circle', symbolSize: 8, showSymbol: datos.length <= 16,
                itemStyle: { color: t.s3, borderColor: t.surface, borderWidth: 2 },
                lineStyle: { width: 2, color: t.s3 },
                areaStyle: {
                    color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                        { offset: 0, color: alpha(t.s3, 0.26) },
                        { offset: 1, color: alpha(t.s3, 0) }
                    ])
                },
                label: {
                    show: false, distance: 10,
                    position: pico === 0 ? 'right' : (pico === datos.length - 1 ? 'left' : 'top'),
                    color: t.ink2, fontFamily: FONT_NUM, fontSize: 11, fontWeight: 600,
                    formatter: p => fmtInt.format(p.value)
                },
                data: datos.map((d, i) => ({ value: d.cortes, label: { show: i === pico } }))
            }]
        });
    }

    // ── 5 · Detalle (para este dato la forma correcta es una tabla) ──
    function renderCortesDetalle() {
        const datos = rowsAll('cortes').slice()
            .sort((a, b) => num(b.suma_cortes) - num(a.suma_cortes));

        tabla('cortesDetalle', [
            { label: 'Folio', value: d => text(d.folio) || '—' },
            { label: 'Clave', value: d => text(d.cve_prod) || '—' },
            { label: 'Producto', value: d => text(d.descr_prod) || '—' },
            { label: 'Ubicación', value: d => text(d.ulocation) || '—' },
            { label: 'Medida', num: true, value: d => units(d.longitud) + ' m' },
            { label: 'Cortes', num: true, value: d => fmtInt.format(num(d.cantidad_cortes)) },
            { label: 'Metros', num: true, value: d => units(d.suma_cortes) },
            { label: 'Creado', value: d => fecha(d.fecha_creacion, true) },
            { label: 'Usuario', value: d => text(d.usuario_creacion) || '—' },
            {
                label: 'Estado', html: true,
                value: d => {
                    const activo = d.activo === true || text(d.activo).toLowerCase() === 'true';
                    return '<span class="dash-tag ' + (activo ? 'is-contado' : '') + '">' +
                        (activo ? 'ACTIVO' : 'INACTIVO') + '</span>';
                }
            }
        ], datos, 'Sin cortes registrados');
    }

    D.registrar({ id: 'cortesResumen', area: 'almacen', render: renderResumen });
    D.registrar({ id: 'cortesProducto', area: 'almacen', render: renderCortesProducto });
    D.registrar({ id: 'cortesMedida', area: 'almacen', render: renderCortesMedida });
    D.registrar({ id: 'cortesUbicacion', area: 'almacen', render: renderCortesUbicacion });
    D.registrar({ id: 'cortesDia', area: 'almacen', render: renderCortesDia });
    D.registrar({ id: 'cortesDetalle', area: 'almacen', render: renderCortesDetalle });

})(window.Dashboard);
