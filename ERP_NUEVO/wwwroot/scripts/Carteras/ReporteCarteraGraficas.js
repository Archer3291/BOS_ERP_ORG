/* ══════════════════════════════════════════════════════════════════════
   ReporteCarteraGraficas.js  —  Dashboard ECharts para el reporte de cartera
   Requiere: Apache ECharts (cargado en la vista)
══════════════════════════════════════════════════════════════════════ */

const CarteraGraficas = (() => {

    const PALETTE = {
        vencido: '#E24B4A',
        pendiente: '#EF9F27',
        cobrado: '#1D9E75',
        porVencer: '#378ADD',
        neutral: '#888780',
        ageing: ['#1D9E75', '#378ADD', '#EF9F27', '#D85A30', '#E24B4A', '#A32D2D'],
        estados: { 'PENDIENTE': '#EF9F27', 'COBRADO': '#1D9E75', 'PARCIAL': '#378ADD', 'VENCIDO': '#E24B4A' },
    };

    const fmt = (n) => new Intl.NumberFormat('es-MX',
        { style: 'currency', currency: 'MXN', minimumFractionDigits: 0, maximumFractionDigits: 0 }
    ).format(n);

    const getTheme = () => window.matchMedia('(prefers-color-scheme: dark)').matches;

    const textColor = () => getTheme() ? '#c9c7c0' : '#5F5E5A';
    const gridColor = () => getTheme() ? 'rgba(255,255,255,0.07)' : 'rgba(0,0,0,0.06)';
    const bgColor = () => getTheme() ? '#1e1e1e' : '#ffffff';

    // Instancias activas para resize
    const _charts = {};

    function initChart(id) {
        if (_charts[id]) { _charts[id].dispose(); }
        const el = document.getElementById(id);
        if (!el) return null;
        const c = echarts.init(el, null, { renderer: 'svg' });
        _charts[id] = c;
        return c;
    }

    /* ── 1. Top vendedores por monto vencido (barras horizontales) ──── */
    function drawVendedores(data) {
        const chart = initChart('gc-vendedores');
        if (!chart || !data?.length) return;

        const sorted = [...data].sort((a, b) => b.monto_vencido - a.monto_vencido);
        const nombres = sorted.map(d => d.vendedor.length > 22 ? d.vendedor.substring(0, 20) + '…' : d.vendedor);

        const monto_vencido = sorted.map(d => d.monto_vencido);
        const monto_vigente = sorted.map(d => d.monto_vigente);
        const monto_total = sorted.map(d => d.monto_total);

        const facturas_vencidas = sorted.map(d => d.facturas_vencidas);
        const facturas_vigentes = sorted.map(d => d.facturas_vigentes);
        const facturas_totales = sorted.map(d => d.facturas_totales);

        chart.setOption({
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: function (params) {
                    let html = `<strong>${params[0].axisValue}</strong><br>`;
                    params.forEach(p => {
                        const monto = Number(p.data.monto || 0)
                            .toLocaleString('es-MX', {
                                style: 'currency',
                                currency: 'MXN'
                            });
                        html += `
                            ${p.marker}
                            ${p.seriesName}: 
                            ${p.value} facturas
                            <br>
                            <span style="margin-left:14px;color:#999;">
                                ${monto}
                            </span>
                            <br>
                        `;
                    });

                    return html;
                }
            },
            legend: { data: ['Vencidas', 'Vigentes'], top: 4, textStyle: { color: textColor(), fontSize: 11 } },
            grid: { right: 20, top: 32 },
            xAxis: [{ type: 'value' }],
            yAxis: [{
                type: 'category',
                axisTick: { show: false },
                data: nombres
            }],
            series: [
                {
                    name: 'Vencidas',
                    type: 'bar',
                    label: {
                        show: true,
                        position: 'rigth',
                        formatter: v => v.value <= 0 ? '' : v.value
                    },
                    emphasis: {
                        focus: 'series'
                    },
                    data: sorted.map(d => ({
                        value: d.facturas_vencidas,
                        monto: d.monto_vencido
                    })),
                    color: PALETTE.vencido
                },
                {
                    name: 'Vigentes',
                    type: 'bar',
                    label: {
                        show: true,
                        position: 'right',
                        formatter: v => v.value <= 0 ? '' : v.value
                    },
                    emphasis: {
                        focus: 'series'
                    },
                    data: sorted.map(d => ({
                        value: d.facturas_vigentes,
                        monto: d.monto_vigente
                    })),
                    color: PALETTE.porVencer
                },
            ]
        })
    }

    /* ── 2. Top clientes por saldo pendiente ────────────────────────── */
    function drawClientes(data) {
        const chart = initChart('gc-clientes');
        if (!chart || !data?.length) return;

        const sorted = [...data].sort((a, b) => b.saldo_pendiente - a.saldo_pendiente);
        const nombres = sorted.map(d => d.cliente.length > 24 ? d.cliente.substring(0, 22) + '…' : d.cliente);

        const monto_vencido = sorted.map(d => d.monto_vencido);
        const monto_vigente = sorted.map(d => d.monto_vigente);
        const monto_total = sorted.map(d => d.saldo_total);

        const facturas_vencidas = sorted.map(d => d.facturas_vencidas);
        const facturas_vigentes = sorted.map(d => d.facturas_vigentes);
        const facturas_totales = sorted.map(d => d.facturas);

        const diasMax = sorted.map(d => +d.max_dias_vencido);

        chart.setOption({
            backgroundColor: 'transparent',
            tooltip: {
                trigger: 'axis',
                axisPointer: { type: 'shadow' },
                formatter: function (params) {
                    let html = `<strong>${params[0].axisValue}</strong><br>`;
                    params.forEach(p => {
                        const monto = Number(p.data.monto || 0)
                            .toLocaleString('es-MX', {
                                style: 'currency',
                                currency: 'MXN'
                            });
                        html += `
                            ${p.marker}
                            ${p.seriesName}: 
                            ${p.value} facturas
                            <br>
                            <span style="margin-left:14px;color:#999;">
                                ${monto}
                            </span>
                            <br>
                        `;
                    });

                    return html;
                }
            },
            legend: { data: ['Vencidas', 'Vigentes'], top: 4, textStyle: { color: textColor(), fontSize: 11 } },
            grid: { right: 20, top: 32 },
            xAxis: {
                type: 'value',
                axisLabel: { formatter: v => fmt(v), color: textColor(), fontSize: 10 },
                splitLine: { lineStyle: { color: gridColor() } }
            },
            yAxis: {
                type: 'category', data: nombres,
                axisLabel: { color: textColor(), fontSize: 11 },
                axisTick: { show: false }
            },
            series: [
                {
                    name: 'Vencidas',
                    type: 'bar',
                    label: {
                        show: true,
                        position: 'rigth',
                        formatter: v => v.value <= 0 ? '' : v.value
                    },
                    emphasis: {
                        focus: 'series'
                    },
                    data: sorted.map(d => ({
                        value: d.facturas_vencidas,
                        monto: d.monto_vencido
                    })),
                    color: PALETTE.vencido
                },
                {
                    name: 'Vigentes',
                    type: 'bar',
                    label: {
                        show: true,
                        position: 'right',
                        formatter: v => v.value <= 0 ? '' : v.value
                    },
                    emphasis: {
                        focus: 'series'
                    },
                    data: sorted.map(d => ({
                        value: d.facturas_vigentes,
                        monto: d.monto_vigente
                    })),
                    color: PALETTE.pendiente
                },
            ]
        });
    }

    /* ── 3. Ageing — donut ──────────────────────────────────────────── */
    function drawAgeing(data) {
        const chart = initChart('gc-ageing');
        if (!chart || !data?.length) return;

        const ORDER = ['Cobrado', 'Por vencer', '1–30 días', '31–60 días', '61–90 días', '+90 días'];
        const sorted = ORDER.map(tramo => data.find(d => d.tramo === tramo) || { tramo, facturas: 0, monto: 0 });
        const total = sorted.reduce((s, d) => s + +d.monto, 0);

        chart.setOption({
            backgroundColor: 'transparent',
            tooltip: {
                trigger: 'item',
                formatter: p => `<b>${p.marker} ${p.name}</b><br/>${p.value} facturas<br/>${p.percent}%`
            },
            legend: {
                orient: 'vertical', right: 10, top: 'middle',
                textStyle: { color: textColor(), fontSize: 11 },
                formatter: name => {
                    const d = sorted.find(x => x.tramo === name);
                    return d ? `${name}  (${d.facturas})` : name;
                }
            },
            series: [{
                type: 'pie',
                radius: ['45%', '70%'],
                center: ['50%', '50%'],
                label: { show: false },
                avoidLabelOverlap: false,
                padAngle: 5,
                itemStyle: {
                    borderRadius: 10
                },
                emphasis: {
                    label: {
                        show: true,
                        fontSize: 13,
                        fontWeight: 'bold',
                        formatter: p => `${p.percent}%`
                    }
                },
                data: sorted.map((d, i) => ({
                    name: d.tramo,
                    value: +d.facturas,
                    itemStyle: { color: PALETTE.ageing[i] }
                })),
            }],
            media: [{
                query: { maxWidth: 600 },
                option: {
                    legend: {
                        orient: 'horizontal',
                        left: 'center',
                        bottom: 0,
                        right: 'auto',
                        top: 'auto'
                    },
                    series: [{ center: ['50%', '40%'] }]
                }
            }]
        });
    }

    /* ── 4. Tendencia mensual — líneas ──────────────────────────────── */
    function drawTendencia(data) {
        const chart = initChart('gc-tendencia');
        if (!chart || !data?.length) return;

        const meses = data.map(d => d.mes);
        const emitido = data.map(d => +d.emitido);
        const cobrado = data.map(d => +d.cobrado);
        const pendiente = data.map((d, i) => Math.max(0, emitido[i] - cobrado[i]));

        chart.setOption({
            backgroundColor: 'transparent',
            tooltip: {
                trigger: 'axis',
                formatter: params => {

                    const em = params.find(p => p.seriesName === 'Emitido');
                    const co = params.find(p => p.seriesName === 'Cobrado');
                    const pe = params.find(p => p.seriesName === 'Pendiente');

                    return `
                        <b>${params[0]?.axisValue || ''}</b><br/>

                        ${em ? `
                            ${em.marker}
                            Emitido: ${fmt(em.value)}
                            <br/>
                        ` : ''}

                        ${co ? `
                            ${co.marker}
                            Cobrado:
                            <b style="color:${PALETTE.cobrado}">
                                ${fmt(co.value)}
                            </b>
                            <br/>
                        ` : ''}

                        ${pe ? `
                            ${pe.marker}
                            Pendiente:
                            <b style="color:${PALETTE.vencido}">
                                ${fmt(pe.value)}
                            </b>
                        ` : ''}
                    `;
                }
            },
            legend: {
                data: ['Emitido', 'Cobrado', 'Pendiente'], top: 4,
                textStyle: { color: textColor(), fontSize: 11 }
            },
            grid: { right: 20, top: 32 },
            xAxis: {
                type: 'category', data: meses,
                axisLabel: { color: textColor(), fontSize: 10, rotate: 30 },
                axisLine: { lineStyle: { color: gridColor() } }
            },
            yAxis: {
                type: 'value',
                axisLabel: { formatter: v => fmt(v), color: textColor(), fontSize: 10 },
                splitLine: { lineStyle: { color: gridColor() } }
            },
            series: [
                {
                    name: 'Emitido', type: 'line', data: emitido,
                    lineStyle: { color: PALETTE.porVencer, width: 2, type: 'dashed' },
                    itemStyle: { color: PALETTE.porVencer },
                    symbol: 'circle', symbolSize: 5, smooth: false
                },
                {
                    name: 'Cobrado', type: 'line', data: cobrado,
                    lineStyle: { color: PALETTE.cobrado, width: 2 },
                    itemStyle: { color: PALETTE.cobrado },
                    areaStyle: { color: PALETTE.cobrado + '22' },
                    symbol: 'circle', symbolSize: 5, smooth: false
                },
                {
                    name: 'Pendiente', type: 'line', data: pendiente,
                    lineStyle: { color: PALETTE.vencido, width: 2 },
                    itemStyle: { color: PALETTE.vencido },
                    areaStyle: { color: PALETTE.vencido + '18' },
                    symbol: 'circle', symbolSize: 5, smooth: false
                }
            ]
        });
    }

    /* ── 5. Estado de cartera — donut pequeño ───────────────────────── */
    function drawEstados(data) {
        const chart = initChart('gc-estados');
        if (!chart || !data?.length) return;

        chart.setOption({
            backgroundColor: 'transparent',
            tooltip: {
                trigger: 'item',
                formatter: p => `<b>${p.marker} ${p.name}</b><br/>${p.value} facturas<br/>${p.percent}%`
            },
            legend: {
                orient: 'vertical', right: 10, top: 'middle',
                textStyle: { color: textColor(), fontSize: 11 },
                formatter: name => {
                    const d = data.find(x => x.estado === name);
                    return d ? `${name}  (${d.facturas})` : name;
                }
            },
            series: [{
                type: 'pie',
                radius: ['45%', '70%'],
                center: ['50%', '50%'],
                label: { show: false },
                avoidLabelOverlap: false,
                padAngle: 5,
                itemStyle: {
                    borderRadius: 10
                },
                emphasis: { label: { show: true, fontSize: 13, formatter: p => `${p.percent}%` } },
                data: data.map(d => ({
                    name: d.estado,
                    value: +d.facturas,
                    itemStyle: { color: PALETTE.estados[d.estado] || PALETTE.neutral }
                }))
            }],
            media: [{
                query: { maxWidth: 600 },
                option: {
                    legend: {
                        orient: 'horizontal',
                        left: 'center',
                        bottom: 0,
                        right: 'auto',
                        top: 'auto'
                    },
                    series: [{ center: ['50%', '40%'] }]
                }
            }]
        });
    }

    /* ── API pública ─────────────────────────────────────────────────── */
    async function load(filters) {
        try {
            ['gc-vendedores', 'gc-clientes', 'gc-ageing', 'gc-tendencia', 'gc-estados']
                .forEach(id => {
                    const el = document.getElementById(id);
                    if (el) el.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100%;color:var(--text-light);font-size:13px;opacity:.5;">Cargando...</div>';
                });

            const d = await GetData({
                path: '/Carteras/GetGraficasCartera',
                data: filters
            });

            // ── Espera a que el contenedor sea visible antes de renderizar ──
            setTimeout(() => {
                drawVendedores(d.vendedores);
                drawClientes(d.clientes);
                drawAgeing(d.ageing);
                drawTendencia(d.tendencia);
                drawEstados(d.estados);
            }, 50);

        } catch (ex) {
            console.error('Graficas error:', ex);
        }
    }

    function resize() {
        Object.values(_charts).forEach(c => c?.resize());
    }

    window.addEventListener('resize', () => resize());

    return { load, resize };

})();