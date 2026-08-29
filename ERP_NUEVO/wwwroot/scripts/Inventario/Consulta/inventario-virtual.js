(function () {
    'use strict';

    // ------------------------------------------------------------------
    // Estado en memoria
    // ------------------------------------------------------------------
    var state = {
        data: { sucursales: [] },
        selectedSucursalId: null,
        selectedAlmacenId: null,
        searchTerm: '',
        nivelMap: {}      // id_nivel -> { rack, nivel }  (se reconstruye al renderizar el almacén actual)
    };

    var modalInstance = null;
    var searchDebounceTimer = null;

    // ------------------------------------------------------------------
    // Helpers DOM
    // ------------------------------------------------------------------
    function $(id) { return document.getElementById(id); }

    function escapeHtml(value) {
        if (value === null || value === undefined) return '';
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // ------------------------------------------------------------------
    // Carga de datos
    // ------------------------------------------------------------------
    function cargarDatos() {
        mostrarEstado('carga');
        fetch('/Consulta/GetAllData', { method: 'POST' })
            .then(function (resp) { return resp.json(); })
            .then(function (json) {
                state.data = extraerEstructura(json);
                poblarSucursales();
                mostrarEstado('placeholder');
            })
            .catch(function (err) {
                console.error('Error cargando inventario:', err);
                mostrarEstado('placeholder');
            });
    }

    // GetAllData regresa jsonb_pretty(...) como una sola columna/fila.
    // Según cómo RunQuery serialice el resultado, puede llegar como:
    //  - un string JSON directo
    //  - un arreglo con una fila { columna: "string json" }
    //  - ya como objeto { sucursales: [...] }
    // Ajusta esta función si tu RunQuery devuelve una forma distinta.
    function extraerEstructura(json) {
        var data = json;
        if (Array.isArray(data) && data.length > 0) {
            var fila = data[0];
            var valores = Object.keys(fila).map(function (k) { return fila[k]; });
            data = valores[0];
        }
        if (typeof data === 'string') {
            try { data = JSON.parse(data); } catch (e) { data = { sucursales: [] }; }
        }
        return (data && data.sucursales) ? data : { sucursales: [] };
    }

    function mostrarEstado(nombre) {
        $('estadoCarga').style.display = nombre === 'carga' ? '' : 'none';
        $('estadoPlaceholder').style.display = nombre === 'placeholder' ? '' : 'none';
        $('estadoVacioAlmacen').style.display = nombre === 'vacioAlmacen' ? '' : 'none';
        $('almacenContenedor').style.display = nombre === 'almacen' ? '' : 'none';
    }

    // ------------------------------------------------------------------
    // Selectores en cascada
    // ------------------------------------------------------------------
    function poblarSucursales() {
        var sel = $('selSucursal');
        sel.innerHTML = '<option value="">Selecciona sucursal...</option>';
        (state.data.sucursales || []).forEach(function (s) {
            var opt = document.createElement('option');
            opt.value = s.id;
            opt.textContent = s.descripcion + ' (' + s.cve + ')';
            sel.appendChild(opt);
        });
    }

    function poblarAlmacenes() {
        var sel = $('selAlmacen');
        sel.innerHTML = '<option value="">Selecciona almacén...</option>';
        var sucursal = obtenerSucursalSeleccionada();
        var almacenes = sucursal ? (sucursal.almacenes || []) : [];
        almacenes.forEach(function (a) {
            var opt = document.createElement('option');
            opt.value = a.id;
            opt.textContent = a.descripcion + ' (' + a.cve + ')';
            sel.appendChild(opt);
        });
        sel.disabled = almacenes.length === 0;
    }

    function obtenerSucursalSeleccionada() {
        var id = state.selectedSucursalId;
        if (!id) return null;
        return (state.data.sucursales || []).find(function (s) { return String(s.id) === String(id); }) || null;
    }

    function obtenerAlmacenSeleccionado() {
        var sucursal = obtenerSucursalSeleccionada();
        if (!sucursal || !state.selectedAlmacenId) return null;
        return (sucursal.almacenes || []).find(function (a) { return String(a.id) === String(state.selectedAlmacenId); }) || null;
    }

    function onSucursalChange() {
        state.selectedSucursalId = $('selSucursal').value || null;
        state.selectedAlmacenId = null;
        $('selAlmacen').value = '';
        poblarAlmacenes();
        mostrarEstado('placeholder');
    }

    function onAlmacenChange() {
        state.selectedAlmacenId = $('selAlmacen').value || null;
        renderAlmacen();
    }

    // ------------------------------------------------------------------
    // Cálculo de ocupación
    // ------------------------------------------------------------------

    // nivel.capacidad viene duplicado desde el rack en la consulta SQL (es la
    // misma capacidad para todos los niveles de un rack). Se usa como
    // referencia de "cuánto cabe" en ese nivel; si no viene, se cae a un
    // estado binario vacío/ocupado.
    function ocupacionNivel(nivel) {
        var tarimas = nivel.tarimas || [];
        var totalTarimas = tarimas.length;
        var totalCantidad = tarimas.reduce(function (suma, t) {
            return suma + (t.productos || []).reduce(function (s2, p) {
                return s2 + (Number(p.cantidad) || 0);
            }, 0);
        }, 0);

        var ratio;
        if (nivel.capacidad && Number(nivel.capacidad) > 0) {
            ratio = Math.min(1, totalCantidad / Number(nivel.capacidad));
        } else {
            ratio = totalTarimas > 0 ? 1 : 0;
        }

        return { totalTarimas: totalTarimas, totalCantidad: totalCantidad, ratio: ratio };
    }

    function claseCalor(ratio) {
        if (ratio <= 0) return 'iv-nivel-vacio';
        if (ratio < 0.34) return 'iv-nivel-baja';
        if (ratio < 0.7) return 'iv-nivel-media';
        return 'iv-nivel-alta';
    }

    function nivelesDeRack(rack) {
        var lista = [];
        (rack.columnas || []).forEach(function (c) {
            (c.niveles || []).forEach(function (n) { lista.push(n); });
        });
        return lista;
    }

    function nivelesDePasillo(pasillo) {
        var lista = [];
        (pasillo.racks || []).forEach(function (r) { lista = lista.concat(nivelesDeRack(r)); });
        return lista;
    }

    function nivelesDeAlmacen(almacen) {
        var lista = [];
        (almacen.pasillos || []).forEach(function (p) { lista = lista.concat(nivelesDePasillo(p)); });
        (almacen.racks_sin_pasillo || []).forEach(function (r) { lista = lista.concat(nivelesDeRack(r)); });
        return lista;
    }

    function calcularEstadisticas(niveles) {
        var totalNiveles = niveles.length;
        var nivelesOcupados = 0, totalTarimas = 0, totalProductos = 0, sumaRatios = 0;

        niveles.forEach(function (n) {
            var o = ocupacionNivel(n);
            if (o.totalTarimas > 0) nivelesOcupados++;
            totalTarimas += o.totalTarimas;
            totalProductos += o.totalCantidad;
            sumaRatios += o.ratio;
        });

        var porcentaje = totalNiveles > 0 ? Math.round((sumaRatios / totalNiveles) * 100) : 0;

        return {
            totalNiveles: totalNiveles,
            nivelesOcupados: nivelesOcupados,
            totalTarimas: totalTarimas,
            totalProductos: totalProductos,
            porcentajeOcupacion: porcentaje
        };
    }

    // ------------------------------------------------------------------
    // Render del plano del almacén
    // ------------------------------------------------------------------
    function renderAlmacen() {
        var almacen = obtenerAlmacenSeleccionado();
        state.nivelMap = {};

        if (!almacen) {
            mostrarEstado('placeholder');
            return;
        }

        var pasillos = almacen.pasillos || [];
        var racksSinPasillo = almacen.racks_sin_pasillo || [];

        if (pasillos.length === 0 && racksSinPasillo.length === 0) {
            mostrarEstado('vacioAlmacen');
            return;
        }

        var statsAlmacen = calcularEstadisticas(nivelesDeAlmacen(almacen));

        var html = construirMiniMapa(almacen) + construirBarraEstadisticas(statsAlmacen);

        pasillos.forEach(function (pasillo) {
            html += renderPasillo(pasillo);
        });

        if (racksSinPasillo.length > 0) {
            html += '<div class="iv-pasillo" id="pasillo-sin-asignar">' +
                '<div class="iv-pasillo-header">Racks sin pasillo asignado</div>' +
                '<div class="iv-pasillo-body iv-un-lado"><div class="iv-racks-side">' +
                racksSinPasillo.map(renderRack).join('') +
                '</div></div></div>';
        }

        $('almacenContenedor').innerHTML = html;
        mostrarEstado('almacen');
        aplicarResaltadoBusqueda();
    }

    function construirMiniMapa(almacen) {
        var chips = (almacen.pasillos || []).map(function (p) {
            var st = calcularEstadisticas(nivelesDePasillo(p));
            var clase = claseCalor(st.porcentajeOcupacion / 100);
            return '<button type="button" class="iv-minimap-chip ' + clase + '" data-target="#pasillo-' + p.id + '">' +
                '<span class="iv-minimap-chip-label">P' + escapeHtml(p.num_pasillo) + '</span>' +
                '<span class="iv-minimap-chip-pct">' + st.porcentajeOcupacion + '%</span>' +
                '</button>';
        }).join('');

        var sinPasilloLista = [];
        (almacen.racks_sin_pasillo || []).forEach(function (r) { sinPasilloLista = sinPasilloLista.concat(nivelesDeRack(r)); });
        if (sinPasilloLista.length > 0) {
            var st2 = calcularEstadisticas(sinPasilloLista);
            var clase2 = claseCalor(st2.porcentajeOcupacion / 100);
            chips += '<button type="button" class="iv-minimap-chip ' + clase2 + '" data-target="#pasillo-sin-asignar">' +
                '<span class="iv-minimap-chip-label">S/P</span>' +
                '<span class="iv-minimap-chip-pct">' + st2.porcentajeOcupacion + '%</span>' +
                '</button>';
        }

        if (!chips) return '';
        return '<div class="iv-minimap"><span class="iv-minimap-titulo">Ir a:</span>' + chips + '</div>';
    }

    function construirBarraEstadisticas(stats) {
        return '<div class="iv-stats-bar">' +
            '<div class="iv-stats-item"><strong>' + stats.nivelesOcupados + '/' + stats.totalNiveles + '</strong><span>niveles ocupados</span></div>' +
            '<div class="iv-stats-item"><strong>' + stats.totalTarimas + '</strong><span>tarimas</span></div>' +
            '<div class="iv-stats-item"><strong>' + stats.totalProductos + '</strong><span>unidades</span></div>' +
            '<div class="iv-stats-gauge" title="' + stats.porcentajeOcupacion + '% de ocupación promedio">' +
            '<div class="iv-stats-gauge-fill" style="width:' + stats.porcentajeOcupacion + '%"></div>' +
            '</div>' +
            '<div class="iv-stats-pct">' + stats.porcentajeOcupacion + '% ocupación</div>' +
            '</div>';
    }

    function ladosDePasillo(pasillo) {
        var set = {};
        (pasillo.racks || []).forEach(function (r) {
            if (r.lado) set[r.lado] = true;
        });
        return Object.keys(set).sort();
    }

    function racksPorLado(pasillo, lado) {
        return (pasillo.racks || []).filter(function (r) { return r.lado === lado; });
    }

    function renderPasillo(pasillo) {
        var lados = ladosDePasillo(pasillo);
        var cuerpo;

        if (lados.length === 2) {
            cuerpo =
                '<div class="iv-pasillo-body iv-dos-lados">' +
                '<div class="iv-racks-side">' +
                '<div class="iv-side-label">Lado ' + escapeHtml(lados[0]) + '</div>' +
                racksPorLado(pasillo, lados[0]).map(renderRack).join('') +
                '</div>' +
                '<div class="iv-pasillo-corridor"><span>PASILLO ' + escapeHtml(pasillo.num_pasillo) + '</span></div>' +
                '<div class="iv-racks-side">' +
                '<div class="iv-side-label">Lado ' + escapeHtml(lados[1]) + '</div>' +
                racksPorLado(pasillo, lados[1]).map(renderRack).join('') +
                '</div>' +
                '</div>';
        } else {
            cuerpo =
                '<div class="iv-pasillo-body iv-un-lado"><div class="iv-racks-side">' +
                (pasillo.racks || []).map(renderRack).join('') +
                '</div></div>';
        }

        return '<div class="iv-pasillo" id="pasillo-' + pasillo.id + '">' +
            '<div class="iv-pasillo-header">Pasillo ' + escapeHtml(pasillo.num_pasillo) +
            ' <span class="text-muted">(' + escapeHtml(pasillo.cve) + ')</span></div>' +
            cuerpo +
            '</div>';
    }

    function renderRack(rack) {
        var columnas = (rack.columnas || []).slice().sort(function (a, b) {
            return (a.num_col || 0) - (b.num_col || 0);
        });

        var columnasHtml = columnas.map(function (columna) {
            var niveles = (columna.niveles || []).slice().sort(function (a, b) {
                return (b.num_nivel || 0) - (a.num_nivel || 0); // nivel más alto arriba, como en un rack real
            });

            var nivelesHtml = niveles.map(function (nivel) {
                state.nivelMap[nivel.id] = { rack: rack, nivel: nivel };
                var ocup = ocupacionNivel(nivel);
                var estadoClase = claseCalor(ocup.ratio);
                var titulo = 'Nivel ' + nivel.num_nivel +
                    (nivel.capacidad ? ' — Capacidad: ' + nivel.capacidad : '') +
                    ' — ' + ocup.totalTarimas + ' tarima(s)' +
                    (ocup.totalCantidad ? ' — ' + ocup.totalCantidad + ' unidades' : '');

                return '<div class="iv-nivel ' + estadoClase + '" data-nivel-id="' + nivel.id + '" title="' + escapeHtml(titulo) + '">' +
                    (ocup.totalTarimas > 0 ? '<div class="iv-pallet" aria-hidden="true"></div>' : '') +
                    '<span class="iv-nivel-label">' + escapeHtml(nivel.num_nivel) + '</span>' +
                    (ocup.totalTarimas > 0 ? '<span class="iv-nivel-count">' + ocup.totalTarimas + '</span>' : '') +
                    '</div>';
            }).join('');

            return '<div class="iv-columna">' +
                '<div class="iv-columna-header">' + escapeHtml(columna.nombre || ('Col ' + columna.num_col)) + '</div>' +
                nivelesHtml +
                '</div>';
        }).join('');

        return '<div class="iv-rack">' +
            '<div class="iv-rack-header">' + escapeHtml(rack.nombre) + ' <small>#' + escapeHtml(rack.num_rack) + '</small></div>' +
            '<div class="iv-rack-columns">' + columnasHtml + '</div>' +
            '</div>';
    }

    // ------------------------------------------------------------------
    // Búsqueda (resalta niveles/tarimas/productos que coinciden)
    // ------------------------------------------------------------------
    function coincideNivel(nivel, term) {
        return (nivel.tarimas || []).some(function (t) {
            var enTarima = (t.codigo || '').toLowerCase().indexOf(term) !== -1;
            var enProductos = (t.productos || []).some(function (p) {
                return (p.cve_prod || '').toLowerCase().indexOf(term) !== -1 ||
                    (p.descr_prod || '').toLowerCase().indexOf(term) !== -1;
            });
            return enTarima || enProductos;
        });
    }

    function aplicarResaltadoBusqueda() {
        var term = state.searchTerm.toLowerCase();
        Object.keys(state.nivelMap).forEach(function (nivelId) {
            var el = document.querySelector('.iv-nivel[data-nivel-id="' + nivelId + '"]');
            if (!el) return;
            var match = term && coincideNivel(state.nivelMap[nivelId].nivel, term);
            el.classList.toggle('iv-nivel-match', !!match);
        });
    }

    function onBusquedaInput() {
        clearTimeout(searchDebounceTimer);
        searchDebounceTimer = setTimeout(function () {
            state.searchTerm = $('inputBusqueda').value.trim();
            aplicarResaltadoBusqueda();
        }, 150);
    }

    // ------------------------------------------------------------------
    // Interacción: clics dentro del plano (delegación de eventos)
    // ------------------------------------------------------------------
    function onContenedorClick(e) {
        var chip = e.target.closest('.iv-minimap-chip');
        if (chip) {
            var destino = document.querySelector(chip.getAttribute('data-target'));
            if (destino) destino.scrollIntoView({ behavior: 'smooth', block: 'start' });
            return;
        }

        var nivelEl = e.target.closest('.iv-nivel');
        if (!nivelEl) return;
        var nivelId = nivelEl.getAttribute('data-nivel-id');
        var entry = state.nivelMap[nivelId];
        if (entry) abrirDetalleNivel(entry.rack, entry.nivel);
    }

    function abrirDetalleNivel(rack, nivel) {
        var ocup = ocupacionNivel(nivel);
        var infoOcupacion = nivel.capacidad
            ? ' — Ocupación: ' + Math.round(ocup.ratio * 100) + '% (cap. ' + nivel.capacidad + ')'
            : '';

        $('modalDetalleTitulo').innerHTML =
            'Rack ' + escapeHtml(rack.nombre) + ' — Nivel ' + escapeHtml(nivel.num_nivel) +
            '<small class="d-block text-muted">' + escapeHtml(nivel.ulocation) + infoOcupacion + '</small>';

        var tarimas = nivel.tarimas || [];
        var term = state.searchTerm.toLowerCase();
        var body;

        if (tarimas.length === 0) {
            body = '<div class="text-muted text-center py-4">Este nivel no tiene tarimas registradas.</div>';
        } else {
            body = tarimas.map(function (tarima) {
                var filas = (tarima.productos || []).map(function (p) {
                    var match = term && (
                        (p.cve_prod || '').toLowerCase().indexOf(term) !== -1 ||
                        (p.descr_prod || '').toLowerCase().indexOf(term) !== -1
                    );
                    return '<tr' + (match ? ' class="table-warning"' : '') + '>' +
                        '<td>' + escapeHtml(p.cve_prod) + '</td>' +
                        '<td>' + escapeHtml(p.descr_prod) + '</td>' +
                        '<td>' + escapeHtml(p.cantidad) + '</td>' +
                        '<td>' + escapeHtml(p.unidad) + '</td>' +
                        '</tr>';
                }).join('');

                return '<div class="iv-tarima-detalle mb-3">' +
                    '<h6>Tarima ' + escapeHtml(tarima.codigo) + ' <small class="text-muted">' + escapeHtml(tarima.fecha) + '</small></h6>' +
                    '<table class="table table-sm table-bordered align-middle">' +
                    '<thead><tr><th>Cve</th><th>Descripción</th><th>Cantidad</th><th>Unidad</th></tr></thead>' +
                    '<tbody>' + filas + '</tbody>' +
                    '</table></div>';
            }).join('');
        }

        $('modalDetalleBody').innerHTML = body;

        if (!modalInstance) {
            modalInstance = new bootstrap.Modal($('modalDetalleNivel'));
        }
        modalInstance.show();
    }

    // ------------------------------------------------------------------
    // Inicialización
    // ------------------------------------------------------------------
    document.addEventListener('DOMContentLoaded', function () {
        $('selSucursal').addEventListener('change', onSucursalChange);
        $('selAlmacen').addEventListener('change', onAlmacenChange);
        $('inputBusqueda').addEventListener('input', onBusquedaInput);
        $('almacenContenedor').addEventListener('click', onContenedorClick);

        cargarDatos();
    });
})();