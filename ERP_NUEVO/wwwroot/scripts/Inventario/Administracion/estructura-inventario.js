/* ==========================================================================
   Explorador de estructura del inventario
   Sucursal › Almacén › Pasillo › Rack › Columna › Nivel › Tarima

   La idea de fondo: el inventario ES una jerarquía, así que se navega como
   tal. Cada alta ocurre DENTRO del nodo donde estás parado, de modo que nunca
   hay que volver a elegir el padre en cascadas de combos: el padre es el sitio
   desde el que pulsaste «Añadir».

   Todo el árbol llega en una sola llamada (/AdministracionInventario/Arbol)
   y se indexa en memoria; a partir de ahí la navegación no toca la red.
   ========================================================================== */

(function (window, document) {
    'use strict';

    // ------------------------------------------------------------------ estado

    var RUTA = '/AdministracionInventario';

    var _arbol = [];            // sucursales[] tal como llegan del backend
    var _indice = new Map();    // clave -> nodo indexado
    var _sel = null;            // clave del nodo seleccionado
    var _abiertos = new Set();  // claves expandidas en el árbol
    var _filtro = '';
    var _cargando = false;
    var _tokenPrevia = 0;       // descarta vistas previas del generador que llegan tarde

    // Metadatos por tipo de nodo: icono, etiqueta y quién puede colgar de él.
    var TIPOS = {
        sucursal: { etiqueta: 'Sucursal', plural: 'Sucursales', icono: 'fa-building', hijo: 'almacen' },
        almacen:  { etiqueta: 'Almacén',  plural: 'Almacenes',  icono: 'fa-warehouse', hijo: 'pasillo' },
        pasillo:  { etiqueta: 'Pasillo',  plural: 'Pasillos',   icono: 'fa-road',      hijo: 'rack' },
        rack:     { etiqueta: 'Rack',     plural: 'Racks',      icono: 'fa-table-cells-large', hijo: 'columna' },
        columna:  { etiqueta: 'Columna',  plural: 'Columnas',   icono: 'fa-grip-lines-vertical', hijo: 'nivel' },
        nivel:    { etiqueta: 'Nivel',    plural: 'Niveles',    icono: 'fa-layer-group', hijo: 'tarima' },
        tarima:   { etiqueta: 'Tarima',   plural: 'Tarimas',    icono: 'fa-pallet',    hijo: null }
    };

    // ------------------------------------------------------------- utilidades

    function esc(v) {
        if (v === null || v === undefined) return '';
        return String(v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function $(sel, raiz) { return (raiz || document).querySelector(sel); }

    function clave(tipo, id) { return tipo + ':' + id; }

    function token() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function aviso(icono, titulo, texto) {
        if (typeof window.toastMixin !== 'undefined') {
            window.toastMixin.fire({ icon: icono, title: titulo, text: texto || '' });
        } else if (icono === 'error') {
            console.error(titulo, texto || '');
        }
    }

    function pluraliza(n, singular, plural) {
        return n + ' ' + (n === 1 ? singular : plural);
    }

    async function pedir(url) {
        var r = await fetch(url, { headers: { 'Accept': 'application/json' } });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        return r.json();
    }

    async function enviar(accion, datos) {
        var fd = new FormData();
        Object.keys(datos).forEach(function (k) {
            if (datos[k] !== null && datos[k] !== undefined) fd.append(k, datos[k]);
        });
        fd.append('__RequestVerificationToken', token());

        var r = await fetch(RUTA + '/' + accion, { method: 'POST', body: fd });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        var data = await r.json();

        // Los endpoints antiguos responden {success}, los nuevos {ok}.
        data.ok = (data.ok !== undefined) ? data.ok : !!data.success;
        return data;
    }

    // -------------------------------------------------------------- indexado

    /**
     * Recorre el árbol y deja un índice plano clave → nodo.
     * Los racks llegan bajo su almacén con `pasillo_id`; aquí se decide si
     * cuelgan de un pasillo o directamente del almacén.
     */
    function indexar() {
        _indice = new Map();

        (_arbol || []).forEach(function (suc) {
            var kSuc = clave('sucursal', suc.id_sucursal);
            var nSuc = registrar(kSuc, 'sucursal', suc.id_sucursal, suc.cve_sucursal, suc, null);
            nSuc.descripcion = suc.descripcion;

            (suc.almacenes || []).forEach(function (alm) {
                var kAlm = clave('almacen', alm.id_almacen);
                var nAlm = registrar(kAlm, 'almacen', alm.id_almacen, alm.cve_almacen, alm, kSuc);
                nAlm.descripcion = alm.descripcion;
                nSuc.hijos.push(kAlm);

                var porPasillo = new Map();
                (alm.pasillos || []).forEach(function (pas) {
                    var kPas = clave('pasillo', pas.id_pasillo);
                    var nPas = registrar(kPas, 'pasillo', pas.id_pasillo, pas.cve_pasillo, pas, kAlm);
                    nAlm.hijos.push(kPas);
                    porPasillo.set(String(pas.id_pasillo), nPas);
                });

                (alm.racks || []).forEach(function (rack) {
                    var kRack = clave('rack', rack.id_rack);
                    // Si el pasillo referido no es de este almacén (dato inconsistente),
                    // el rack cuelga del almacén en lugar de desaparecer del árbol.
                    var padre = rack.pasillo_id != null ? porPasillo.get(String(rack.pasillo_id)) : null;
                    var kPadre = padre ? padre.clave : kAlm;

                    var nRack = registrar(kRack, 'rack', rack.id_rack, rack.nombre, rack, kPadre);
                    (padre ? padre.hijos : nAlm.hijos).push(kRack);

                    (rack.columnas || []).forEach(function (col, iCol) {
                        var kCol = clave('columna', col.id_columna);
                        var nCol = registrar(kCol, 'columna', col.id_columna, col.nombre, col, kRack);
                        nCol.numero = col.num_col != null ? col.num_col : iCol + 1;
                        nRack.hijos.push(kCol);

                        (col.niveles || []).forEach(function (niv, iNiv) {
                            var kNiv = clave('nivel', niv.id_nivel);
                            var nNiv = registrar(kNiv, 'nivel', niv.id_nivel, niv.nombre, niv, kCol);
                            nNiv.numero = niv.num_nivel != null ? niv.num_nivel : iNiv + 1;
                            nCol.hijos.push(kNiv);

                            (niv.tarimas || []).forEach(function (tar) {
                                var kTar = clave('tarima', tar.id_tarima);
                                registrar(kTar, 'tarima', tar.id_tarima, tar.codigo, tar, kNiv);
                                nNiv.hijos.push(kTar);
                            });
                        });
                    });
                });
            });
        });
    }

    function registrar(k, tipo, id, nombre, datos, padre) {
        var nodo = {
            clave: k, tipo: tipo, id: id,
            nombre: nombre || '(sin nombre)',
            datos: datos, padre: padre, hijos: []
        };
        _indice.set(k, nodo);
        return nodo;
    }

    function nodo(k) { return k ? _indice.get(k) : null; }

    function hijosDe(k, tipo) {
        var n = nodo(k);
        if (!n) return [];
        return n.hijos.map(nodo).filter(function (h) { return h && (!tipo || h.tipo === tipo); });
    }

    function ancestros(k) {
        var lista = [], actual = nodo(k);
        while (actual) { lista.unshift(actual); actual = nodo(actual.padre); }
        return lista;
    }

    /** Sube por el árbol hasta encontrar un ancestro del tipo pedido. */
    function ancestroDe(k, tipo) {
        var actual = nodo(k);
        while (actual) {
            if (actual.tipo === tipo) return actual;
            actual = nodo(actual.padre);
        }
        return null;
    }

    /** Cuenta recursivamente los descendientes de un tipo dado. */
    function contar(k, tipo) {
        var n = nodo(k);
        if (!n) return 0;
        var total = 0;
        n.hijos.forEach(function (hk) {
            var h = nodo(hk);
            if (!h) return;
            if (h.tipo === tipo) total++;
            total += contar(hk, tipo);
        });
        return total;
    }

    // ------------------------------------------------------------- filtrado

    /** ¿El nodo o alguno de sus descendientes casa con el texto buscado? */
    function coincide(k) {
        if (!_filtro) return true;
        var n = nodo(k);
        if (!n) return false;

        var propio = (n.nombre || '').toLowerCase().indexOf(_filtro) !== -1 ||
                     (n.descripcion || '').toLowerCase().indexOf(_filtro) !== -1 ||
                     (n.datos && n.datos.ulocation || '').toLowerCase().indexOf(_filtro) !== -1;

        if (propio) return true;
        return n.hijos.some(coincide);
    }

    // ------------------------------------------------------- render: árbol

    function pintarArbol() {
        var cont = $('#ei-arbol-lista');
        if (!cont) return;

        if (!_arbol.length) {
            cont.innerHTML = '<div class="ei-arbol__vacio">' +
                '<i class="fas fa-building fa-lg d-block mb-2"></i>' +
                'Todavía no hay sucursales.</div>';
            return;
        }

        var raices = _arbol.map(function (s) { return clave('sucursal', s.id_sucursal); })
                           .filter(coincide);

        if (!raices.length) {
            cont.innerHTML = '<div class="ei-arbol__vacio">Sin resultados para «' + esc(_filtro) + '».</div>';
            return;
        }

        cont.innerHTML = raices.map(function (k) { return ramaHtml(k); }).join('');
    }

    function ramaHtml(k) {
        var n = nodo(k);
        if (!n) return '';

        var hijos = n.hijos.filter(coincide);
        // Con filtro activo se abre todo lo que sobrevive, para ver el resultado sin clicar.
        var abierto = _filtro ? true : _abiertos.has(k);
        var meta = TIPOS[n.tipo];

        var flecha = hijos.length
            ? '<i class="fas fa-chevron-right ei-nodo__flecha' + (abierto ? ' abierto' : '') + '"></i>'
            : '<i class="fas fa-chevron-right ei-nodo__flecha vacio"></i>';

        var conteo = hijos.length ? '<span class="ei-nodo__conteo">' + hijos.length + '</span>' : '';

        var html = '<div class="ei-nodo">' +
            '<div class="ei-nodo__fila' + (_sel === k ? ' activo' : '') + '" data-clave="' + esc(k) + '" role="treeitem" tabindex="0">' +
                flecha +
                '<i class="fas ' + meta.icono + ' ei-nodo__icono"></i>' +
                '<span class="ei-nodo__texto" title="' + esc(n.nombre) + '">' + esc(n.nombre) + '</span>' +
                conteo +
            '</div>';

        if (hijos.length && abierto) {
            html += '<div class="ei-nodo__hijos">' + hijos.map(ramaHtml).join('') + '</div>';
        }

        return html + '</div>';
    }

    // ------------------------------------------------------- render: panel

    function pintarPanel() {
        var cont = $('#ei-panel');
        if (!cont) return;

        if (!_sel || !nodo(_sel)) {
            cont.innerHTML = panelRaizHtml();
            return;
        }

        var n = nodo(_sel);
        var meta = TIPOS[n.tipo];

        var html = migasHtml(n) + '<div class="ei-tarjeta">' + cabeceraHtml(n, meta) + metricasHtml(n) + '</div>';

        if (n.tipo === 'rack') html += '<div class="ei-tarjeta">' + mapaRackHtml(n) + '</div>';

        html += '<div class="ei-tarjeta">' + seccionesHtml(n) + '</div>';

        cont.innerHTML = html;
    }

    function panelRaizHtml() {
        var sucursales = _arbol.map(function (s) { return nodo(clave('sucursal', s.id_sucursal)); })
                               .filter(Boolean);

        var fichas = sucursales.map(function (s) {
            return fichaHtml(s, pluraliza(hijosDe(s.clave, 'almacen').length, 'almacén', 'almacenes'));
        }).join('');

        return '<div class="ei-tarjeta">' +
            '<div class="ei-cabecera">' +
                '<div class="ei-cabecera__icono"><i class="fas fa-sitemap"></i></div>' +
                '<div class="ei-cabecera__texto">' +
                    '<div class="ei-cabecera__tipo">Estructura</div>' +
                    '<h2 class="ei-cabecera__nombre">Todas las sucursales</h2>' +
                '</div>' +
            '</div>' +
            '<div class="ei-seccion">' +
                seccionCabeceraHtml('Sucursales', 'fa-building', sucursales.length) +
                '<div class="ei-rejilla">' + fichas + agregarHtml('sucursal', null) + '</div>' +
            '</div>' +
        '</div>';
    }

    function migasHtml(n) {
        var cadena = ancestros(n.clave);
        var partes = cadena.map(function (a, i) {
            var esUltimo = i === cadena.length - 1;
            return (i ? '<span class="ei-migas__sep"><i class="fas fa-chevron-right"></i></span>' : '') +
                '<button type="button" class="ei-miga' + (esUltimo ? ' actual' : '') + '" data-ir="' + esc(a.clave) + '">' +
                esc(a.nombre) + '</button>';
        }).join('');

        return '<nav class="ei-migas">' +
            '<button type="button" class="ei-miga" data-ir="__raiz"><i class="fas fa-sitemap"></i></button>' +
            '<span class="ei-migas__sep"><i class="fas fa-chevron-right"></i></span>' +
            partes + '</nav>';
    }

    function cabeceraHtml(n, meta) {
        var subtitulo = n.descripcion ? '<div class="ei-ficha__meta">' + esc(n.descripcion) + '</div>' : '';

        var extras = '';
        if (n.tipo === 'rack') {
            // «Rack / Rack» sería ruido: el chip solo aparece cuando aporta algo.
            if (n.datos.tipo === 'Estante') {
                extras = '<span class="ei-chip-tipo">Estante</span>' +
                    ' <span class="ei-chip-tipo">Lado ' + (n.datos.lado === 'D' ? 'derecho' : 'izquierdo') + '</span>';
            }
        } else if (n.tipo === 'almacen') {
            extras = '<span class="ei-chip-tipo">' + esc(n.datos.tipo || '—') + '</span>';
        } else if (n.tipo === 'nivel' && n.datos.ulocation) {
            extras = '<span class="ei-chip-tipo">' + esc(n.datos.ulocation) + '</span>';
        }

        var acciones = '<button type="button" class="ei-btn" data-accion="editar">' +
                '<i class="fas fa-pen"></i> Editar</button>' +
            '<button type="button" class="ei-btn ei-btn--peligro" data-accion="eliminar">' +
                '<i class="fas fa-trash"></i> Eliminar</button>';

        // El generador solo tiene sentido donde puede acabar creando racks o niveles.
        if (['almacen', 'pasillo', 'rack'].indexOf(n.tipo) !== -1) {
            acciones = '<button type="button" class="ei-btn ei-btn--primario" data-accion="generar">' +
                '<i class="fas fa-bolt"></i> Generar estructura</button>' + acciones;
        }

        return '<div class="ei-cabecera">' +
            '<div class="ei-cabecera__icono"><i class="fas ' + meta.icono + '"></i></div>' +
            '<div class="ei-cabecera__texto">' +
                '<div class="ei-cabecera__tipo">' + esc(meta.etiqueta) + ' ' + extras + '</div>' +
                '<h2 class="ei-cabecera__nombre">' + esc(n.nombre) + '</h2>' +
                subtitulo +
            '</div>' +
            '<div class="ei-cabecera__acciones">' + acciones + '</div>' +
        '</div>';
    }

    function metricasHtml(n) {
        var metricas = [];

        function agrega(tipo, etiqueta) {
            var total = contar(n.clave, tipo);
            metricas.push('<div class="ei-metrica"><span class="ei-metrica__valor">' + total + '</span>' + etiqueta + '</div>');
        }

        if (n.tipo === 'sucursal') { agrega('almacen', 'almacenes'); agrega('pasillo', 'pasillos'); agrega('rack', 'racks'); agrega('nivel', 'niveles'); agrega('tarima', 'tarimas'); }
        else if (n.tipo === 'almacen') { agrega('pasillo', 'pasillos'); agrega('rack', 'racks'); agrega('columna', 'columnas'); agrega('nivel', 'niveles'); agrega('tarima', 'tarimas'); }
        else if (n.tipo === 'pasillo') { agrega('rack', 'racks'); agrega('nivel', 'niveles'); agrega('tarima', 'tarimas'); }
        else if (n.tipo === 'rack') { agrega('columna', 'columnas'); agrega('nivel', 'niveles'); agrega('tarima', 'tarimas'); }
        else if (n.tipo === 'columna') { agrega('nivel', 'niveles'); agrega('tarima', 'tarimas'); }
        else if (n.tipo === 'nivel') { agrega('tarima', 'tarimas'); }
        else if (n.tipo === 'tarima') {
            metricas.push('<div class="ei-metrica"><span class="ei-metrica__valor">' +
                (n.datos.productos || 0) + '</span>productos</div>');
            if (n.datos.fecha) {
                metricas.push('<div class="ei-metrica">Alta: <span class="ei-metrica__valor">' +
                    esc(String(n.datos.fecha).slice(0, 10)) + '</span></div>');
            }
        }

        return metricas.length ? '<div class="ei-metricas">' + metricas.join('') + '</div>' : '';
    }

    function seccionesHtml(n) {
        // El almacén es el único nodo con dos clases de hijo a la vez:
        // sus pasillos y los racks que no cuelgan de ninguno.
        if (n.tipo === 'almacen') {
            return seccionHtml(n, 'pasillo', hijosDe(n.clave, 'pasillo')) +
                   seccionHtml(n, 'rack', hijosDe(n.clave, 'rack'), 'Racks sin pasillo');
        }

        var meta = TIPOS[n.tipo];
        if (!meta.hijo) {
            return '<div class="ei-seccion"><div class="ei-vacio-inline">' +
                'Una tarima es el último eslabón de la estructura. Su contenido se administra desde el inventario.' +
                '</div></div>';
        }

        return seccionHtml(n, meta.hijo, hijosDe(n.clave, meta.hijo));
    }

    function seccionHtml(padre, tipoHijo, hijos, tituloPersonalizado) {
        var meta = TIPOS[tipoHijo];
        var titulo = tituloPersonalizado || meta.plural;

        var fichas = hijos.map(function (h) { return fichaHtml(h, resumenDe(h)); }).join('');

        var cuerpo = '<div class="ei-rejilla">' + fichas + agregarHtml(tipoHijo, padre.clave) + '</div>';

        return '<div class="ei-seccion">' +
            seccionCabeceraHtml(titulo, meta.icono, hijos.length) + cuerpo + '</div>';
    }

    function seccionCabeceraHtml(titulo, icono, total) {
        return '<div class="ei-seccion__cabecera">' +
            '<h3 class="ei-seccion__titulo"><i class="fas ' + icono + '"></i>' + esc(titulo) + '</h3>' +
            '<span class="ei-seccion__conteo">' + total + '</span>' +
        '</div>';
    }

    /** Línea secundaria de una ficha: lo más útil que se puede decir de ese nodo. */
    function resumenDe(n) {
        switch (n.tipo) {
            case 'almacen':
                return (n.datos.tipo || '') + ' · ' + pluraliza(contar(n.clave, 'rack'), 'rack', 'racks');
            case 'pasillo':
                return pluraliza(hijosDe(n.clave, 'rack').length, 'rack', 'racks');
            case 'rack':
                return pluraliza(hijosDe(n.clave, 'columna').length, 'columna', 'columnas') +
                    ' · ' + pluraliza(contar(n.clave, 'nivel'), 'nivel', 'niveles');
            case 'columna':
                return pluraliza(hijosDe(n.clave, 'nivel').length, 'nivel', 'niveles');
            case 'nivel':
                return (n.datos.ulocation || 'sin ULocation') +
                    ' · ' + pluraliza(hijosDe(n.clave, 'tarima').length, 'tarima', 'tarimas');
            case 'tarima':
                return pluraliza(n.datos.productos || 0, 'producto', 'productos');
            case 'sucursal':
                return n.descripcion || '';
            default:
                return '';
        }
    }

    function fichaHtml(n, meta) {
        return '<button type="button" class="ei-ficha" data-ir="' + esc(n.clave) + '">' +
            '<span class="ei-ficha__icono"><i class="fas ' + TIPOS[n.tipo].icono + '"></i></span>' +
            '<span class="ei-ficha__texto">' +
                '<span class="ei-ficha__nombre">' + esc(n.nombre) + '</span>' +
                '<span class="ei-ficha__meta">' + esc(meta || '') + '</span>' +
            '</span>' +
        '</button>';
    }

    function agregarHtml(tipo, padreClave) {
        return '<button type="button" class="ei-ficha ei-ficha--agregar" data-nuevo="' + tipo + '"' +
            (padreClave ? ' data-padre="' + esc(padreClave) + '"' : '') + '>' +
            '<i class="fas fa-plus"></i> Añadir ' + esc(TIPOS[tipo].etiqueta.toLowerCase()) +
        '</button>';
    }

    // -------------------------------------------- render: rejilla del rack

    /**
     * Vista columna × nivel del rack. Es la forma más rápida de ver qué falta:
     * los huecos son celdas punteadas que crean el nivel que les toca.
     */
    function mapaRackHtml(rack) {
        var columnas = hijosDe(rack.clave, 'columna').sort(function (a, b) {
            return (a.numero || 0) - (b.numero || 0);
        });

        if (!columnas.length) {
            return '<div class="ei-seccion">' +
                seccionCabeceraHtml('Mapa del rack', 'fa-border-all', 0) +
                '<div class="ei-vacio-inline">Este rack todavía no tiene columnas. ' +
                'Usa <strong>Generar estructura</strong> para crear varias columnas y sus niveles de una vez.</div></div>';
        }

        // Filas = números de nivel presentes en cualquiera de las columnas.
        var numeros = new Set();
        columnas.forEach(function (col) {
            hijosDe(col.clave, 'nivel').forEach(function (niv) { numeros.add(niv.numero || 1); });
        });
        var filas = Array.from(numeros).sort(function (a, b) { return b - a; }); // el nivel alto arriba

        var html = '<table class="ei-mapa__tabla"><thead><tr>' +
            '<th class="ei-mapa__esquina">nivel \\ col</th>' +
            columnas.map(function (c) { return '<th>' + esc(c.nombre) + '</th>'; }).join('') +
            '</tr></thead><tbody>';

        filas.forEach(function (num) {
            html += '<tr><th class="ei-mapa__esquina">' + num + '</th>';

            columnas.forEach(function (col) {
                var niv = hijosDe(col.clave, 'nivel').find(function (x) { return (x.numero || 1) === num; });

                if (niv) {
                    var tarimas = hijosDe(niv.clave, 'tarima').length;
                    html += '<td><button type="button" class="ei-celda' + (tarimas ? ' ei-celda--ocupada' : '') +
                        '" data-ir="' + esc(niv.clave) + '" title="' + esc(niv.nombre) + '">' +
                        '<span class="ei-celda__uloc">' + esc(niv.datos.ulocation || niv.nombre) + '</span>' +
                        '<span class="ei-celda__tarimas">' + pluraliza(tarimas, 'tarima', 'tarimas') + '</span>' +
                        '</button></td>';
                } else {
                    html += '<td><button type="button" class="ei-celda ei-celda--hueco" ' +
                        'data-nuevo="nivel" data-padre="' + esc(col.clave) + '" data-num="' + num + '" ' +
                        'title="Crear el nivel ' + num + ' en ' + esc(col.nombre) + '">' +
                        '<i class="fas fa-plus"></i></button></td>';
                }
            });

            html += '</tr>';
        });

        html += '</tbody></table>';

        return '<div class="ei-seccion">' +
            seccionCabeceraHtml('Mapa del rack', 'fa-border-all', columnas.length * filas.length) +
            '<div class="ei-mapa">' + html + '</div></div>';
    }

    // ------------------------------------------------------- drawer: formularios

    /**
     * Definición declarativa de cada formulario: qué campos pide, a qué endpoint
     * va y qué contexto necesita del padre. Añadir un tipo nuevo es añadir una
     * entrada aquí, no tocar el renderizado.
     */
    var FORMULARIOS = {
        sucursal: {
            campos: function (n) {
                return [
                    campoTexto('cve_sucursal', 'Clave de la sucursal', n && n.datos.cve_sucursal, true, 'fa-key'),
                    campoTexto('descripcion', 'Descripción', n && n.datos.descripcion, true, 'fa-align-left')
                ];
            },
            guardar: function (datos, n) {
                return n
                    ? enviar('EditarSucursal', { id_sucursal: n.id, cve_sucursal: datos.cve_sucursal, descripcion: datos.descripcion })
                    : enviar('GuardarSucursal', { cve_sucursal: datos.cve_sucursal, descripcion: datos.descripcion });
            }
        },

        almacen: {
            campos: function (n) {
                return [
                    campoTexto('cve_almacen', 'Clave del almacén', n && n.datos.cve_almacen, true, 'fa-barcode'),
                    campoTexto('descripcion', 'Descripción', n && n.datos.descripcion, true, 'fa-align-left'),
                    campoSelect('tipo', 'Tipo', ['Stock', 'Picking', 'Re-Stock', 'Recepcion'],
                        (n && n.datos.tipo) || 'Stock', true, 'fa-flag')
                ];
            },
            guardar: function (datos, n, padre) {
                // Ojo: EditarAlmacen borra y reinserta los pasillos si recibe el campo
                // `pasillos`. Aquí no se manda nunca — los pasillos son nodos propios.
                var suc = n ? ancestroDe(n.clave, 'sucursal') : ancestroDe(padre.clave, 'sucursal');
                var base = {
                    cve_almacen: datos.cve_almacen,
                    descripcion: datos.descripcion,
                    tipo: datos.tipo,
                    cve_sucursal: suc ? suc.id : ''
                };
                if (n) { base.id_almacen = n.id; return enviar('EditarAlmacen', base); }
                return enviar('GuardarAlmacen', base);
            }
        },

        pasillo: {
            campos: function (n) {
                return [campoTexto('cve_pasillo', 'Clave del pasillo', n && n.datos.cve_pasillo, true, 'fa-road')];
            },
            guardar: function (datos, n, padre) {
                return enviar('GuardarPasillo', {
                    id_pasillo: n ? n.id : '',
                    cve_pasillo: datos.cve_pasillo,
                    almacen_id: n ? ancestroDe(n.clave, 'almacen').id : ancestroDe(padre.clave, 'almacen').id
                });
            }
        },

        rack: {
            campos: function (n, padre) {
                var alm = n ? ancestroDe(n.clave, 'almacen') : ancestroDe(padre.clave, 'almacen');
                var pasillos = alm ? hijosDe(alm.clave, 'pasillo') : [];
                var pasilloActual = n ? ancestroDe(n.clave, 'pasillo') : (padre && padre.tipo === 'pasillo' ? padre : null);

                var opcionesPasillo = [{ valor: '', texto: 'Sin pasillo' }].concat(
                    pasillos.map(function (p) { return { valor: p.id, texto: p.nombre }; }));

                return [
                    campoTexto('nombre', 'Identificador del rack', n && n.datos.nombre, true, 'fa-table-cells-large'),
                    campoSelect('tipo', 'Tipo', ['Rack', 'Estante'], (n && n.datos.tipo) || 'Rack', true, 'fa-flag'),
                    campoSelect('lado', 'Lado', [{ valor: 'I', texto: 'Izquierdo' }, { valor: 'D', texto: 'Derecho' }],
                        (n && n.datos.lado) || 'I', false, 'fa-arrows-left-right',
                        'Solo aplica a estantes: entra en su ULocation.'),
                    campoSelect('pasillo_id', 'Pasillo', opcionesPasillo,
                        pasilloActual ? pasilloActual.id : '', false, 'fa-road')
                ];
            },
            alPintar: function () {
                // El lado solo importa en estantes; se oculta para no pedir datos inertes.
                var tipo = $('#ei-campo-tipo'), grupoLado = $('#ei-grupo-lado');
                if (!tipo || !grupoLado) return;
                var sincroniza = function () {
                    grupoLado.classList.toggle('ei-oculto', tipo.value !== 'Estante');
                };
                tipo.addEventListener('change', sincroniza);
                sincroniza();
            },
            guardar: function (datos, n, padre) {
                var alm = n ? ancestroDe(n.clave, 'almacen') : ancestroDe(padre.clave, 'almacen');
                return enviar('GuardarRack', {
                    id_rack: n ? n.id : '',
                    nombre: datos.nombre,
                    tipo: datos.tipo,
                    lado: datos.lado,
                    pasillo_id: datos.pasillo_id,
                    almacen_id: alm ? alm.id : ''
                });
            }
        },

        columna: {
            campos: function (n) {
                return [campoTexto('nombre', 'Nombre de la columna', n && n.datos.nombre, true, 'fa-grip-lines-vertical')];
            },
            guardar: function (datos, n, padre) {
                return enviar('GuardarColumna', {
                    id_columna: n ? n.id : '',
                    nombre: datos.nombre,
                    rack_id: n ? ancestroDe(n.clave, 'rack').id : ancestroDe(padre.clave, 'rack').id
                });
            }
        },

        nivel: {
            campos: function (n, padre, extra) {
                var num = n ? n.numero : (extra && extra.num);
                return [
                    campoTexto('nombre', 'Nombre del nivel', n ? n.datos.nombre : (num ? 'N' + num : ''), true, 'fa-layer-group'),
                    campoTexto('ulocation', 'ULocation', n && n.datos.ulocation, false, 'fa-map-location-dot',
                        'Se calcula sola a partir del rack, la columna y el número de nivel.'),
                    campoNumero('num_nivel', 'Número de nivel', num, false, 'fa-hashtag')
                ];
            },
            alPintar: async function (n, padre, extra) {
                if (n) return;   // al editar se respeta la ULocation guardada
                var col = padre && padre.tipo === 'columna' ? padre : null;
                if (!col) return;

                var campo = $('#ei-campo-ulocation');
                if (campo) { campo.value = 'Calculando…'; campo.readOnly = true; }

                try {
                    var url = RUTA + '/SugerirULocation?columnaId=' + col.id +
                        (extra && extra.num ? '&numNivel=' + extra.num : '');
                    var r = await pedir(url);
                    if (campo) { campo.value = r.ok ? (r.ulocation || '') : ''; campo.readOnly = false; }

                    var campoNum = $('#ei-campo-num_nivel');
                    if (campoNum && !campoNum.value && r.ok) campoNum.value = r.num_nivel;

                    var campoNombre = $('#ei-campo-nombre');
                    if (campoNombre && !campoNombre.value && r.ok) campoNombre.value = 'N' + r.num_nivel;
                } catch (e) {
                    if (campo) { campo.value = ''; campo.readOnly = false; }
                }
            },
            guardar: function (datos, n, padre) {
                return enviar('GuardarNivel', {
                    id_nivel: n ? n.id : '',
                    nombre: datos.nombre,
                    ulocation: datos.ulocation,
                    num_nivel: datos.num_nivel,
                    columna_id: n ? ancestroDe(n.clave, 'columna').id : ancestroDe(padre.clave, 'columna').id
                });
            }
        },

        tarima: {
            campos: function (n) {
                return [campoTexto('codigo', 'Código de la tarima', n && n.datos.codigo, true, 'fa-barcode')];
            },
            guardar: function (datos, n, padre) {
                return enviar('GuardarTarima', {
                    id_tarima: n ? n.id : '',
                    codigo: datos.codigo,
                    nivel_id: n ? ancestroDe(n.clave, 'nivel').id : ancestroDe(padre.clave, 'nivel').id
                });
            }
        }
    };

    function campoTexto(nombre, etiqueta, valor, requerido, icono, ayuda) {
        return '<div class="ei-campo" id="ei-grupo-' + nombre + '">' +
            '<label class="ei-campo__label" for="ei-campo-' + nombre + '">' +
                '<i class="fas ' + icono + '"></i>' + esc(etiqueta) +
                (requerido ? ' <span class="req">*</span>' : '') +
            '</label>' +
            '<input type="text" id="ei-campo-' + nombre + '" name="' + nombre + '" value="' + esc(valor || '') + '"' +
                (requerido ? ' required' : '') + ' autocomplete="off">' +
            (ayuda ? '<div class="ei-campo__ayuda">' + esc(ayuda) + '</div>' : '') +
        '</div>';
    }

    function campoNumero(nombre, etiqueta, valor, requerido, icono, ayuda) {
        return '<div class="ei-campo" id="ei-grupo-' + nombre + '">' +
            '<label class="ei-campo__label" for="ei-campo-' + nombre + '">' +
                '<i class="fas ' + icono + '"></i>' + esc(etiqueta) +
                (requerido ? ' <span class="req">*</span>' : '') +
            '</label>' +
            '<input type="number" min="1" id="ei-campo-' + nombre + '" name="' + nombre + '" value="' + esc(valor || '') + '"' +
                (requerido ? ' required' : '') + '>' +
            (ayuda ? '<div class="ei-campo__ayuda">' + esc(ayuda) + '</div>' : '') +
        '</div>';
    }

    function campoSelect(nombre, etiqueta, opciones, valor, requerido, icono, ayuda) {
        var html = opciones.map(function (o) {
            var v = (typeof o === 'object') ? o.valor : o;
            var t = (typeof o === 'object') ? o.texto : o;
            return '<option value="' + esc(v) + '"' + (String(v) === String(valor) ? ' selected' : '') + '>' + esc(t) + '</option>';
        }).join('');

        return '<div class="ei-campo" id="ei-grupo-' + nombre + '">' +
            '<label class="ei-campo__label" for="ei-campo-' + nombre + '">' +
                '<i class="fas ' + icono + '"></i>' + esc(etiqueta) +
                (requerido ? ' <span class="req">*</span>' : '') +
            '</label>' +
            '<select id="ei-campo-' + nombre + '" name="' + nombre + '">' + html + '</select>' +
            (ayuda ? '<div class="ei-campo__ayuda">' + esc(ayuda) + '</div>' : '') +
        '</div>';
    }

    // ------------------------------------------------------- drawer: control

    var _drawerCtx = null;

    function abrirDrawer(tipo, nodoEdicion, padre, extra) {
        var def = FORMULARIOS[tipo];
        if (!def) return;

        _drawerCtx = { tipo: tipo, nodo: nodoEdicion, padre: padre, extra: extra };

        var meta = TIPOS[tipo];
        var esNuevo = !nodoEdicion;

        $('#ei-drawer-titulo').innerHTML = (esNuevo ? 'Nuevo ' : 'Editar ') + esc(meta.etiqueta.toLowerCase()) +
            '<span class="ei-drawer__sub">' + esc(esNuevo && padre ? 'en ' + padre.nombre : (nodoEdicion ? nodoEdicion.nombre : 'nivel raíz')) + '</span>';

        var nota = '';
        if (esNuevo && padre) {
            nota = '<div class="ei-nota"><i class="fas fa-circle-info"></i><div>' +
                'Se creará dentro de <strong>' + esc(padre.nombre) + '</strong>' +
                (ancestros(padre.clave).length > 1
                    ? ' <span style="opacity:.7">(' + esc(ancestros(padre.clave).map(function (a) { return a.nombre; }).join(' › ')) + ')</span>'
                    : '') +
                '.</div></div>';
        }

        $('#ei-drawer-cuerpo').innerHTML = nota +
            '<form id="ei-form" novalidate>' + def.campos(nodoEdicion, padre, extra).join('') + '</form>';

        $('#ei-drawer').classList.add('abierto');
        $('#ei-drawer-fondo').classList.add('abierto');

        if (def.alPintar) def.alPintar(nodoEdicion, padre, extra);

        var primero = $('#ei-drawer-cuerpo input, #ei-drawer-cuerpo select');
        if (primero) setTimeout(function () { primero.focus(); }, 260);
    }

    function cerrarDrawer() {
        $('#ei-drawer').classList.remove('abierto');
        $('#ei-drawer-fondo').classList.remove('abierto');
        _drawerCtx = null;
    }

    async function guardarDrawer() {
        if (!_drawerCtx) return;

        var form = $('#ei-form');
        var datos = {};
        Array.prototype.forEach.call(form.elements, function (el) {
            if (el.name) datos[el.name] = (el.value || '').trim();
        });

        var faltante = Array.prototype.find.call(form.elements, function (el) {
            return el.required && !datos[el.name];
        });
        if (faltante) {
            var etiquetaCampo = faltante.closest('.ei-campo');
            etiquetaCampo = etiquetaCampo ? etiquetaCampo.querySelector('.ei-campo__label') : null;
            aviso('error', 'Falta ' + (etiquetaCampo
                ? etiquetaCampo.textContent.replace('*', '').trim().toLowerCase()
                : 'un dato obligatorio'));
            faltante.focus();
            return;
        }

        var boton = $('#ei-drawer-guardar');
        boton.disabled = true;

        try {
            var def = FORMULARIOS[_drawerCtx.tipo];
            var r = await def.guardar(datos, _drawerCtx.nodo, _drawerCtx.padre);

            if (!r.ok) throw new Error(r.msg || r.message || 'No se pudo guardar.');

            var etiqueta = TIPOS[_drawerCtx.tipo].etiqueta;
            aviso('success', etiqueta + (_drawerCtx.nodo ? ' actualizado' : ' creado'));

            // Al crear, se salta al nodo nuevo para poder seguir construyendo dentro.
            var destino = _drawerCtx.nodo
                ? _drawerCtx.nodo.clave
                : (r.id ? clave(_drawerCtx.tipo, r.id) : (_drawerCtx.padre && _drawerCtx.padre.clave));

            cerrarDrawer();
            await cargar(destino);
        } catch (e) {
            aviso('error', 'No se pudo guardar', e.message);
        } finally {
            boton.disabled = false;
        }
    }

    // ---------------------------------------------------------- eliminación

    async function eliminar(n) {
        var meta = TIPOS[n.tipo];
        var descendientes = n.hijos.length;

        var texto = descendientes
            ? 'Se eliminará también todo lo que contiene (' + descendientes + ' elemento(s) directos).'
            : 'Esta acción no se puede deshacer.';

        var confirmado;
        if (typeof window.Swal !== 'undefined') {
            var res = await window.Swal.fire({
                icon: 'warning',
                title: '¿Eliminar ' + meta.etiqueta.toLowerCase() + ' «' + n.nombre + '»?',
                text: texto,
                showCancelButton: true,
                confirmButtonText: 'Eliminar',
                cancelButtonText: 'Cancelar',
                confirmButtonColor: '#dc2626'
            });
            confirmado = res.isConfirmed;
        } else {
            confirmado = window.confirm('¿Eliminar ' + meta.etiqueta.toLowerCase() + ' "' + n.nombre + '"? ' + texto);
        }

        if (!confirmado) return;

        try {
            var r = await enviar('EliminarNodo', { tipo: n.tipo, id: n.id });

            if (!r.ok) {
                // El backend distingue «no se puede» de «falló»: lo primero se explica.
                aviso(r.bloqueado ? 'warning' : 'error',
                      r.bloqueado ? 'No se puede eliminar' : 'Error al eliminar',
                      r.msg);
                return;
            }

            aviso('success', meta.etiqueta + ' eliminado');
            await cargar(n.padre);
        } catch (e) {
            aviso('error', 'Error al eliminar', e.message);
        }
    }

    // ------------------------------------------------------------ generador

    var _genCtx = null;

    function abrirGenerador(n) {
        var alm = ancestroDe(n.clave, 'almacen');
        if (!alm) { aviso('error', 'No se pudo determinar el almacén de destino'); return; }

        var pas = ancestroDe(n.clave, 'pasillo');
        var rack = n.tipo === 'rack' ? n : null;

        _genCtx = { almacen: alm, pasillo: pas, rack: rack };

        var ruta = ancestros(n.clave).map(function (a) { return a.nombre; }).join(' › ');

        var destino = rack
            ? [{ valor: 'rack', texto: 'Añadir columnas y niveles a ' + rack.nombre },
               { valor: 'nuevos', texto: 'Crear racks nuevos junto a él' }]
            : [{ valor: 'nuevos', texto: 'Crear racks nuevos' }];

        $('#ei-gen-form').innerHTML =
            '<div class="ei-nota"><i class="fas fa-location-dot"></i><div>Destino: <strong>' + esc(ruta) + '</strong>' +
                (pas ? '' : '<br><span style="opacity:.75">Los racks se crearán sin pasillo asignado.</span>') +
            '</div></div>' +

            campoSelect('gen_destino', 'Qué generar', destino, rack ? 'rack' : 'nuevos', true, 'fa-bullseye') +

            '<div id="ei-gen-bloque-racks">' +
                '<div class="ei-campo--fila">' +
                    campoSelect('gen_tipo', 'Tipo', ['Rack', 'Estante'], 'Rack', true, 'fa-flag') +
                    campoSelect('gen_lado', 'Lado', [{ valor: 'I', texto: 'Izquierdo' }, { valor: 'D', texto: 'Derecho' }], 'I', false, 'fa-arrows-left-right') +
                '</div>' +
                '<div class="ei-campo--fila">' +
                    campoNumero('gen_racks', 'Cuántos racks', 1, true, 'fa-table-cells-large') +
                    campoTexto('gen_racks_prefijo', 'Prefijo', 'R', false, 'fa-font') +
                    campoNumero('gen_racks_desde', 'Numerar desde', '', false, 'fa-hashtag') +
                '</div>' +
            '</div>' +

            '<div class="ei-campo--fila">' +
                campoNumero('gen_columnas', 'Columnas por rack', 4, true, 'fa-grip-lines-vertical') +
                campoTexto('gen_columnas_prefijo', 'Prefijo', 'C', false, 'fa-font') +
            '</div>' +

            '<div class="ei-campo--fila">' +
                campoNumero('gen_niveles', 'Niveles por columna', 4, true, 'fa-layer-group') +
                campoTexto('gen_niveles_prefijo', 'Prefijo', 'N', false, 'fa-font') +
            '</div>';

        $('#ei-gen-previa').innerHTML =
            '<div class="ei-previa__espera"><div><i class="fas fa-wand-magic-sparkles fa-lg d-block mb-2"></i>' +
            'Ajusta las cantidades para ver aquí exactamente qué se creará.</div></div>';

        $('#ei-gen-fondo').classList.add('abierto');

        sincronizarGenerador();
        programarPrevia();
    }

    function cerrarGenerador() {
        $('#ei-gen-fondo').classList.remove('abierto');
        _genCtx = null;
    }

    /** Muestra/oculta lo que no aplica según lo que se esté generando. */
    function sincronizarGenerador() {
        var destino = $('#ei-campo-gen_destino');
        var bloque = $('#ei-gen-bloque-racks');
        var lado = $('#ei-grupo-gen_lado');
        var tipo = $('#ei-campo-gen_tipo');

        if (bloque && destino) bloque.classList.toggle('ei-oculto', destino.value === 'rack');
        if (lado && tipo) lado.classList.toggle('ei-oculto', tipo.value !== 'Estante');
    }

    function parametrosGenerador() {
        if (!_genCtx) return null;

        var destino = $('#ei-campo-gen_destino');
        var aRack = destino && destino.value === 'rack' && _genCtx.rack;

        function val(id, porDefecto) {
            var el = $('#ei-campo-' + id);
            var v = el ? (el.value || '').trim() : '';
            return v === '' ? (porDefecto === undefined ? '' : porDefecto) : v;
        }

        return {
            almacen_id: _genCtx.almacen.id,
            pasillo_id: _genCtx.pasillo ? _genCtx.pasillo.id : '',
            rack_id: aRack ? _genCtx.rack.id : '',
            tipo: val('gen_tipo', 'Rack'),
            lado: val('gen_lado', 'I'),
            racks_cantidad: aRack ? 0 : val('gen_racks', 1),
            racks_prefijo: val('gen_racks_prefijo', 'R'),
            racks_desde: val('gen_racks_desde', ''),
            columnas_cantidad: val('gen_columnas', 0),
            columnas_prefijo: val('gen_columnas_prefijo', 'C'),
            niveles_cantidad: val('gen_niveles', 0),
            niveles_prefijo: val('gen_niveles_prefijo', 'N')
        };
    }

    var _timerPrevia = null;
    function programarPrevia() {
        clearTimeout(_timerPrevia);
        _timerPrevia = setTimeout(refrescarPrevia, 260);
    }

    async function refrescarPrevia() {
        var params = parametrosGenerador();
        if (!params) return;

        var mio = ++_tokenPrevia;
        var cont = $('#ei-gen-previa');
        var boton = $('#ei-gen-crear');

        try {
            var r = await enviar('PreviewEstructura', params);
            if (mio !== _tokenPrevia) return;   // llegó tarde: ya hay otra previa más nueva

            if (!r.ok) {
                cont.innerHTML = '<div class="ei-previa__cuerpo"><div class="ei-nota ei-nota--error">' +
                    '<i class="fas fa-triangle-exclamation"></i><div>' + esc(r.msg) + '</div></div></div>';
                boton.disabled = true;
                return;
            }

            var hayDuplicadas = (r.avisos || []).some(function (a) { return a.indexOf('ULocation duplicada') === 0; });
            var noHayNada = (r.totales.racks === 0 && r.totales.columnas === 0 && r.totales.niveles === 0);
            boton.disabled = hayDuplicadas || noHayNada;

            cont.innerHTML = previaHtml(r);
        } catch (e) {
            if (mio !== _tokenPrevia) return;
            cont.innerHTML = '<div class="ei-previa__cuerpo"><div class="ei-nota ei-nota--error">' +
                '<i class="fas fa-triangle-exclamation"></i><div>' + esc(e.message) + '</div></div></div>';
            boton.disabled = true;
        }
    }

    function previaHtml(r) {
        var resumen = '<div class="ei-previa__resumen">' +
            '<div class="ei-metrica"><span class="ei-metrica__valor">' + r.totales.racks + '</span>racks</div>' +
            '<div class="ei-metrica"><span class="ei-metrica__valor">' + r.totales.columnas + '</span>columnas</div>' +
            '<div class="ei-metrica"><span class="ei-metrica__valor">' + r.totales.niveles + '</span>niveles</div>' +
        '</div>';

        var avisos = (r.avisos || []).map(function (a) {
            var esError = a.indexOf('ULocation duplicada') === 0;
            return '<div class="ei-nota ' + (esError ? 'ei-nota--error' : 'ei-nota--aviso') + '">' +
                '<i class="fas fa-triangle-exclamation"></i><div>' + esc(a) + '</div></div>';
        }).join('');

        var racks = (r.racks || []).map(function (rk) {
            var ulocs = [];
            (rk.columnas || []).forEach(function (c) {
                (c.niveles || []).forEach(function (n) { ulocs.push(n.ulocation); });
            });

            // Con 6 columnas × 5 niveles serían 30 chips por rack: se recortan.
            var visibles = ulocs.slice(0, 18);
            var resto = ulocs.length - visibles.length;

            return '<div class="ei-previa__rack">' +
                '<div class="ei-previa__rack-nombre">' +
                    '<i class="fas fa-table-cells-large"></i>' + esc(rk.nombre) +
                    (rk.es_nuevo ? '<span class="ei-etiqueta-nuevo">nuevo</span>'
                                 : '<span class="ei-etiqueta-existente">existente</span>') +
                '</div>' +
                '<div class="ei-previa__ulocs">' +
                    visibles.map(function (u) { return '<span class="ei-uloc">' + esc(u) + '</span>'; }).join('') +
                    (resto > 0 ? '<span class="ei-uloc ei-uloc--mas">+' + resto + ' más</span>' : '') +
                    (ulocs.length === 0 ? '<span class="ei-uloc ei-uloc--mas">sin niveles</span>' : '') +
                '</div>' +
            '</div>';
        }).join('');

        return '<div class="ei-previa__cuerpo">' + resumen + avisos + racks + '</div>';
    }

    async function ejecutarGenerador() {
        var params = parametrosGenerador();
        if (!params) return;

        var boton = $('#ei-gen-crear');
        boton.disabled = true;
        boton.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Creando…';

        try {
            var r = await enviar('GenerarEstructura', params);
            if (!r.ok) throw new Error(r.msg || 'No se pudo generar la estructura.');

            aviso('success', 'Estructura creada',
                r.racks + ' racks, ' + r.columnas + ' columnas y ' + r.niveles + ' niveles.');

            var destino = (r.ids_racks && r.ids_racks.length)
                ? clave('rack', r.ids_racks[0])
                : _sel;

            cerrarGenerador();
            await cargar(destino);
        } catch (e) {
            aviso('error', 'No se pudo generar', e.message);
        } finally {
            boton.disabled = false;
            boton.innerHTML = '<i class="fas fa-bolt"></i> Crear estructura';
        }
    }

    // ---------------------------------------------------------- navegación

    function seleccionar(k) {
        if (k === '__raiz') { _sel = null; pintarArbol(); pintarPanel(); return; }
        if (!nodo(k)) return;

        _sel = k;
        // Abrir toda la rama hasta el nodo, para que se vea dónde estás parado.
        ancestros(k).forEach(function (a) { _abiertos.add(a.clave); });
        _abiertos.add(k);

        pintarArbol();
        pintarPanel();

        var fila = document.querySelector('.ei-nodo__fila[data-clave="' + k.replace(/"/g, '\\"') + '"]');
        if (fila && fila.scrollIntoView) fila.scrollIntoView({ block: 'nearest' });
    }

    function alternar(k) {
        if (_abiertos.has(k)) _abiertos.delete(k); else _abiertos.add(k);
        pintarArbol();
    }

    // ------------------------------------------------------------- carga

    async function cargar(seleccionar_) {
        if (_cargando) return;
        _cargando = true;

        var panel = $('#ei-panel');
        if (panel && !_indice.size) {
            panel.innerHTML = '<div class="ei-tarjeta"><div class="ei-cargando">' +
                '<i class="fas fa-spinner fa-spin"></i>Cargando la estructura…</div></div>';
        }

        try {
            var r = await pedir(RUTA + '/Arbol');
            if (!r.ok) throw new Error(r.msg || 'No se pudo cargar el árbol.');

            // La cadena de ancestros de ANTES de reindexar es la red de seguridad:
            // si el nodo seleccionado desapareció (lo acabamos de borrar), se cae
            // al primer ancestro que siga existiendo en el árbol nuevo.
            var respaldo = _sel ? ancestros(_sel).map(function (a) { return a.clave; }).reverse() : [];

            _arbol = r.data || [];
            indexar();

            var destino = seleccionar_ !== undefined ? seleccionar_ : _sel;

            if (destino && !_indice.has(destino)) {
                destino = respaldo.find(function (k) { return _indice.has(k); }) || null;
            }
            _sel = destino && _indice.has(destino) ? destino : null;

            if (_sel) ancestros(_sel).forEach(function (a) { _abiertos.add(a.clave); });

            actualizarResumen();
            pintarArbol();
            pintarPanel();
        } catch (e) {
            if (panel) {
                panel.innerHTML = '<div class="ei-tarjeta"><div class="ei-seccion">' +
                    '<div class="ei-nota ei-nota--error"><i class="fas fa-triangle-exclamation"></i>' +
                    '<div><strong>No se pudo cargar la estructura.</strong><br>' + esc(e.message) + '</div></div>' +
                    '</div></div>';
            }
            aviso('error', 'No se pudo cargar la estructura', e.message);
        } finally {
            _cargando = false;
        }
    }

    function actualizarResumen() {
        var el = $('#ei-resumen');
        if (!el) return;

        var totales = { sucursal: 0, almacen: 0, rack: 0, nivel: 0, tarima: 0 };
        _indice.forEach(function (n) { if (totales[n.tipo] !== undefined) totales[n.tipo]++; });

        el.textContent = totales.sucursal + ' sucursales · ' + totales.almacen + ' almacenes · ' +
            totales.rack + ' racks · ' + totales.nivel + ' niveles · ' + totales.tarima + ' tarimas';
    }

    // -------------------------------------------------------------- eventos

    function conectar() {
        // Árbol: clic en la flecha alterna, clic en la fila selecciona.
        $('#ei-arbol-lista').addEventListener('click', function (e) {
            var fila = e.target.closest('.ei-nodo__fila');
            if (!fila) return;

            var k = fila.dataset.clave;
            if (e.target.closest('.ei-nodo__flecha')) { alternar(k); return; }
            seleccionar(k);
        });

        $('#ei-arbol-lista').addEventListener('keydown', function (e) {
            var fila = e.target.closest('.ei-nodo__fila');
            if (!fila) return;
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); seleccionar(fila.dataset.clave); }
            if (e.key === 'ArrowRight') { _abiertos.add(fila.dataset.clave); pintarArbol(); }
            if (e.key === 'ArrowLeft') { _abiertos.delete(fila.dataset.clave); pintarArbol(); }
        });

        // Panel: navegar, crear y las acciones de la cabecera.
        $('#ei-panel').addEventListener('click', function (e) {
            var ir = e.target.closest('[data-ir]');
            if (ir) { seleccionar(ir.dataset.ir); return; }

            var nuevo = e.target.closest('[data-nuevo]');
            if (nuevo) {
                var padre = nuevo.dataset.padre ? nodo(nuevo.dataset.padre) : null;
                var extra = nuevo.dataset.num ? { num: parseInt(nuevo.dataset.num, 10) } : null;
                abrirDrawer(nuevo.dataset.nuevo, null, padre, extra);
                return;
            }

            var accion = e.target.closest('[data-accion]');
            if (!accion) return;

            var n = nodo(_sel);
            if (!n) return;

            if (accion.dataset.accion === 'editar') abrirDrawer(n.tipo, n, nodo(n.padre));
            if (accion.dataset.accion === 'eliminar') eliminar(n);
            if (accion.dataset.accion === 'generar') abrirGenerador(n);
        });

        // Buscador
        var buscador = $('#ei-buscar');
        var timer = null;
        buscador.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(function () {
                _filtro = buscador.value.trim().toLowerCase();
                pintarArbol();
            }, 180);
        });

        // Barra
        $('#ei-recargar').addEventListener('click', function () { cargar(_sel); });

        $('#ei-colapsar').addEventListener('click', function () {
            if (_abiertos.size) { _abiertos.clear(); }
            else { _indice.forEach(function (n) { if (n.hijos.length) _abiertos.add(n.clave); }); }
            pintarArbol();
        });

        $('#ei-nueva-sucursal').addEventListener('click', function () { abrirDrawer('sucursal', null, null); });

        // Drawer
        $('#ei-drawer-cerrar').addEventListener('click', cerrarDrawer);
        $('#ei-drawer-cancelar').addEventListener('click', cerrarDrawer);
        $('#ei-drawer-fondo').addEventListener('click', cerrarDrawer);
        $('#ei-drawer-guardar').addEventListener('click', guardarDrawer);

        $('#ei-drawer').addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && e.target.tagName === 'INPUT') { e.preventDefault(); guardarDrawer(); }
        });

        // Generador. Los oyentes van en el contenedor fijo, no en el HTML que se
        // regenera al abrirlo, para que no se acumulen en cada apertura.
        $('#ei-gen-form').addEventListener('input', programarPrevia);
        $('#ei-gen-form').addEventListener('change', function () {
            sincronizarGenerador();
            programarPrevia();
        });

        $('#ei-gen-cerrar').addEventListener('click', cerrarGenerador);
        $('#ei-gen-cancelar').addEventListener('click', cerrarGenerador);
        $('#ei-gen-crear').addEventListener('click', ejecutarGenerador);
        $('#ei-gen-fondo').addEventListener('click', function (e) {
            if (e.target === e.currentTarget) cerrarGenerador();
        });

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if ($('#ei-gen-fondo').classList.contains('abierto')) { cerrarGenerador(); return; }
            if ($('#ei-drawer').classList.contains('abierto')) cerrarDrawer();
        });
    }

    // ------------------------------------------------------------- arranque

    function iniciar() {
        if (!$('#ei-arbol-lista')) return;
        conectar();
        cargar();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', iniciar);
    } else {
        iniciar();
    }

    window.EstructuraInventario = {
        recargar: cargar,
        seleccionar: seleccionar,
        get arbol() { return _arbol; }
    };

})(window, document);
