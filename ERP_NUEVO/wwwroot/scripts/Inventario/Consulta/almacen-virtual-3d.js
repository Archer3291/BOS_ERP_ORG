/**
 * almacen-virtual-3d.js
 * ─────────────────────────────────────────────────────────────────
 * Vista 3D del plano de almacén (Three.js).
 *
 * Consume EXACTAMENTE los mismos datos que el motor 2D de Konva:
 *   plano      → { ancho_m, alto_m, escala_px_m, almacenes[], racks[] }
 *   inventario → { sucursales[ { almacenes[ { pasillos[ { racks[] } ] } ] } ] }
 *
 * Las posiciones guardadas en px del plano se convierten a metros con
 * `escala_px_m` y se proyectan al plano XZ (Y = altura).
 *
 * Se expone como `window.AV3D` y avisa con el evento `av3d:ready`.
 */

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import * as BGU from 'three/addons/utils/BufferGeometryUtils.js';

const mergeGeoms = BGU.mergeGeometries || BGU.mergeBufferGeometries;

const AV3D = (() => {

    'use strict';

    // ═══════════════════════════════════════════════════════════════
    //  CONFIGURACIÓN
    // ═══════════════════════════════════════════════════════════════
    const CFG_KEY = 'av3d.cfg.v1';

    const CFG = Object.assign({
        nivelH: 1.5,        // altura de cada nivel del rack (m)
        muroH: 8,           // altura de muros perimetrales (m)
        verMuros: true,
        verEtiquetas: true,
        verUbicaciones: true,   // ulocation en las vigas, al acercarse
        verTarimas: true,
        verSombras: true,
        verZonas: true,
    }, _leerCfg());

    function _leerCfg() {
        try { return JSON.parse(localStorage.getItem(CFG_KEY)) || {}; }
        catch { return {}; }
    }

    function _guardarCfg() {
        try { localStorage.setItem(CFG_KEY, JSON.stringify(CFG)); } catch { /* noop */ }
    }

    // Medidas físicas de referencia (m)
    const M = {
        posteW: 0.10,      // sección del poste vertical
        vigaH: 0.12,      // alto de la viga horizontal
        vigaD: 0.05,      // espesor de la viga
        deckH: 0.035,     // espesor del piso de nivel
        baseH: 0.12,      // altura del larguero base
        tarimaW: 1.20,      // ancho estándar de tarima
        tarimaD: 1.00,      // fondo estándar de tarima
        tarimaH: 0.14,      // alto de la tarima de madera
        cargaMin: 0.35,      // alto mínimo de la carga
        cargaMax: 1.05,      // alto máximo de la carga
    };

    // ═══════════════════════════════════════════════════════════════
    //  ESTADO
    // ═══════════════════════════════════════════════════════════════
    let _renderer, _scene, _camera, _controls, _raycaster, _clock;
    let _container = null;
    let _mounted = false;
    let _dirty = true;
    let _resizeObs = null;

    let _grpMundo = null;      // piso, muros, grid
    let _grpZonas = null;      // losas de almacén
    let _grpRacks = null;      // racks (estructura, tarimas, halos y etiquetas)

    let _muros = [];        // { mesh, normal }
    let _hitboxes = [];        // cajas invisibles de rack, para raycast
    let _tarimaMeshes = [];        // mallas fusionadas de tarimas, con índice de slots
    let _racksIdx = new Map();  // rackId → { grupo, hitbox, meta, materiales[] }
    let _zonasIdx = new Map();  // almacenId → { grupo, meta }
    let _labels = [];        // sprites con franja de distancia

    let _hoverId = null;        // rack bajo el cursor
    let _hoverTarima = null;    // { slot, rack, alm } bajo el cursor
    let _cajaHover = null;      // marco que resalta la tarima apuntada
    let _selId = null;
    let _anilloSel = null;

    let _datos = null;      // { plano, inventario, sucursalId }
    let _bbox = new THREE.Box3();
    let _flying = null;      // animación de cámara en curso

    let _nave = { anchoM: 0, largoM: 0 };   // superficie útil del piso
    let _huellas = [];      // AABB en mundo de cada rack, para no aterrizar dentro
    let _huellasZona = [];      // ídem por almacén, para saber en qué zona caes

    let _onRackClick = null;
    let _onRackHover = null;
    let _onTarimaClick = null;

    const _tmpV = new THREE.Vector3();
    const _mouseNDC = new THREE.Vector2();

    // ═══════════════════════════════════════════════════════════════
    //  TEMA — lee las variables CSS del módulo
    // ═══════════════════════════════════════════════════════════════
    function _cssVar(name, fallback) {
        const v = getComputedStyle(document.documentElement)
            .getPropertyValue(name).trim();
        return v || fallback;
    }

    let TEMA = null;

    function _leerTema() {
        const esOscuro = _esTemaOscuro();
        TEMA = {
            oscuro: esOscuro,
            accent: new THREE.Color(_cssVar('--av-accent', '#00d4ff')),
            accent2: new THREE.Color(_cssVar('--av-accent2', '#0066cc')),
            success: new THREE.Color(_cssVar('--av-success', '#10b981')),
            warn: new THREE.Color(_cssVar('--av-warn', '#f59e0b')),
            danger: new THREE.Color(_cssVar('--av-danger', '#ef4444')),
            fondo: new THREE.Color(esOscuro ? '#0a1220' : '#dde5ee'),
            fondoAlto: new THREE.Color(esOscuro ? '#132038' : '#f4f7fb'),
            piso: new THREE.Color(esOscuro ? '#454f5e' : '#b9c1cb'),
            pisoLinea: new THREE.Color(esOscuro ? '#5a6678' : '#a3adba'),
            muro: new THREE.Color(esOscuro ? '#1b2637' : '#e8edf3'),
            muroTrim: new THREE.Color(esOscuro ? '#2b3a52' : '#ccd6e2'),
            acero: new THREE.Color(esOscuro ? '#5b6b80' : '#8b97a6'),
            viga: new THREE.Color('#e07b1f'),   // naranja industrial
            madera: new THREE.Color('#b5854a'),
            pintura: new THREE.Color('#e8c33a'),   // amarillo de señalización
            texto: _cssVar('--av-text', '#e2e8f0'),
            textoTenue: _cssVar('--av-text-muted', '#64748b'),
            superficie: _cssVar('--av-surface', '#0f1a2b'),
        };
        return TEMA;
    }

    function _esTemaOscuro() {
        // El módulo hereda el tema global del ERP; deducimos por luminancia
        // del color de fondo real para no depender de un atributo concreto.
        const bg = _cssVar('--av-bg', '') || _cssVar('--bg-gray', '#0f172a');
        const c = new THREE.Color(bg);
        return (c.r * 0.299 + c.g * 0.587 + c.b * 0.114) < 0.45;
    }

    // ═══════════════════════════════════════════════════════════════
    //  OCUPACIÓN (mismo contrato que el motor 2D)
    // ═══════════════════════════════════════════════════════════════
    const MAP_TXT = {
        lleno: 1, full: 1, alto: 1, completo: 1, ocupado: 1, high: 1,
        medio: .55, medium: .55, med: .55, parcial: .55, partial: .55, mitad: .55, half: .55,
        bajo: .2, low: .2, poco: .2, escaso: .2, minimo: .2,
        vacio: 0, empty: 0, libre: 0, disponible: 0, free: 0, nulo: 0,
    };

    function _normCap(v) {
        return (v ?? '').toString().trim().toLowerCase()
            .normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }

    function _pctDesde(capRaw, usados, fallbackCap) {
        const s = _normCap(capRaw);
        if (s !== '' && MAP_TXT[s] !== undefined) return MAP_TXT[s];

        const mPct = s.match(/^(\d+(?:\.\d+)?)\s*%$/);
        if (mPct) return Math.min(1, parseFloat(mPct[1]) / 100);

        const mFrac = s.match(/^(\d+)\s*\/\s*(\d+)$/);
        if (mFrac) {
            const den = parseFloat(mFrac[2]);
            return den > 0 ? Math.min(1, parseFloat(mFrac[1]) / den) : 0;
        }

        const num = parseFloat(s.replace(',', '.'));
        if (!isNaN(num) && num > 0) return Math.min(1, usados / num);

        return fallbackCap > 0 ? Math.min(1, usados / fallbackCap) : 0;
    }

    function _ocupNivel(nivel) {
        const usados = (nivel.tarimas || []).length;
        return _pctDesde(nivel.capacidad, usados, 4);
    }

    function _ocupRack(rack) {
        const cols = rack.columnas || [];
        let usados = 0, total = 0;
        cols.forEach(c => (c.niveles || []).forEach(n => {
            usados += (n.tarimas || []).length;
            const cn = parseFloat(_normCap(n.capacidad).replace(',', '.'));
            total += isNaN(cn) || cn <= 0 ? 4 : cn;
        }));
        const pct = _pctDesde(rack.capacidad, usados, total);
        return { pct, usados, total };
    }

    function _colorOcup(pct) {
        if (pct >= 0.9) return TEMA.danger;
        if (pct >= 0.5) return TEMA.warn;
        if (pct > 0) return TEMA.success;
        return TEMA.accent2;
    }

    function _etiquetaOcup(pct) {
        return Math.round(pct * 100) + '%';
    }

    // ═══════════════════════════════════════════════════════════════
    //  INICIALIZACIÓN DE LA ESCENA
    // ═══════════════════════════════════════════════════════════════
    let _initFallo = false;

    function init(containerId) {
        if (_initFallo) return false;
        _container = document.getElementById(containerId || 'av-3d-container');
        if (!_container || _renderer) return !!_renderer;

        _leerTema();

        try {
            _renderer = new THREE.WebGLRenderer({
                antialias: true,
                alpha: false,
                powerPreference: 'high-performance',
            });
        } catch (err) {
            // Sin WebGL (driver bloqueado, VM sin GPU, etc.)
            console.warn('[AV3D] WebGL no disponible:', err);
            _renderer = null;
            _initFallo = true;
            return false;
        }

        _renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        _renderer.setSize(_ancho(), _alto(), false);
        _renderer.outputColorSpace = THREE.SRGBColorSpace;
        _renderer.toneMapping = THREE.ACESFilmicToneMapping;
        _renderer.toneMappingExposure = 1.05;
        _renderer.shadowMap.enabled = CFG.verSombras;
        _renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        _container.appendChild(_renderer.domElement);

        _scene = new THREE.Scene();
        _scene.background = _texturaCielo();
        _scene.fog = new THREE.Fog(TEMA.fondo, 80, 400);

        _camera = new THREE.PerspectiveCamera(45, _ancho() / _alto(), 0.5, 3000);
        _camera.position.set(45, 40, 65);

        _controls = new OrbitControls(_camera, _renderer.domElement);
        _controls.enableDamping = true;
        _controls.dampingFactor = 0.075;
        _controls.screenSpacePanning = false;
        _controls.minDistance = 4;
        _controls.maxDistance = 900;
        _controls.maxPolarAngle = Math.PI * 0.495;   // nunca bajo el piso
        _controls.mouseButtons = {
            LEFT: THREE.MOUSE.ROTATE,
            MIDDLE: THREE.MOUSE.DOLLY,
            RIGHT: THREE.MOUSE.PAN,
        };
        _controls.touches = { ONE: THREE.TOUCH.ROTATE, TWO: THREE.TOUCH.DOLLY_PAN };

        _raycaster = new THREE.Raycaster();
        _clock = new THREE.Clock();

        _grpMundo = new THREE.Group();
        _grpZonas = new THREE.Group();
        _grpRacks = new THREE.Group();
        _scene.add(_grpMundo, _grpZonas, _grpRacks);

        _luces();
        _bindEventos();

        _resizeObs = new ResizeObserver(_onResize);
        _resizeObs.observe(_container);

        _renderer.setAnimationLoop(_loop);
        return true;
    }

    function _ancho() { return Math.max(1, _container?.clientWidth || 1); }
    function _alto() { return Math.max(1, _container?.clientHeight || 1); }

    // ── Iluminación ──────────────────────────────────────────────
    let _sol = null, _hemi = null, _amb = null;

    function _luces() {
        // En tema oscuro se sube la luz de relleno: sin ella los racks quedan
        // como siluetas negras sobre el piso y no se leen las tarimas.
        _hemi = new THREE.HemisphereLight(
            TEMA.oscuro ? 0xa8c6e4 : 0xffffff,
            TEMA.oscuro ? 0x24304a : 0x8d97a3,
            TEMA.oscuro ? 1.9 : 1.5
        );
        _hemi.position.set(0, 60, 0);

        _amb = new THREE.AmbientLight(0xffffff, TEMA.oscuro ? 0.55 : 0.4);

        _sol = new THREE.DirectionalLight(0xfff3e0, TEMA.oscuro ? 2.1 : 2.0);
        _sol.position.set(60, 90, 40);
        _sol.castShadow = CFG.verSombras;
        _sol.shadow.mapSize.set(2048, 2048);
        _sol.shadow.bias = -0.0008;
        _sol.shadow.normalBias = 0.03;

        _scene.add(_hemi, _amb, _sol, _sol.target);
    }

    function _ajustarSombras(radio) {
        if (!_sol) return;
        // El sol es oblicuo: la huella proyectada es mayor que el radio real,
        // por eso se sobredimensiona la cámara ortográfica de sombras.
        const r = Math.max(20, radio * 1.8);
        const c = _sol.shadow.camera;
        c.left = -r; c.right = r; c.top = r; c.bottom = -r;
        c.near = 1; c.far = r * 5;
        c.updateProjectionMatrix();
        _sol.position.set(r * 0.6, r * 1.2, r * 0.45);
        _sol.target.position.set(0, 0, 0);
        _sol.target.updateMatrixWorld();
    }

    // ── Fondo degradado ──────────────────────────────────────────
    function _texturaCielo() {
        const cv = document.createElement('canvas');
        cv.width = 4; cv.height = 256;
        const ctx = cv.getContext('2d');
        const g = ctx.createLinearGradient(0, 0, 0, 256);
        g.addColorStop(0, '#' + TEMA.fondoAlto.getHexString());
        g.addColorStop(0.55, '#' + TEMA.fondo.getHexString());
        g.addColorStop(1, '#' + TEMA.fondo.clone().multiplyScalar(0.82).getHexString());
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, 4, 256);
        const t = new THREE.CanvasTexture(cv);
        t.colorSpace = THREE.SRGBColorSpace;
        t.mapping = THREE.EquirectangularReflectionMapping;
        return t;
    }

    // ── Textura procedural de concreto ───────────────────────────
    let _texPiso = null;

    function _texturaConcreto() {
        if (_texPiso) return _texPiso;
        const S = 256;
        const cv = document.createElement('canvas');
        cv.width = cv.height = S;
        const ctx = cv.getContext('2d');

        ctx.fillStyle = '#' + TEMA.piso.getHexString();
        ctx.fillRect(0, 0, S, S);

        // Grano fino
        const img = ctx.getImageData(0, 0, S, S);
        const d = img.data;
        for (let i = 0; i < d.length; i += 4) {
            const n = (Math.random() - 0.5) * 22;
            d[i] += n; d[i + 1] += n; d[i + 2] += n;
        }
        ctx.putImageData(img, 0, 0);

        // Manchas suaves — muy tenues: al repetirse decenas de veces sobre el
        // piso, cualquier mancha marcada delata el teselado de la textura.
        for (let i = 0; i < 26; i++) {
            const x = Math.random() * S, y = Math.random() * S;
            const r = 12 + Math.random() * 46;
            const g = ctx.createRadialGradient(x, y, 0, x, y, r);
            const a = 0.012 + Math.random() * 0.022;
            g.addColorStop(0, `rgba(0,0,0,${a})`);
            g.addColorStop(1, 'rgba(0,0,0,0)');
            ctx.fillStyle = g;
            ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2); ctx.fill();
        }

        // Juntas de losa
        ctx.strokeStyle = '#' + TEMA.pisoLinea.getHexString();
        ctx.globalAlpha = 0.55;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(0, 0.5); ctx.lineTo(S, 0.5);
        ctx.moveTo(0.5, 0); ctx.lineTo(0.5, S);
        ctx.stroke();
        ctx.globalAlpha = 1;

        _texPiso = new THREE.CanvasTexture(cv);
        _texPiso.colorSpace = THREE.SRGBColorSpace;
        _texPiso.wrapS = _texPiso.wrapT = THREE.RepeatWrapping;
        _texPiso.anisotropy = _renderer?.capabilities.getMaxAnisotropy?.() || 4;
        return _texPiso;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN DEL MUNDO
    // ═══════════════════════════════════════════════════════════════

    /**
     * Construye (o reconstruye) toda la escena.
     * @param {{plano:Object, inventario:Object, sucursalId:number}} datos
     */
    function build(datos, opts = {}) {
        if (!init()) return;
        _datos = datos;
        _leerTema();

        // Al reconstruir por un cambio de configuración se conserva el encuadre
        const camPrev = opts.mantenerCamara
            ? { pos: _camera.position.clone(), target: _controls.target.clone() }
            : null;

        _limpiarGrupo(_grpMundo);
        _limpiarGrupo(_grpZonas);
        _limpiarGrupo(_grpRacks);
        _muros = []; _hitboxes = []; _tarimaMeshes = []; _labels = [];
        _racksIdx.clear(); _zonasIdx.clear();
        _hoverId = null; _selId = null; _anilloSel = null;
        _hoverTarima = null; _cajaHover = null; _marca = null;
        _huellas = []; _huellasZona = [];
        _quitarBaliza();
        _quitarFantasma();

        const plano = datos?.plano;
        if (!plano) { _dirty = true; return; }

        const S = parseFloat(plano.escala_px_m) || 8;      // px por metro
        const almacenes = _almacenesDeSucursal(datos);
        const posAlm = _indexar(plano.almacenes || [], 'almacen_id');
        const posRack = _indexar(plano.racks || [], 'rack_id');

        // 1. Recolectar todas las cajas en px para conocer la extensión real
        const items = _recolectar(almacenes, posAlm, posRack);

        let minX = 0, minY = 0, maxX = (parseFloat(plano.ancho_m) || 100) * S;
        let maxY = (parseFloat(plano.alto_m) || 60) * S;
        items.forEach(it => {
            minX = Math.min(minX, it.x);
            minY = Math.min(minY, it.y);
            maxX = Math.max(maxX, it.x + it.ancho);
            maxY = Math.max(maxY, it.y + it.alto);
        });

        const margen = 6 * S;   // 6 m de holgura hasta los muros
        minX -= margen; minY -= margen; maxX += margen; maxY += margen;

        const cx = (minX + maxX) / 2;
        const cy = (minY + maxY) / 2;
        const anchoM = (maxX - minX) / S;
        const largoM = (maxY - minY) / S;

        // Conversores px → mundo (metros, centrado en el origen)
        const wx = px => (px - cx) / S;
        const wz = py => (py - cy) / S;
        const wm = px => px / S;

        _piso(anchoM, largoM);
        if (CFG.verMuros) _murosPerimetrales(anchoM, largoM);

        // 2. Zonas de almacén
        almacenes.forEach((alm, i) => {
            const p = posAlm[alm.id] || _posAlmacenDefault(i);
            _zona(alm, p, wx, wz, wm);
        });

        // 3. Racks
        almacenes.forEach((alm, ai) => {
            const pA = posAlm[alm.id] || _posAlmacenDefault(ai);
            _racksDeAlmacen(alm).forEach(({ rack, idx }) => {
                const p = posRack[rack.id] || _posRackDefault(pA, idx);
                _rack(rack, alm, p, wx, wz, wm);
            });
        });

        _nave = { anchoM, largoM };

        // Huellas en mundo, calculadas una sola vez: el monito las consulta en
        // cada pointermove y hacerlo con setFromObject ahí dentro se notaría.
        _racksIdx.forEach((entry, id) => {
            _huellas.push({ caja: new THREE.Box3().setFromObject(entry.hitbox), id, meta: entry.meta });
        });
        _zonasIdx.forEach((entry, id) => {
            _huellasZona.push({ caja: new THREE.Box3().setFromObject(entry.grupo), id, meta: entry.meta });
        });

        _bbox.setFromObject(_grpRacks);
        if (_bbox.isEmpty()) _bbox.setFromObject(_grpZonas);
        if (_bbox.isEmpty()) {
            _bbox.set(new THREE.Vector3(-anchoM / 2, 0, -largoM / 2),
                new THREE.Vector3(anchoM / 2, 8, largoM / 2));
        }

        const radio = Math.max(anchoM, largoM) / 2;
        _ajustarSombras(radio);
        // La niebla solo debe morder en el horizonte, nunca sobre el almacén:
        // la distancia típica de cámara al encuadrar ronda 3.5·radio.
        _scene.fog.near = radio * 3.5;
        _scene.fog.far = radio * 12;

        if (camPrev) {
            _camera.position.copy(camPrev.pos);
            _controls.target.copy(camPrev.target);
            _controls.update();
        } else {
            zoomFit(false);
        }
        _dirty = true;
    }

    function _almacenesDeSucursal(datos) {
        const sucs = datos?.inventario?.sucursales || [];
        const s = sucs.find(x => x.id === datos.sucursalId) || sucs[0];
        return s?.almacenes || [];
    }

    /**
     * Racks de un almacén junto con el índice que usa el motor 2D para
     * calcular su posición por defecto. Se replica ese mismo criterio
     * (índice relativo al pasillo) para que ambas vistas coincidan cuando
     * el rack todavía no tiene posición guardada.
     */
    function _racksDeAlmacen(alm) {
        const out = [];
        (alm.pasillos || []).forEach(p =>
            (p.racks || []).forEach((r, ri) => out.push({ rack: r, idx: ri })));
        const base = (alm.pasillos || []).length;
        (alm.racks_sin_pasillo || []).forEach((r, ri) =>
            out.push({ rack: r, idx: base + ri }));
        return out;
    }

    function _recolectar(almacenes, posAlm, posRack) {
        const items = [];
        almacenes.forEach((alm, ai) => {
            const pA = posAlm[alm.id] || _posAlmacenDefault(ai);
            items.push(pA);
            _racksDeAlmacen(alm).forEach(({ rack, idx }) => {
                items.push(posRack[rack.id] || _posRackDefault(pA, idx));
            });
        });
        return items;
    }

    // Mismos defaults que el motor 2D, para que ambas vistas coincidan
    function _posAlmacenDefault(index) {
        const col = index % 2, row = Math.floor(index / 2);
        return {
            x: 60 + col * 480, y: 60 + row * 360,
            ancho: 420, alto: 300, rotacion: 0,
            color_fondo: '#071628', color_borde: '#1e6cb5',
        };
    }

    function _posRackDefault(pA, index) {
        const cols = 6;
        const col = index % cols, row = Math.floor(index / cols);
        const rackW = Math.floor((pA.ancho - 48 - 8 * (cols - 1)) / cols);
        return {
            x: pA.x + 24 + col * (rackW + 8),
            y: pA.y + 50 + row * 62,
            ancho: rackW, alto: 52, rotacion: 0,
            color_fondo: '#071e38', color_borde: '#0080cc',
        };
    }

    function _indexar(arr, key) {
        const o = {};
        (arr || []).forEach(x => { o[x[key]] = x; });
        return o;
    }

    // ── Piso ─────────────────────────────────────────────────────
    function _piso(anchoM, largoM) {
        const tex = _texturaConcreto().clone();
        tex.needsUpdate = true;
        tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
        tex.repeat.set(anchoM / 8, largoM / 8);   // una losa cada 8 m

        const suelo = new THREE.Mesh(
            new THREE.PlaneGeometry(anchoM, largoM),
            new THREE.MeshStandardMaterial({
                map: tex, roughness: 0.94, metalness: 0.02,
                color: 0xffffff,
            })
        );
        suelo.rotation.x = -Math.PI / 2;
        suelo.receiveShadow = CFG.verSombras;
        suelo.name = 'piso';
        _grpMundo.add(suelo);

        // Explanada exterior (evita el "vacío" al orbitar)
        const fuera = new THREE.Mesh(
            new THREE.PlaneGeometry(anchoM * 4, largoM * 4),
            new THREE.MeshBasicMaterial({
                color: TEMA.fondo.clone().multiplyScalar(TEMA.oscuro ? 1.15 : 0.92),
            })
        );
        fuera.rotation.x = -Math.PI / 2;
        fuera.position.y = -0.06;
        _grpMundo.add(fuera);

        // Retícula de metros, recortada exactamente a la superficie del piso
        // (un GridHelper es siempre cuadrado y se desbordaría del rectángulo).
        const paso = 5;
        const pts = [];
        const hx = anchoM / 2, hz = largoM / 2;
        for (let x = -hx; x <= hx + 0.001; x += paso) {
            pts.push(x, 0, -hz, x, 0, hz);
        }
        for (let z = -hz; z <= hz + 0.001; z += paso) {
            pts.push(-hx, 0, z, hx, 0, z);
        }
        const gGeo = new THREE.BufferGeometry();
        gGeo.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
        const grid = new THREE.LineSegments(gGeo, new THREE.LineBasicMaterial({
            color: TEMA.pisoLinea,
            transparent: true,
            opacity: TEMA.oscuro ? 0.18 : 0.3,
        }));
        grid.position.y = 0.015;
        _grpMundo.add(grid);
    }

    // ── Muros perimetrales ───────────────────────────────────────
    function _murosPerimetrales(anchoM, largoM) {
        const h = CFG.muroH;
        const t = 0.35;
        const mat = () => new THREE.MeshStandardMaterial({
            color: TEMA.muro, roughness: 0.9, metalness: 0.02,
            transparent: true, opacity: 0.95,
            side: THREE.DoubleSide,
        });

        const defs = [
            { w: anchoM + t, d: t, x: 0, z: -largoM / 2, n: new THREE.Vector3(0, 0, -1) },
            { w: anchoM + t, d: t, x: 0, z: largoM / 2, n: new THREE.Vector3(0, 0, 1) },
            { w: t, d: largoM + t, x: -anchoM / 2, z: 0, n: new THREE.Vector3(-1, 0, 0) },
            { w: t, d: largoM + t, x: anchoM / 2, z: 0, n: new THREE.Vector3(1, 0, 0) },
        ];

        defs.forEach(def => {
            const m = new THREE.Mesh(new THREE.BoxGeometry(def.w, h, def.d), mat());
            m.position.set(def.x, h / 2, def.z);
            m.receiveShadow = CFG.verSombras;
            m.name = 'muro';
            _grpMundo.add(m);
            _muros.push({ mesh: m, normal: def.n });

            // Remate superior
            const trim = new THREE.Mesh(
                new THREE.BoxGeometry(def.w + 0.12, 0.28, def.d + 0.12),
                new THREE.MeshStandardMaterial({
                    color: TEMA.muroTrim, roughness: 0.7, metalness: 0.1,
                    transparent: true, opacity: 0.95,
                })
            );
            trim.position.set(def.x, h + 0.14, def.z);
            _grpMundo.add(trim);
            _muros.push({ mesh: trim, normal: def.n });
        });
    }

    // ── Zona de almacén (losa + señalización + etiqueta) ─────────
    function _zona(alm, pos, wx, wz, wm) {
        if (!CFG.verZonas) return;

        const W = Math.max(1, wm(pos.ancho));
        const D = Math.max(1, wm(pos.alto));
        const g = new THREE.Group();
        g.position.set(wx(pos.x), 0, wz(pos.y));
        g.rotation.y = -THREE.MathUtils.degToRad(pos.rotacion || 0);

        const centro = new THREE.Group();
        centro.position.set(W / 2, 0, D / 2);
        g.add(centro);

        const colBorde = new THREE.Color(pos.color_borde || '#1e6cb5');

        // Losa tintada
        const losa = new THREE.Mesh(
            new THREE.PlaneGeometry(W, D),
            new THREE.MeshStandardMaterial({
                color: colBorde, roughness: 1, metalness: 0,
                transparent: true, opacity: TEMA.oscuro ? 0.13 : 0.10,
                depthWrite: false,
            })
        );
        losa.rotation.x = -Math.PI / 2;
        losa.position.y = 0.02;
        centro.add(losa);

        // Señalización pintada en el piso (línea amarilla perimetral)
        const pinturaMat = new THREE.MeshStandardMaterial({
            color: TEMA.pintura, roughness: 0.85, metalness: 0,
            transparent: true, opacity: 0.75, depthWrite: false,
        });
        const lw = 0.16;
        [[W, lw, 0, -D / 2], [W, lw, 0, D / 2],
        [lw, D, -W / 2, 0], [lw, D, W / 2, 0]].forEach(([w, d, x, z]) => {
            const l = new THREE.Mesh(new THREE.PlaneGeometry(w, d), pinturaMat);
            l.rotation.x = -Math.PI / 2;
            l.position.set(x, 0.03, z);
            centro.add(l);
        });

        // Etiqueta flotante
        if (CFG.verEtiquetas) {
            const nRacks = _racksDeAlmacen(alm).length;
            const sp = _sprite(
                (alm.cve_almacen || alm.cve || 'ALM').toUpperCase(),
                `${alm.descripcion || ''}${nRacks ? `  ·  ${nRacks} racks` : ''}`,
                '#' + colBorde.getHexString(), 20
            );
            sp.position.set(0, CFG.nivelH * 4.2, 0);
            centro.add(sp);
            _labels.push({ sprite: sp, min: 22, max: 420 });
        }

        _grpZonas.add(g);
        _zonasIdx.set(alm.id, { grupo: g, meta: alm });
    }

    // ═══════════════════════════════════════════════════════════════
    //  RACK 3D
    // ═══════════════════════════════════════════════════════════════
    function _rack(rack, alm, pos, wx, wz, wm) {
        const Wm = Math.max(0.5, wm(pos.ancho));
        const Dm = Math.max(0.5, wm(pos.alto));

        const cols = rack.columnas || [];
        const nCols = Math.max(cols.length, 1);
        const nNiv = Math.max(1, ...cols.map(c => (c.niveles || []).length), 1);
        const H = M.baseH + nNiv * CFG.nivelH;

        // Los módulos (bays) corren a lo largo del lado más largo
        const alongX = Wm >= Dm;
        const L = alongX ? Wm : Dm;     // longitud del rack
        const P = alongX ? Dm : Wm;     // profundidad del rack
        const bayL = L / nCols;

        const ocup = _ocupRack(rack);

        // Grupo raíz posicionado en la esquina (igual que Konva) …
        const raiz = new THREE.Group();
        raiz.position.set(wx(pos.x), 0, wz(pos.y));
        raiz.rotation.y = -THREE.MathUtils.degToRad(pos.rotacion || 0);

        // … y un grupo interno centrado en la huella
        const g = new THREE.Group();
        g.position.set(Wm / 2, 0, Dm / 2);
        if (!alongX) g.rotation.y = Math.PI / 2;
        raiz.add(g);

        // ── Geometrías acumuladas ────────────────────────────────
        const gAcero = [];   // postes, decks, diagonales
        const gViga = [];   // vigas horizontales
        const gTarima = [];   // tarimas + carga, con color por vértice

        // Índice de tarimas dentro de la malla fusionada: permite resolver
        // qué tarima se picó a partir del faceIndex del raycast.
        const slotsTarima = [];
        let triTarima = 0;

        // Posiciones de las etiquetas de ubicación (se materializan luego)
        const ulocs = [];

        const zFrente = P / 2 - M.posteW / 2;
        const zFondo = -zFrente;

        // Postes verticales
        for (let i = 0; i <= nCols; i++) {
            const x = -L / 2 + i * bayL;
            [zFrente, zFondo].forEach(z => {
                gAcero.push(_caja(M.posteW, H, M.posteW, x, H / 2, z));
            });
        }

        // Travesaños de los bastidores extremos (frente/fondo del marco)
        for (let i = 0; i <= nCols; i += nCols) {
            const x = -L / 2 + i * bayL;
            for (let k = 0; k <= nNiv; k++) {
                const y = M.baseH + k * CFG.nivelH;
                if (y > H) continue;
                gAcero.push(_caja(M.posteW * 0.7, M.posteW * 0.7, P - M.posteW, x, y, 0));
            }
            // Diagonales del bastidor (zigzag)
            const luz = Math.max(0.2, P - M.posteW);
            const dl = Math.hypot(luz, CFG.nivelH);
            const ang = Math.atan2(CFG.nivelH, luz);
            for (let k = 0; k < nNiv; k++) {
                const y = M.baseH + k * CFG.nivelH + CFG.nivelH / 2;
                // Se crea en el origen, se inclina y recién entonces se traslada.
                const dg = _caja(M.posteW * 0.5, M.posteW * 0.5, dl, 0, 0, 0);
                dg.rotateX(ang * (k % 2 ? 1 : -1));
                dg.translate(x, y, 0);
                gAcero.push(dg);
            }
        }

        // Largueros base
        [zFrente, zFondo].forEach(z => {
            gAcero.push(_caja(L, M.vigaH * 0.8, M.vigaD, 0, M.baseH / 2, z));
        });

        // Niveles: vigas + piso + tarimas
        cols.forEach((col, ci) => {
            const niveles = [...(col.niveles || [])]
                .sort((a, b) => (a.num_nivel || 0) - (b.num_nivel || 0));

            const xBay = -L / 2 + ci * bayL + bayL / 2;

            niveles.forEach((nivel, k) => {
                const y = M.baseH + k * CFG.nivelH;

                // Vigas frontal y trasera
                [zFrente, zFondo].forEach(z => {
                    gViga.push(_caja(bayL - M.posteW, M.vigaH, M.vigaD, xBay, y + M.vigaH / 2, z));
                });

                // Piso del nivel
                gAcero.push(_caja(bayL - M.posteW - 0.02, M.deckH, P - M.posteW - 0.04,
                    xBay, y + M.vigaH + M.deckH / 2, 0));

                // ── Ubicación (ulocation) ────────────────────────
                // Se anota la posición ahora; la malla se fabrica más tarde,
                // solo si la cámara llega a acercarse a este rack.
                // Proporcional al módulo: en un plano los racks pueden ser
                // enormes y una etiqueta de tamaño fijo sería ilegible.
                const wU = Math.min(Math.max(bayL * 0.32, 0.4), 2.6);
                ulocs.push({
                    texto: nivel.ulocation
                        || `C${col.num_col ?? ci + 1}-N${nivel.num_nivel ?? k + 1}`,
                    color: '#' + _colorOcup(_ocupNivel(nivel)).getHexString(),
                    // Extremo izquierdo de la viga: ahí no estorba ni al hueco
                    // del nivel ni a la tarima, igual que el etiquetado real.
                    x: xBay - bayL / 2 + wU / 2 + M.posteW,
                    xAtras: xBay + bayL / 2 - wU / 2 - M.posteW,
                    y: y + M.vigaH / 2,
                    zFrente: zFrente + M.vigaD / 2 + 0.012,
                    zAtras: zFondo - M.vigaD / 2 - 0.012,
                    w: wU,
                    h: wU * 0.3,
                });

                if (!CFG.verTarimas) return;

                // Tarimas
                const tarimas = nivel.tarimas || [];
                if (!tarimas.length) return;

                const pctNivel = _ocupNivel(nivel);
                const colCarga = _colorOcup(pctNivel || 0.2);

                const slotsCabe = Math.max(1, Math.floor((bayL - 0.12) / (M.tarimaW * 0.8)));
                const slots = Math.min(tarimas.length, slotsCabe, 4);
                const tarW = Math.min(M.tarimaW, (bayL - 0.14) / slots);
                const tarD = Math.min(M.tarimaD, P - M.posteW - 0.16);
                const yTar = y + M.vigaH + M.deckH;

                for (let s = 0; s < slots; s++) {
                    const xt = xBay - (slots - 1) * tarW / 2 + s * tarW;
                    const t = tarimas[s];

                    // Todas las piezas de esta tarima van al MISMO buffer, en
                    // orden, para poder mapear después faceIndex → tarima.
                    const triIni = triTarima;
                    const piezas = [];

                    // Tarima de madera: cubierta + 2 patines
                    piezas.push(_caja(tarW * 0.92, 0.045, tarD * 0.94,
                        xt, yTar + M.tarimaH - 0.022, 0));
                    [-tarD * 0.33, tarD * 0.33].forEach(dz => {
                        piezas.push(_caja(tarW * 0.9, M.tarimaH - 0.05, tarD * 0.16,
                            xt, yTar + (M.tarimaH - 0.05) / 2, dz));
                    });
                    piezas.forEach(p => _pintar(p, TEMA.madera));

                    // Carga
                    const nProd = (t?.productos || []).length;
                    const llenado = nProd ? Math.min(1, 0.45 + nProd * 0.18) : 0.4;
                    const cargaH = Math.min(
                        CFG.nivelH - M.vigaH - M.tarimaH - 0.18,
                        M.cargaMin + (M.cargaMax - M.cargaMin) * llenado
                    );
                    if (cargaH > 0.05) {
                        const cg = _caja(tarW * 0.84, cargaH, tarD * 0.86,
                            xt, yTar + M.tarimaH + cargaH / 2, 0);
                        _pintar(cg, colCarga);
                        piezas.push(cg);
                    }

                    const altoTotal = M.tarimaH + Math.max(0, cargaH);
                    piezas.forEach(p => { gTarima.push(p); triTarima += _tris(p); });

                    slotsTarima.push({
                        triIni, triFin: triTarima,
                        tarima: t, nivel, col,
                        pct: pctNivel,
                        centro: { x: xt, y: yTar + altoTotal / 2, z: 0 },
                        dims: { w: tarW * 0.95, h: altoTotal + 0.04, d: tarD },
                    });
                }
            });
        });

        // ── Materiales y meshes ──────────────────────────────────
        const matAcero = new THREE.MeshStandardMaterial({
            color: TEMA.acero, roughness: 0.55, metalness: 0.65,
        });
        const matViga = new THREE.MeshStandardMaterial({
            color: TEMA.viga, roughness: 0.5, metalness: 0.35,
        });
        const matTarima = new THREE.MeshStandardMaterial({
            vertexColors: true, roughness: 0.82, metalness: 0.04,
        });

        const materiales = [matAcero, matViga, matTarima];
        const meshes = [];
        let meshTarimas = null;

        [[gAcero, matAcero], [gViga, matViga], [gTarima, matTarima]].forEach(([geoms, mat]) => {
            if (!geoms.length) return;
            const merged = mergeGeoms(geoms, false);
            geoms.forEach(x => x.dispose());
            if (!merged) return;
            const mesh = new THREE.Mesh(merged, mat);
            mesh.castShadow = CFG.verSombras;
            mesh.receiveShadow = CFG.verSombras;
            g.add(mesh);
            meshes.push(mesh);
            if (mat === matTarima) meshTarimas = mesh;
        });

        // La malla de tarimas se raycastea aparte (antes que el hitbox del
        // rack) para poder distinguir "clic en tarima" de "clic en rack".
        if (meshTarimas && slotsTarima.length) {
            meshTarimas.userData = { rackId: rack.id, rack, alm, slots: slotsTarima };
            _tarimaMeshes.push(meshTarimas);
        }

        // ── Hitbox para raycast / selección ──────────────────────
        const hit = new THREE.Mesh(
            new THREE.BoxGeometry(L + 0.2, H + 0.3, P + 0.2),
            new THREE.MeshBasicMaterial({
                transparent: true, opacity: 0, depthWrite: false,
            })
        );
        hit.position.y = (H + 0.3) / 2 - 0.15;
        hit.userData = {
            rackId: rack.id,
            rack, alm,
            ocup,
            dims: { L, P, H },
        };
        g.add(hit);
        _hitboxes.push(hit);

        // ── Halo de ocupación en el piso ─────────────────────────
        if (ocup.pct > 0) {
            const halo = new THREE.Mesh(
                new THREE.PlaneGeometry(L + 0.5, P + 0.5),
                new THREE.MeshBasicMaterial({
                    color: _colorOcup(ocup.pct),
                    transparent: true, opacity: 0.16,
                    depthWrite: false,
                })
            );
            halo.rotation.x = -Math.PI / 2;
            halo.position.y = 0.045;
            g.add(halo);
        }

        // ── Etiqueta del rack ────────────────────────────────────
        if (CFG.verEtiquetas) {
            const txt = rack.num_rack ? `R${rack.num_rack}` : `#${rack.id}`;
            const sp = _sprite(txt, _etiquetaOcup(ocup.pct),
                '#' + _colorOcup(ocup.pct).getHexString(), 14);
            sp.position.set(0, H + 1.1, 0);
            g.add(sp);
            _labels.push({ sprite: sp, min: 7, max: 110 });
        }

        _grpRacks.add(raiz);
        _racksIdx.set(rack.id, {
            raiz, grupo: g, hitbox: hit, meta: rack, alm,
            materiales, meshes, ocup, dims: { L, P, H },
            ulocs, meshUloc: null,
            alturaUloc: ulocs.length ? ulocs[0].h : 0,
            slots: slotsTarima,
        });
    }

    // ── Helpers de geometría ─────────────────────────────────────
    function _caja(w, h, d, x, y, z) {
        const g = new THREE.BoxGeometry(
            Math.max(0.01, w), Math.max(0.01, h), Math.max(0.01, d));
        g.translate(x, y, z);
        return g;
    }

    /** Triángulos de una geometría (para indexar dentro de la malla fusionada). */
    function _tris(geo) {
        return geo.index ? geo.index.count / 3 : geo.attributes.position.count / 3;
    }

    function _pintar(geo, color) {
        const n = geo.attributes.position.count;
        const arr = new Float32Array(n * 3);
        for (let i = 0; i < n; i++) {
            arr[i * 3] = color.r;
            arr[i * 3 + 1] = color.g;
            arr[i * 3 + 2] = color.b;
        }
        geo.setAttribute('color', new THREE.BufferAttribute(arr, 3));
    }

    // ── Sprite de etiqueta ───────────────────────────────────────
    function _sprite(titulo, sub, color, alturaPx) {
        const pad = 14;
        const fT = 46, fS = 26;
        const cv = document.createElement('canvas');
        const ctx = cv.getContext('2d');

        ctx.font = `700 ${fT}px "Segoe UI", sans-serif`;
        const wT = ctx.measureText(titulo).width;
        ctx.font = `400 ${fS}px "Segoe UI", sans-serif`;
        const wS = sub ? ctx.measureText(sub).width : 0;

        const W = Math.ceil(Math.max(wT, wS) + pad * 2 + 8);
        const H = Math.ceil(fT + (sub ? fS + 8 : 0) + pad * 2);
        cv.width = W; cv.height = H;

        // Fondo tipo "pill"
        const r = 16;
        ctx.beginPath();
        ctx.moveTo(r, 0); ctx.lineTo(W - r, 0); ctx.quadraticCurveTo(W, 0, W, r);
        ctx.lineTo(W, H - r); ctx.quadraticCurveTo(W, H, W - r, H);
        ctx.lineTo(r, H); ctx.quadraticCurveTo(0, H, 0, H - r);
        ctx.lineTo(0, r); ctx.quadraticCurveTo(0, 0, r, 0);
        ctx.closePath();
        ctx.fillStyle = TEMA.oscuro ? 'rgba(9,17,30,0.86)' : 'rgba(255,255,255,0.92)';
        ctx.fill();
        ctx.strokeStyle = color;
        ctx.lineWidth = 3;
        ctx.stroke();

        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.fillStyle = color;
        ctx.font = `700 ${fT}px "Segoe UI", sans-serif`;
        ctx.fillText(titulo, W / 2, pad);

        if (sub) {
            ctx.fillStyle = TEMA.oscuro ? 'rgba(226,232,240,0.72)' : 'rgba(30,41,59,0.7)';
            ctx.font = `400 ${fS}px "Segoe UI", sans-serif`;
            ctx.fillText(sub, W / 2, pad + fT + 6);
        }

        const tex = new THREE.CanvasTexture(cv);
        tex.colorSpace = THREE.SRGBColorSpace;
        const sp = new THREE.Sprite(new THREE.SpriteMaterial({
            map: tex, transparent: true, depthTest: true, depthWrite: false,
        }));
        const escala = (alturaPx || 16) / 10;
        sp.scale.set(W / H * escala, escala, 1);
        sp.renderOrder = 10;
        return sp;
    }

    // ═══════════════════════════════════════════════════════════════
    //  INTERACCIÓN
    // ═══════════════════════════════════════════════════════════════
    function _bindEventos() {
        const dom = _renderer.domElement;

        let downPos = null;

        dom.addEventListener('pointerdown', e => { downPos = { x: e.clientX, y: e.clientY }; });

        dom.addEventListener('pointermove', e => {
            _actualizarNDC(e);
            const pick = _picar();
            const id = pick?.rackId ?? null;
            const tarimaId = pick?.tipo === 'tarima' ? pick.slot.tarima?.id : null;
            const tarimaPrev = _hoverTarima?.slot.tarima?.id ?? null;

            if (tarimaId !== tarimaPrev) {
                _hoverTarima = pick?.tipo === 'tarima' ? pick : null;
                _resaltarTarima(_hoverTarima);
                _dirty = true;
            }
            if (id !== _hoverId) {
                _hoverId = id;
                if (typeof _onRackHover === 'function') _onRackHover(id, pick);
                _dirty = true;
            }
            dom.style.cursor = pick ? 'pointer' : 'grab';
            _tooltip(pick, e);
        });

        dom.addEventListener('pointerleave', () => {
            _hoverId = null;
            _hoverTarima = null;
            _resaltarTarima(null);
            _tooltip(null);
            _dirty = true;
        });

        dom.addEventListener('pointerup', e => {
            if (!downPos) return;
            const movido = Math.hypot(e.clientX - downPos.x, e.clientY - downPos.y);
            downPos = null;
            if (movido > 5 || e.button !== 0) return;   // fue un orbit, no un click

            _actualizarNDC(e);
            const pick = _picar();

            if (!pick) { seleccionarRack(null); return; }

            if (pick.tipo === 'tarima') {
                // Clic en tarima → su detalle; clic en el rack → detalle del rack
                if (typeof _onTarimaClick === 'function') {
                    _onTarimaClick({
                        tarima: pick.slot.tarima,
                        nivel: pick.slot.nivel,
                        columna: pick.slot.col,
                        rack: pick.rack,
                        almacen: pick.alm,
                    });
                    return;
                }
            }

            seleccionarRack(pick.rackId);
            if (typeof _onRackClick === 'function') _onRackClick(pick.rackId, pick);
        });

        dom.addEventListener('dblclick', e => {
            _actualizarNDC(e);
            const pick = _picar();
            if (pick) focusRack(pick.rackId);
        });

        dom.addEventListener('contextmenu', e => e.preventDefault());

        _controls.addEventListener('change', () => { _dirty = true; });
    }

    function _actualizarNDC(e) {
        const r = _renderer.domElement.getBoundingClientRect();
        _mouseNDC.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        _mouseNDC.y = -((e.clientY - r.top) / r.height) * 2 + 1;
    }

    /**
     * Picking en dos pasadas: primero la geometría real de las tarimas y,
     * si no hay acierto, la caja invisible del rack. El orden importa —
     * el hitbox del rack envuelve a las tarimas y siempre ganaría.
     *
     * @returns {{tipo:'tarima'|'rack', ...}|null}
     */
    function _picar() {
        _raycaster.setFromCamera(_mouseNDC, _camera);

        if (_tarimaMeshes.length) {
            const hits = _raycaster.intersectObjects(_tarimaMeshes, false);
            if (hits.length) {
                const h = hits[0];
                const { slots, rack, alm, rackId } = h.object.userData;
                const slot = _slotPorFace(slots, h.faceIndex);
                if (slot) {
                    return { tipo: 'tarima', slot, rack, alm, rackId, grupo: h.object.parent };
                }
            }
        }

        if (_hitboxes.length) {
            const hits = _raycaster.intersectObjects(_hitboxes, false);
            if (hits.length) {
                const ud = hits[0].object.userData;
                return { tipo: 'rack', rack: ud.rack, alm: ud.alm, rackId: ud.rackId, ocup: ud.ocup };
            }
        }
        return null;
    }

    /** Busca (binaria) a qué tarima pertenece un triángulo de la malla fusionada. */
    function _slotPorFace(slots, faceIndex) {
        if (!slots || faceIndex == null) return null;
        let lo = 0, hi = slots.length - 1;
        while (lo <= hi) {
            const mid = (lo + hi) >> 1;
            const s = slots[mid];
            if (faceIndex < s.triIni) hi = mid - 1;
            else if (faceIndex >= s.triFin) lo = mid + 1;
            else return s;
        }
        return null;
    }

    /** Marco luminoso alrededor de la tarima apuntada. */
    function _resaltarTarima(pick) {
        if (_cajaHover) {
            _cajaHover.parent?.remove(_cajaHover);
            _cajaHover.geometry.dispose();
            _cajaHover.material.dispose();
            _cajaHover = null;
        }
        if (!pick) return;

        const { w, h, d } = pick.slot.dims;
        const geo = new THREE.BoxGeometry(w + 0.06, h + 0.06, d + 0.06);
        const marco = new THREE.LineSegments(
            new THREE.EdgesGeometry(geo),
            new THREE.LineBasicMaterial({ color: TEMA.accent, transparent: true, opacity: 0.95 })
        );
        geo.dispose();
        marco.position.set(pick.slot.centro.x, pick.slot.centro.y, pick.slot.centro.z);
        marco.renderOrder = 20;
        pick.grupo.add(marco);
        _cajaHover = marco;
    }

    // ── Tooltip HTML ─────────────────────────────────────────────
    function _tooltip(pick, e) {
        const el = document.getElementById('av-3d-tooltip');
        if (!el) return;

        if (!pick) { el.classList.remove('show'); return; }

        el.innerHTML = pick.tipo === 'tarima'
            ? _tooltipTarima(pick)
            : _tooltipRack(pick);

        el.classList.add('show');

        const r = _container.getBoundingClientRect();
        let x = e.clientX - r.left + 16;
        let y = e.clientY - r.top + 16;
        if (x + el.offsetWidth > r.width - 8) x = e.clientX - r.left - el.offsetWidth - 16;
        if (y + el.offsetHeight > r.height - 8) y = r.height - el.offsetHeight - 8;
        el.style.left = x + 'px';
        el.style.top = Math.max(8, y) + 'px';
    }

    function _tooltipRack({ rack, alm, ocup }) {
        const nTar = (rack.columnas || []).reduce((s, c) =>
            s + (c.niveles || []).reduce((s2, n) => s2 + (n.tarimas || []).length, 0), 0);
        const col = '#' + _colorOcup(ocup.pct).getHexString();

        return `
            <div class="av3d-tt-title">
                <i class="fas fa-pallet"></i>
                ${_esc(rack.num_rack ? 'Rack ' + rack.num_rack : 'Rack #' + rack.id)}
                ${rack.nombre ? `<span>${_esc(rack.nombre)}</span>` : ''}
            </div>
            <div class="av3d-tt-row"><span>Almacén</span><b>${_esc(alm.cve_almacen || alm.cve || alm.descripcion || '—')}</b></div>
            <div class="av3d-tt-row"><span>Columnas</span><b>${(rack.columnas || []).length}</b></div>
            <div class="av3d-tt-row"><span>Tarimas</span><b>${nTar}</b></div>
            <div class="av3d-tt-row"><span>Ocupación</span><b style="color:${col}">${_etiquetaOcup(ocup.pct)}</b></div>
            <div class="av3d-tt-bar"><i style="width:${Math.round(ocup.pct * 100)}%;background:${col}"></i></div>
            <div class="av3d-tt-hint">Clic para inspeccionar · doble clic para acercar</div>`;
    }

    function _tooltipTarima({ slot, rack }) {
        const { tarima, nivel, col } = slot;
        const prods = tarima?.productos || [];
        const total = prods.reduce((s, p) => s + (parseFloat(p.cantidad) || 0), 0);

        const lineas = prods.slice(0, 3).map(p => `
            <div class="av3d-tt-row">
                <span>${_esc(p.cve_prod || '—')}</span>
                <b>${_fmt(p.cantidad)} ${_esc(p.udm || '')}</b>
            </div>`).join('');

        return `
            <div class="av3d-tt-title av3d-tt-tarima">
                <i class="fas fa-pallet"></i>
                ${_esc(tarima?.codigo || 'Tarima ' + (tarima?.id ?? ''))}
                <span>${_esc(nivel?.ulocation || '')}</span>
            </div>
            <div class="av3d-tt-row"><span>Rack</span><b>${_esc(rack.num_rack ? 'R' + rack.num_rack : '#' + rack.id)}</b></div>
            <div class="av3d-tt-row"><span>Col · Nivel</span><b>${col?.num_col ?? '—'} · ${nivel?.num_nivel ?? '—'}</b></div>
            <div class="av3d-tt-row"><span>Productos</span><b>${prods.length}</b></div>
            <div class="av3d-tt-row"><span>Cantidad</span><b>${_fmt(total)}</b></div>
            ${lineas ? `<div class="av3d-tt-sep"></div>${lineas}` : ''}
            ${prods.length > 3 ? `<div class="av3d-tt-mas">+${prods.length - 3} más…</div>` : ''}
            <div class="av3d-tt-hint">Clic para ver el detalle de la tarima</div>`;
    }

    function _fmt(n) {
        if (n === null || n === undefined || n === '') return '—';
        const v = parseFloat(n);
        return isNaN(v) ? '—' : v.toLocaleString('es-MX', { maximumFractionDigits: 2 });
    }

    function _esc(s) {
        return String(s ?? '').replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    // ── Selección ────────────────────────────────────────────────
    function seleccionarRack(rackId) {
        if (_anilloSel) {
            _anilloSel.parent?.remove(_anilloSel);
            _anilloSel.geometry.dispose();
            _anilloSel.material.dispose();
            _anilloSel = null;
        }
        _selId = rackId;
        if (rackId == null) { _dirty = true; return; }

        const r = _racksIdx.get(rackId);
        if (!r) { _dirty = true; return; }

        const { L, P } = r.dims;
        // Circunscribe la huella del rack, sin desbordarla
        const radio = Math.hypot(L, P) / 2 + 0.35;
        const anillo = new THREE.Mesh(
            new THREE.RingGeometry(radio, radio + 0.28, 64),
            new THREE.MeshBasicMaterial({
                color: TEMA.accent, transparent: true, opacity: 0.9,
                side: THREE.DoubleSide, depthWrite: false,
            })
        );
        anillo.rotation.x = -Math.PI / 2;
        anillo.position.y = 0.07;
        anillo.userData.pulso = 0;
        r.grupo.add(anillo);
        _anilloSel = anillo;
        _dirty = true;
    }

    // ── Cámara ───────────────────────────────────────────────────
    function _esfericaAPos(target, distancia, elevDeg, azimDeg) {
        const phi = THREE.MathUtils.degToRad(90 - elevDeg);
        const theta = THREE.MathUtils.degToRad(azimDeg);
        const sp = new THREE.Spherical(distancia, phi, theta);
        return new THREE.Vector3().setFromSpherical(sp).add(target);
    }

    function _volarA(pos, target, dur = 700) {
        // Si había un vuelo en curso, se cancela resolviendo su promesa
        _flying?.resolve?.();

        // Sin bucle de render (vista oculta) el salto es inmediato,
        // así ningún `await focusRack(...)` se queda colgado.
        if (!_mounted) {
            _camera.position.copy(pos);
            _controls.target.copy(target);
            _controls.update();
            _flying = null;
            _dirty = true;
            return Promise.resolve();
        }

        const p0 = _camera.position.clone();
        const t0 = _controls.target.clone();
        const inicio = performance.now();
        _flying = { p0, t0, p1: pos.clone(), t1: target.clone(), inicio, dur };
        _dirty = true;
        return new Promise(res => { _flying.resolve = res; });
    }

    function _tickVuelo() {
        if (!_flying) return;
        const t = Math.min(1, (performance.now() - _flying.inicio) / _flying.dur);

        if (_flying.curva) {
            // Recorrido: acelera al salir y frena al llegar
            const e = _suavizar(t);
            _flying.curva.getPointAt(e, _camera.position);
            _flying.curvaMira.getPointAt(e, _controls.target);
            // Una Catmull-Rom puede sobrepasar sus puntos de control; esto
            // evita que el tramo final se hunda por debajo del piso.
            if (_camera.position.y < 1.4) _camera.position.y = 1.4;
        } else {
            const e = 1 - Math.pow(1 - t, 3);
            _camera.position.lerpVectors(_flying.p0, _flying.p1, e);
            _controls.target.lerpVectors(_flying.t0, _flying.t1, e);
        }

        _controls.update();
        _dirty = true;

        if (t >= 1) {
            const done = _flying.resolve;
            _flying = null;
            done?.();
        }
    }

    /** easeInOutCubic */
    function _suavizar(t) {
        return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
    }

    /**
     * Vuelo "por dentro de la nave": en lugar de interpolar en línea recta
     * (que atraviesa racks y muros), se traza una curva que se eleva por
     * encima de la estantería, avanza sobre el almacén y desciende al pasillo
     * frente al destino. La mirada gira por su propia curva, así se ve hacia
     * dónde se va durante el trayecto.
     */
    function _volarPorAlmacen(pos, mira, opts = {}) {
        _flying?.resolve?.();

        if (!_mounted) {
            _camera.position.copy(pos);
            _controls.target.copy(mira);
            _controls.update();
            _flying = null;
            _dirty = true;
            return Promise.resolve();
        }

        const p0 = _camera.position.clone();
        const t0 = _controls.target.clone();
        const dist = p0.distanceTo(pos);

        // Trayecto corto: no merece la pena la curva, se nota artificial
        if (dist < 12) return _volarA(pos, mira, 700);

        // Cima del recorrido: por encima de los racks, algo antes del destino
        const cima = p0.clone().lerp(pos, 0.5);
        cima.y = Math.max(p0.y, pos.y) + THREE.MathUtils.clamp(dist * 0.2, 4, 16);

        // Punto de aproximación: ya bajando, alineado con el pasillo final
        const aprox = pos.clone().lerp(cima, 0.28);

        const curva = new THREE.CatmullRomCurve3(
            [p0, cima, aprox, pos], false, 'catmullrom', 0.4);
        const curvaMira = new THREE.CatmullRomCurve3(
            [t0, t0.clone().lerp(mira, 0.55), mira.clone(), mira.clone()],
            false, 'catmullrom', 0.4);

        const dur = opts.dur ?? THREE.MathUtils.clamp(900 + dist * 20, 1100, 2800);

        _flying = { curva, curvaMira, inicio: performance.now(), dur };
        _dirty = true;
        return new Promise(res => { _flying.resolve = res; });
    }

    /**
     * Punto de vista "de operario en el pasillo": frente a la cara del rack
     * que ya mira hacia la cámara (para no atravesarlo) y a altura de persona.
     */
    function _vistaDePasillo(entry, objetivo) {
        entry.grupo.updateWorldMatrix(true, false);
        const q = entry.grupo.getWorldQuaternion(new THREE.Quaternion());

        const frente = new THREE.Vector3(0, 0, 1)
            .applyQuaternion(q).setY(0).normalize();

        // Se elige el pasillo por el que ya está la cámara
        const haciaCam = _camera.position.clone().sub(objetivo).setY(0);
        if (haciaCam.lengthSq() > 0.001 && frente.dot(haciaCam) < 0) frente.negate();

        const { L, P, H } = entry.dims;
        const fov = THREE.MathUtils.degToRad(_camera.fov);

        // Lo que se quiere ver: la altura del rack y unos metros a lo ancho
        const encuadre = Math.max(H, Math.min(L, 9));
        const separacion = THREE.MathUtils.clamp(
            (encuadre / 2) / Math.tan(fov / 2) * 0.85, 5, 22);

        // El objetivo está DENTRO del rack: si no se suma su fondo, la cámara
        // acaba pegada a la estantería y no se ve nada de contexto.
        const dist = separacion + P / 2;

        const pos = objetivo.clone().addScaledVector(frente, dist);
        pos.y = Math.max(2, objetivo.y + Math.max(1.2, H * 0.18));
        return pos;
    }

    function zoomFit(animar = true) {
        if (!_camera) return;
        const box = _bbox.isEmpty()
            ? new THREE.Box3(new THREE.Vector3(-30, 0, -20), new THREE.Vector3(30, 8, 20))
            : _bbox.clone();

        const centro = box.getCenter(new THREE.Vector3());
        const tam = box.getSize(new THREE.Vector3());
        const radio = Math.max(tam.x, tam.z) / 2 || 20;

        const fov = THREE.MathUtils.degToRad(_camera.fov);
        const dist = (radio / Math.tan(fov / 2)) * 1.45 + tam.y;

        const target = new THREE.Vector3(centro.x, tam.y * 0.28, centro.z);
        const pos = _esfericaAPos(target, dist, 34, -50);

        if (animar) return _volarA(pos, target, 750);

        _camera.position.copy(pos);
        _controls.target.copy(target);
        _controls.update();
        _dirty = true;
        return Promise.resolve();
    }

    const PRESETS = {
        perspectiva: { elev: 34, azim: -50, f: 1.45 },
        isometrica: { elev: 35.264, azim: 45, f: 1.55 },
        cenital: { elev: 89, azim: -90, f: 1.30 },
        // Vista "de operario": la cámara debe quedar DENTRO de la nave,
        // por eso el factor de distancia es muy inferior a 1.
        piso: { elev: 5, azim: -60, f: 0.32 },
    };

    function setPreset(nombre) {
        const p = PRESETS[nombre] || PRESETS.perspectiva;
        const box = _bbox.isEmpty()
            ? new THREE.Box3(new THREE.Vector3(-30, 0, -20), new THREE.Vector3(30, 8, 20))
            : _bbox.clone();
        const centro = box.getCenter(new THREE.Vector3());
        const tam = box.getSize(new THREE.Vector3());
        const radio = Math.max(tam.x, tam.z) / 2 || 20;
        const fov = THREE.MathUtils.degToRad(_camera.fov);
        const dist = (radio / Math.tan(fov / 2)) * p.f + tam.y;

        // A nivel de piso se mira a la altura de un operario, no al suelo
        const target = new THREE.Vector3(centro.x,
            nombre === 'piso' ? 3 : tam.y * 0.28, centro.z);
        return _volarA(_esfericaAPos(target, dist, p.elev, p.azim), target, 700);
    }

    /** Vuela la cámara hasta un rack y lo resalta. */
    function focusRack(rackId, opts = {}) {
        return focusUbicacion({ rackId }, opts);
    }

    /**
     * Vuela hasta una ubicación concreta del almacén y la deja marcada.
     * Si se indica tarima o nivel, aterriza apuntando a ese hueco exacto;
     * si no, encuadra el rack completo.
     *
     * @param {{rackId:number, nivelId?:number, tarimaId?:number}} destino
     */
    function focusUbicacion(destino, opts = {}) {
        const r = _racksIdx.get(destino?.rackId);
        if (!r) return Promise.resolve(false);

        const slot = _buscarSlot(r, destino);

        // Punto exacto al que se mira
        const mira = new THREE.Vector3();
        if (slot) {
            r.grupo.updateWorldMatrix(true, false);
            mira.set(slot.centro.x, slot.centro.y, slot.centro.z)
                .applyMatrix4(r.grupo.matrixWorld);
        } else {
            r.hitbox.updateWorldMatrix(true, false);
            r.hitbox.getWorldPosition(mira);
            mira.y = r.dims.H * 0.5;
        }

        const pos = _vistaDePasillo(r, mira);

        seleccionarRack(destino.rackId);
        marcarUbicacion(destino.rackId, slot);
        _baliza(mira, r.dims.H);

        return _volarPorAlmacen(pos, mira, opts).then(() => {
            _desvanecerBaliza();
            return true;
        });
    }

    function _buscarSlot(entry, destino) {
        const slots = entry.slots || [];
        if (!slots.length) return null;
        if (destino.tarimaId != null) {
            const s = slots.find(x => x.tarima?.id === destino.tarimaId);
            if (s) return s;
        }
        if (destino.nivelId != null) {
            const s = slots.find(x => x.nivel?.id === destino.nivelId);
            if (s) return s;
        }
        return null;
    }

    /**
     * Escuadras en las 8 esquinas de una caja, tipo mira de cámara.
     *
     * @param {boolean} verATraves  en el plano conviene dibujarlas sin test de
     *        profundidad (la ubicación puede quedar detrás de otro rack); en
     *        los visores del modal, no: nada la tapa y las líneas traseras
     *        cruzando por delante de las cajas solo ensucian.
     */
    function _escuadras(w, h, d, color, prop = 0.28, verATraves = true) {
        const hx = w / 2, hy = h / 2, hz = d / 2;
        const L = Math.max(0.05, Math.min(w, h, d) * prop);
        const pts = [];

        for (const sx of [-1, 1]) {
            for (const sy of [-1, 1]) {
                for (const sz of [-1, 1]) {
                    const x = sx * hx, y = sy * hy, z = sz * hz;
                    pts.push(x, y, z, x - sx * L, y, z);
                    pts.push(x, y, z, x, y - sy * L, z);
                    pts.push(x, y, z, x, y, z - sz * L);
                }
            }
        }

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
        const linea = new THREE.LineSegments(geo, new THREE.LineBasicMaterial({
            color, transparent: true, opacity: 1, depthTest: !verATraves,
        }));
        linea.renderOrder = 999;
        return linea;
    }

    // ── Marca persistente sobre la ubicación encontrada ──────────
    let _marca = null;

    function marcarUbicacion(rackId, slot) {
        _limpiarMarca();
        if (!slot) { _dirty = true; return; }

        const r = _racksIdx.get(rackId);
        if (!r) return;

        const { w, h, d } = slot.dims;
        const g = new THREE.Group();
        g.position.set(slot.centro.x, slot.centro.y, slot.centro.z);

        const escuadras = _escuadras(w + 0.14, h + 0.14, d + 0.14, TEMA.accent);
        g.add(escuadras);

        // Tinte translúcido para que la tarima destaque entre sus vecinas
        const tinte = new THREE.Mesh(
            new THREE.BoxGeometry(w + 0.05, h + 0.05, d + 0.05),
            new THREE.MeshBasicMaterial({
                color: TEMA.accent, transparent: true, opacity: 0.12,
                depthWrite: false,
            })
        );
        tinte.renderOrder = 26;
        g.add(tinte);

        // Chincheta flotante con el código de la tarima y su ubicación
        const codigo = slot.tarima?.codigo || (slot.tarima ? 'Tarima ' + slot.tarima.id : '');
        if (codigo) {
            const sp = _sprite(codigo, slot.nivel?.ulocation || '',
                '#' + TEMA.accent.getHexString(), 11);
            sp.position.y = h / 2 + 0.85;
            g.add(sp);
        }

        r.grupo.add(g);
        _marca = { grupo: g, escuadras, tinte };
        _dirty = true;
    }

    function _limpiarMarca() {
        if (!_marca) return;
        _marca.grupo.parent?.remove(_marca.grupo);
        _marca.grupo.traverse(n => {
            n.geometry?.dispose();
            if (n.material) {
                const ms = Array.isArray(n.material) ? n.material : [n.material];
                ms.forEach(m => { m.map?.dispose?.(); m.dispose(); });
            }
        });
        _marca = null;
    }

    /** Latido del marcador de ubicación. */
    function _tickMarca() {
        if (!_marca) return;
        const p = 0.5 + Math.sin(_clock.getElapsedTime() * 3.4) * 0.5;
        _marca.escuadras.material.opacity = 0.5 + p * 0.5;
        _marca.tinte.material.opacity = 0.06 + p * 0.13;
        const s = 1 + p * 0.035;
        _marca.escuadras.scale.set(s, s, s);
        _dirty = true;
    }

    // ── Baliza vertical en el destino durante el vuelo ───────────
    let _balizaMesh = null;
    let _balizaFin = 0;

    function _baliza(punto, alturaRack) {
        _quitarBaliza();

        const alto = Math.max(6, (alturaRack || 4) * 1.6);
        const geo = new THREE.CylinderGeometry(0.22, 0.42, alto, 16, 1, true);
        const mat = new THREE.MeshBasicMaterial({
            color: TEMA.accent,
            transparent: true, opacity: 0.3,
            side: THREE.DoubleSide, depthWrite: false,
            blending: THREE.AdditiveBlending,
        });
        _balizaMesh = new THREE.Mesh(geo, mat);
        _balizaMesh.position.set(punto.x, alto / 2, punto.z);
        _balizaMesh.renderOrder = 24;
        _scene.add(_balizaMesh);
        _balizaFin = 0;
        _dirty = true;
    }

    function _desvanecerBaliza() {
        if (_balizaMesh) _balizaFin = performance.now() + 900;
    }

    function _quitarBaliza() {
        if (!_balizaMesh) return;
        _scene.remove(_balizaMesh);
        _balizaMesh.geometry.dispose();
        _balizaMesh.material.dispose();
        _balizaMesh = null;
        _balizaFin = 0;
    }

    function _tickBaliza() {
        if (!_balizaMesh) return;
        const t = performance.now();

        if (!_balizaFin) {
            // Pulso suave mientras dura el vuelo
            _balizaMesh.material.opacity = 0.22 + Math.sin(t * 0.006) * 0.12;
        } else {
            const resta = _balizaFin - t;
            if (resta <= 0) { _quitarBaliza(); _dirty = true; return; }
            _balizaMesh.material.opacity = 0.32 * (resta / 900);
        }
        _dirty = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  LOOP DE RENDER
    // ═══════════════════════════════════════════════════════════════
    function _loop() {
        if (!_mounted) return;

        _tickVuelo();
        _tickBaliza();
        _tickMarca();
        const movio = _controls.update();

        // Pulso del anillo de selección
        if (_anilloSel) {
            const t = _clock.getElapsedTime();
            const s = 1 + Math.sin(t * 3.2) * 0.06;
            _anilloSel.scale.set(s, s, 1);
            _anilloSel.material.opacity = 0.55 + Math.sin(t * 3.2) * 0.32;
            _dirty = true;
        }

        if (movio || _dirty) {
            _actualizarMuros();
            _actualizarLabels();
            _renderer.render(_scene, _camera);
            _dirty = false;
        }
    }

    /** Desvanece los muros que quedan entre la cámara y el centro. */
    function _actualizarMuros() {
        if (!_muros.length) return;
        _camera.getWorldDirection(_tmpV);
        _muros.forEach(({ mesh, normal }) => {
            // La normal exterior del muro cercano apunta hacia la cámara,
            // es decir en sentido opuesto a la dirección de vista.
            const tapaLaVista = normal.dot(_tmpV) < -0.12;
            const objetivo = tapaLaVista ? 0.05 : 0.95;
            if (Math.abs(mesh.material.opacity - objetivo) > 0.01) {
                mesh.material.opacity += (objetivo - mesh.material.opacity) * 0.18;
                _dirty = true;
            }
            mesh.visible = mesh.material.opacity > 0.02;
        });
    }

    /**
     * Muestra cada etiqueta solo dentro de su franja útil de distancia:
     * de lejos satura la vista y de muy cerca el sprite (que está en
     * unidades de mundo) tapa media pantalla.
     */
    function _actualizarLabels() {
        const cam = _camera.position;

        _labels.forEach(({ sprite, min, max }) => {
            sprite.getWorldPosition(_tmpV);
            const d = cam.distanceTo(_tmpV);
            sprite.visible = CFG.verEtiquetas && d > min && d < max;
        });

        _actualizarUbicaciones(cam);
    }

    // Altura aparente mínima, en píxeles, para que una ubicación se muestre.
    const PX_ULOC_MIN = 9;

    /**
     * Las etiquetas de ubicación solo existen cerca: se fabrican la primera
     * vez que se vuelven legibles y luego solo se muestran/ocultan.
     * Construirlas todas de golpe sería tirar memoria de vídeo en carteles
     * que a distancia de plano no se leen.
     *
     * El criterio no es una distancia fija sino el tamaño en pantalla: los
     * racks de un plano pueden medir 3 m o 40 m, y a la misma distancia una
     * etiqueta se lee o no según lo grande que sea en metros.
     */
    function _actualizarUbicaciones(cam) {
        if (!_racksIdx.size) return;
        const activo = CFG.verEtiquetas && CFG.verUbicaciones !== false;
        const tanFov = Math.tan(THREE.MathUtils.degToRad(_camera.fov) / 2);
        const altoPx = _alto();

        _racksIdx.forEach(entry => {
            if (!entry.ulocs?.length) return;

            if (!activo) {
                if (entry.meshUloc) entry.meshUloc.visible = false;
                return;
            }

            entry.hitbox.getWorldPosition(_tmpV);
            const d = Math.max(0.001, cam.distanceTo(_tmpV));
            const px = entry.alturaUloc * altoPx / (2 * d * tanFov);
            const legible = px >= PX_ULOC_MIN;

            if (legible && !entry.meshUloc) entry.meshUloc = _construirUloc(entry);
            if (entry.meshUloc) entry.meshUloc.visible = legible;
        });
    }

    /**
     * Todas las ubicaciones de un rack en UNA sola malla: un atlas de canvas
     * con una celda por etiqueta y un par de quads (frente y fondo) que apuntan
     * a su celda vía UV. Así son 1 draw call y 1 textura por rack, en vez de
     * un plano suelto por posición.
     */
    function _construirUloc(entry) {
        const items = entry.ulocs;
        if (!items?.length) return null;

        const CW = 128, CH = 32, COLS = 4;
        const filas = Math.ceil(items.length / COLS);
        const W = CW * COLS;
        const H = CH * filas;

        const cv = document.createElement('canvas');
        cv.width = W;
        cv.height = H;
        const ctx = cv.getContext('2d');
        ctx.clearRect(0, 0, W, H);

        items.forEach((it, i) => {
            const c = i % COLS, r = Math.floor(i / COLS);
            const x0 = c * CW, y0 = r * CH;

            _rectRedondo(ctx, x0 + 3, y0 + 3, CW - 6, CH - 6, 6);
            ctx.fillStyle = TEMA.oscuro ? 'rgba(8,15,26,0.94)' : 'rgba(255,255,255,0.96)';
            ctx.fill();
            ctx.strokeStyle = it.color;
            ctx.lineWidth = 3;
            ctx.stroke();

            let fs = 19;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            do {
                ctx.font = `700 ${fs}px "Segoe UI", sans-serif`;
                if (ctx.measureText(it.texto).width <= CW - 20) break;
                fs -= 1;
            } while (fs > 7);

            ctx.fillStyle = it.color;
            ctx.fillText(it.texto, x0 + CW / 2, y0 + CH / 2 + 1);
        });

        const tex = new THREE.CanvasTexture(cv);
        tex.colorSpace = THREE.SRGBColorSpace;
        tex.generateMipmaps = false;
        tex.minFilter = THREE.LinearFilter;
        tex.magFilter = THREE.LinearFilter;

        const pos = [], uvs = [], idx = [];
        let v = 0;

        items.forEach((it, i) => {
            const c = i % COLS, r = Math.floor(i / COLS);
            const u0 = (c * CW + 2) / W;
            const u1 = ((c + 1) * CW - 2) / W;
            const v0 = 1 - ((r + 1) * CH - 2) / H;
            const v1 = 1 - (r * CH + 2) / H;

            const hw = it.w / 2, hh = it.h / 2;

            // Cara frontal (mira a +Z)
            pos.push(
                it.x - hw, it.y - hh, it.zFrente,
                it.x + hw, it.y - hh, it.zFrente,
                it.x + hw, it.y + hh, it.zFrente,
                it.x - hw, it.y + hh, it.zFrente);
            uvs.push(u0, v0, u1, v0, u1, v1, u0, v1);
            idx.push(v, v + 1, v + 2, v, v + 2, v + 3);
            v += 4;

            // Cara trasera (mira a -Z). La X va invertida para que el texto
            // se lea bien desde el pasillo de atrás y no en espejo.
            pos.push(
                it.xAtras + hw, it.y - hh, it.zAtras,
                it.xAtras - hw, it.y - hh, it.zAtras,
                it.xAtras - hw, it.y + hh, it.zAtras,
                it.xAtras + hw, it.y + hh, it.zAtras);
            uvs.push(u0, v0, u1, v0, u1, v1, u0, v1);
            idx.push(v, v + 1, v + 2, v, v + 2, v + 3);
            v += 4;
        });

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        geo.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
        geo.setIndex(idx);
        geo.computeVertexNormals();

        const mesh = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({
            map: tex, transparent: true, toneMapped: false,
            depthWrite: false, side: THREE.FrontSide,
        }));
        mesh.renderOrder = 6;
        entry.grupo.add(mesh);
        return mesh;
    }

    function _rectRedondo(ctx, x, y, w, h, r) {
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + w - r, y);
        ctx.quadraticCurveTo(x + w, y, x + w, y + r);
        ctx.lineTo(x + w, y + h - r);
        ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
        ctx.lineTo(x + r, y + h);
        ctx.quadraticCurveTo(x, y + h, x, y + h - r);
        ctx.lineTo(x, y + r);
        ctx.quadraticCurveTo(x, y, x + r, y);
        ctx.closePath();
    }

    function _onResize() {
        if (!_renderer || !_container) return;
        const w = _ancho(), h = _alto();
        _renderer.setSize(w, h, false);
        _camera.aspect = w / h;
        _camera.updateProjectionMatrix();
        _dirty = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CICLO DE VIDA
    // ═══════════════════════════════════════════════════════════════
    function mount() {
        if (!init()) return false;
        _mounted = true;
        _onResize();
        _dirty = true;
        return true;
    }

    function unmount() {
        _mounted = false;
        _quitarBaliza();
        // Sin bucle no hay quien termine el vuelo: se salta al destino y se
        // resuelve, o cualquier `await focusRack(...)` se quedaría colgado.
        if (_flying) {
            _camera.position.copy(_flying.p1);
            _controls.target.copy(_flying.t1);
            _controls.update();
            const done = _flying.resolve;
            _flying = null;
            done?.();
        }
        _tooltip(null);
    }

    function _limpiarGrupo(g) {
        if (!g) return;
        for (let i = g.children.length - 1; i >= 0; i--) {
            const o = g.children[i];
            o.traverse(n => {
                if (n.geometry) n.geometry.dispose();
                if (n.material) {
                    const mats = Array.isArray(n.material) ? n.material : [n.material];
                    mats.forEach(m => { m.map?.dispose?.(); m.dispose(); });
                }
            });
            g.remove(o);
        }
    }

    function dispose() {
        unmount();
        _quitarFantasma();   // cuelga de la escena, no de los grupos que se limpian abajo
        _resizeObs?.disconnect();
        _renderer?.setAnimationLoop(null);
        [_grpMundo, _grpZonas, _grpRacks].forEach(_limpiarGrupo);
        _renderer?.dispose();
        _renderer = null;
    }

    // ── Configuración en caliente ────────────────────────────────
    function setConfig(parcial) {
        Object.assign(CFG, parcial || {});
        _guardarCfg();
        if (_renderer) {
            _renderer.shadowMap.enabled = CFG.verSombras;
            if (_sol) _sol.castShadow = CFG.verSombras;
        }
        if (_datos) build(_datos, { mantenerCamara: true });
    }

    function getConfig() { return Object.assign({}, CFG); }

    function refrescarTema() {
        if (!_renderer) return;
        _leerTema();
        _scene.background?.dispose?.();
        _scene.background = _texturaCielo();
        _scene.fog.color.copy(TEMA.fondo);
        _texPiso?.dispose(); _texPiso = null;
        _scene.remove(_hemi, _amb, _sol, _sol.target);
        _luces();
        if (_datos) build(_datos, { mantenerCamara: true });
    }

    // ═══════════════════════════════════════════════════════════════
    //  EL MONITO — bajar al piso soltándolo en un punto
    // ═══════════════════════════════════════════════════════════════
    //  Se arrastra la figura desde su base y al soltarla sobre el plano la
    //  cámara viaja hasta ahí y se queda a la altura de los ojos, como si te
    //  hubieras plantado en ese pasillo.
    //
    //  Dos reglas que hacen que se sienta bien:
    //   · No se aterriza DENTRO de un rack. El punto se empuja fuera de la
    //     huella más cercana, igual que el monito de los mapas se pega a la
    //     calle en vez de meterse en un edificio.
    //   · Se conserva el rumbo que ya llevaba la cámara. Girar al usuario
    //     hacia otro lado al aterrizar desorienta: aterrizas mirando hacia
    //     donde ya mirabas.
    // ───────────────────────────────────────────────────────────────
    const OPERARIO = {
        ojos: 1.7,     // altura de los ojos (m)
        // Holgura al expulsar el punto de un rack. A 0,7 m acabas con la cara
        // pegada al larguero y no se lee nada; 1,2 m es aproximadamente medio
        // pasillo, que es donde uno se pondría de verdad para mirar el rack.
        margen: 1.2,
        mira: 7,       // a qué distancia por delante se pone el punto de mira
    };

    const ALTO_MONITO = 1.85;   // alto real de la figura (m)
    const PX_MONITO_MIN = 58;   // altura aparente mínima en pantalla (px)

    const _planoPiso = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    let _fantasma = null;    // marcador del punto de aterrizaje
    let _texMonito = null;

    /** Punto del suelo (y=0) bajo unas coordenadas de pantalla. */
    function _puntoPiso(clientX, clientY) {
        if (!_camera || !_container) return null;

        const r = _container.getBoundingClientRect();
        if (clientX < r.left || clientX > r.right ||
            clientY < r.top || clientY > r.bottom) return null;

        _mouseNDC.x = ((clientX - r.left) / r.width) * 2 - 1;
        _mouseNDC.y = -((clientY - r.top) / r.height) * 2 + 1;
        _raycaster.setFromCamera(_mouseNDC, _camera);

        const p = new THREE.Vector3();
        // Apuntando al cielo el rayo nunca corta el piso.
        return _raycaster.ray.intersectPlane(_planoPiso, p) ? p : null;
    }

    function _dentroDeNave(p) {
        const hx = _nave.anchoM / 2, hz = _nave.largoM / 2;
        if (!hx || !hz) return true;
        return Math.abs(p.x) <= hx && Math.abs(p.z) <= hz;
    }

    /**
     * Devuelve el punto transitable más cercano: si cae dentro de un rack lo
     * saca por la cara más próxima. Se repite porque salir de una estantería
     * puede dejarte dentro de la de al lado.
     */
    function _puntoCaminable(p) {
        const q = new THREE.Vector3(p.x, 0, p.z);

        for (let vuelta = 0; vuelta < 4; vuelta++) {
            let movido = false;

            for (const h of _huellas) {
                const minX = h.caja.min.x - OPERARIO.margen;
                const maxX = h.caja.max.x + OPERARIO.margen;
                const minZ = h.caja.min.z - OPERARIO.margen;
                const maxZ = h.caja.max.z + OPERARIO.margen;

                if (q.x < minX || q.x > maxX || q.z < minZ || q.z > maxZ) continue;

                const salidas = [
                    { eje: 'x', v: minX, d: q.x - minX },
                    { eje: 'x', v: maxX, d: maxX - q.x },
                    { eje: 'z', v: minZ, d: q.z - minZ },
                    { eje: 'z', v: maxZ, d: maxZ - q.z },
                ].sort((a, b) => a.d - b.d)[0];

                q[salidas.eje] = salidas.v;
                movido = true;
                break;
            }

            if (!movido) break;
        }

        return q;
    }

    /** Nombre del almacén sobre el que cae el punto, si cae en alguno. */
    function _zonaEn(p) {
        for (const z of _huellasZona) {
            if (p.x >= z.caja.min.x && p.x <= z.caja.max.x &&
                p.z >= z.caja.min.z && p.z <= z.caja.max.z) {
                return z.meta?.cve_almacen || z.meta?.cve || z.meta?.descripcion || null;
            }
        }
        return null;
    }

    /** Rack más próximo al punto, para poder decir «junto a R3». */
    function _rackCerca(p) {
        let mejor = null, mejorD = Infinity;
        for (const h of _huellas) {
            const dx = Math.max(h.caja.min.x - p.x, 0, p.x - h.caja.max.x);
            const dz = Math.max(h.caja.min.z - p.z, 0, p.z - h.caja.max.z);
            const d = Math.hypot(dx, dz);
            if (d < mejorD) { mejorD = d; mejor = h; }
        }
        if (!mejor || mejorD > 8) return null;
        const m = mejor.meta;
        return m?.num_rack ? 'R' + m.num_rack : (m?.nombre || null);
    }

    function _texturaMonito() {
        if (_texMonito) return _texMonito;

        const c = document.createElement('canvas');
        c.width = 128; c.height = 208;
        const x = c.getContext('2d');
        x.lineJoin = x.lineCap = 'round';

        // Se dibuja dos veces: primero un contorno claro y ancho, encima la
        // figura. Así el monito se lee igual sobre piso claro que sobre uno
        // oscuro, sin depender del tema.
        const figura = (color, grosor, radio) => {
            x.strokeStyle = color; x.fillStyle = color; x.lineWidth = grosor;
            x.beginPath(); x.arc(64, 42, radio, 0, Math.PI * 2); x.fill();
            x.beginPath(); x.moveTo(64, 66); x.lineTo(64, 128); x.stroke();
            x.beginPath(); x.moveTo(28, 104); x.lineTo(64, 80); x.lineTo(100, 104); x.stroke();
            x.beginPath(); x.moveTo(34, 182); x.lineTo(64, 128); x.lineTo(94, 182); x.stroke();
        };

        figura('rgba(255,255,255,.96)', 30, 30);
        figura('#' + TEMA.accent.getHexString(), 16, 22);

        _texMonito = new THREE.CanvasTexture(c);
        _texMonito.colorSpace = THREE.SRGBColorSpace;
        return _texMonito;
    }

    function _pintarFantasma(p) {
        if (!_fantasma) {
            const g = new THREE.Group();

            // Anillo en el suelo: marca el sitio exacto donde caerá
            const disco = new THREE.Mesh(
                new THREE.RingGeometry(0.6, 0.85, 44),
                new THREE.MeshBasicMaterial({
                    color: TEMA.accent, transparent: true, opacity: 0.92,
                    side: THREE.DoubleSide, depthTest: false, depthWrite: false,
                })
            );
            disco.rotation.x = -Math.PI / 2;
            disco.position.y = 0.04;
            disco.renderOrder = 40;
            g.add(disco);

            const fig = new THREE.Sprite(new THREE.SpriteMaterial({
                map: _texturaMonito(), transparent: true,
                depthTest: false, depthWrite: false,
            }));
            fig.scale.set(1.15, 1.85, 1);
            fig.position.y = 1.05;
            fig.renderOrder = 41;
            g.add(fig);

            _scene.add(g);
            _fantasma = g;
        }

        _fantasma.position.set(p.x, 0, p.z);
        _fantasma.visible = true;

        // A tamaño real, desde el encuadre general el monito mide cuatro píxeles
        // y no se ve dónde vas a caer. Se le fija una altura aparente mínima:
        // de cerca queda a escala 1 y solo crece cuando la distancia lo exige.
        const dist = Math.max(0.001, _camera.position.distanceTo(_fantasma.position));
        const tanFov = Math.tan(THREE.MathUtils.degToRad(_camera.fov) / 2);
        const pxReales = ALTO_MONITO * _alto() / (2 * dist * tanFov);
        _fantasma.scale.setScalar(Math.max(1, PX_MONITO_MIN / Math.max(1e-3, pxReales)));

        _dirty = true;
    }

    function _quitarFantasma() {
        if (!_fantasma) return;
        _scene?.remove(_fantasma);
        _fantasma.traverse(n => {
            n.geometry?.dispose?.();
            if (n.material) { n.material.map?.dispose?.(); n.material.dispose(); }
        });
        _fantasma = null;
        _texMonito = null;
        _dirty = true;
    }

    /** Resuelve el punto de aterrizaje a partir de coordenadas de pantalla. */
    function _destinoOperario(clientX, clientY) {
        const bruto = _puntoPiso(clientX, clientY);
        if (!bruto || !_dentroDeNave(bruto)) return null;

        const p = _puntoCaminable(bruto);
        return { punto: p, zona: _zonaEn(p), rack: _rackCerca(p) };
    }

    /**
     * Vista previa mientras se arrastra el monito.
     * @returns {{zona:string|null, rack:string|null}|null} null si ahí no se puede soltar
     */
    function preverOperario(clientX, clientY) {
        const d = _destinoOperario(clientX, clientY);
        if (!d) { ocultarOperario(); return null; }

        _pintarFantasma(d.punto);
        return { zona: d.zona, rack: d.rack };
    }

    function ocultarOperario() {
        if (_fantasma) { _fantasma.visible = false; _dirty = true; }
    }

    /**
     * Suelta el monito: la cámara vuela hasta el punto y se queda de pie ahí.
     * @returns {Promise<{zona:string|null, rack:string|null}|null>}
     */
    function soltarOperario(clientX, clientY, opts = {}) {
        const d = _destinoOperario(clientX, clientY);
        ocultarOperario();
        if (!d) return Promise.resolve(null);

        const rumbo = new THREE.Vector3();
        _camera.getWorldDirection(rumbo);
        rumbo.y = 0;
        if (rumbo.lengthSq() < 1e-4) rumbo.set(0, 0, -1);
        rumbo.normalize();

        const pos = new THREE.Vector3(d.punto.x, OPERARIO.ojos, d.punto.z);
        const mira = pos.clone().addScaledVector(rumbo, OPERARIO.mira);
        // Ligeramente por debajo de los ojos: mirar del todo horizontal choca
        // con el tope polar de OrbitControls y deja la vista temblona.
        mira.y = OPERARIO.ojos * 0.9;

        seleccionarRack(null);

        return _volarPorAlmacen(pos, mira, opts)
            .then(() => ({ zona: d.zona, rack: d.rack }));
    }

    // ═══════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════
    return {
        init,
        build,
        mount,
        unmount,
        dispose,
        zoomFit,
        setPreset,
        focusRack,
        focusUbicacion,
        marcarUbicacion,
        seleccionarRack,
        preverOperario,
        ocultarOperario,
        soltarOperario,

        /** Posición y punto de mira actuales (solo lectura, para depurar encuadres). */
        estadoCamara() {
            if (!_camera) return null;
            const p = _camera.position, t = _controls.target;
            return { pos: { x: p.x, y: p.y, z: p.z }, mira: { x: t.x, y: t.y, z: t.z } };
        },
        setConfig,
        getConfig,
        refrescarTema,
        get listo() { return !!_renderer; },
        set onRackClick(fn) { _onRackClick = fn; },
        set onRackHover(fn) { _onRackHover = fn; },
        set onTarimaClick(fn) { _onTarimaClick = fn; },

        // Helpers compartidos con los visores del modal (rack-3d-viewer.js),
        // para no tener una cuarta copia de la lógica de tema y ocupación.
        lib: {
            leerTema: _leerTema,
            tema: () => TEMA || _leerTema(),
            colorOcup: _colorOcup,
            ocupNivel: _ocupNivel,
            ocupRack: _ocupRack,
            etiquetaOcup: _etiquetaOcup,
            caja: _caja,
            pintar: _pintar,
            sprite: _sprite,
            escuadras: _escuadras,
            fmt: _fmt,
            esc: _esc,
            M,
        },
    };

})();

window.AV3D = AV3D;
export const lib = AV3D.lib;

document.dispatchEvent(new CustomEvent('av3d:ready'));
