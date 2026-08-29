/* ==========================================================================
   Taller de barras en 3D

   Misma idea que el almacén virtual, pero el objeto que se dibuja aquí no es
   una tarima sino una barra: cada pieza física de `corte_piezas` es un cuerpo
   con su longitud REAL en metros. Tubo se dibuja como cilindro y barra como
   perfil macizo, según `catproductos.es_tubo`.

   Las piezas de un producto se apilan alineadas por su extremo izquierdo, como
   se estiban de verdad en un rack de material largo. Esa alineación es lo que
   hace la vista útil: la escalera que forman los extremos derechos ES la
   distribución de longitudes disponibles, que es justo lo que hay que mirar
   para decidir de qué barra cortar.

   Se expone como `window.Barras3D` y avisa con el evento `barras3d:ready`.
   ========================================================================== */

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const Barras3D = (() => {
    'use strict';

    // ── Medidas físicas de referencia (m) ────────────────────────────────
    //  Un almacén de material largo no se estiba en tarimas: se guarda en
    //  racks cantilever, postes verticales con brazos en voladizo sobre los
    //  que descansan las barras. Estas medidas son las de un cantilever real.
    const M = {
        // Barras algo más gruesas y separadas que en la realidad: a tamaño
        // exacto se funden en una plancha y se pierde el conteo de piezas.
        diametro: 0.12,
        holgura: 0.055,
        largoMin: 0.25,    // para que un recorte diminuto siga siendo visible

        // Tope de lo que se DIBUJA. El material sin configurar llega como una
        // sola "barra" con la existencia entera (500 m y más): a escala real
        // el rack se vuelve kilométrico y el resto del almacén deja de verse.
        // La longitud real se sigue mostrando en el tooltip y en la ficha.
        largoMaxRender: 12,

        nivelesPorRack: 4,
        alturaNivel: 0.85,
        alturaBase: 0.28,    // del suelo al primer brazo
        profundidad: 1.05,   // voladizo del brazo
        posteAncho: 0.13,
        brazoAlto: 0.09,
        vanoPoste: 2.6,    // separación entre postes a lo largo del rack
        pasillo: 3.2,    // hueco entre racks para que pase el operario
        muroAlto: 7,
        margenNave: 4,
    };

    // ── Estado ───────────────────────────────────────────────────────────
    let _renderer, _scene, _camera, _controls, _raycaster, _clock;
    let _container = null;
    let _mounted = false;
    let _dirty = true;
    let _initFallo = false;
    let _resizeObs = null;

    let _grpMundo = null;
    let _grpPiezas = null;

    let _piezaMeshes = [];     // mallas seleccionables
    let _muros       = [];     // { mesh, normal } de los muros de la nave
    let _piezasIdx = new Map();  // id_pieza → { mesh, datos, producto }
    let _labels = [];
    let _codigoSprites = [];   // sprites del código de barras de cada pieza real
    let _bbox = new THREE.Box3();

    let _hoverId = null;
    let _selId = null;
    let _halo = null;
    let _flying = null;
    let _modo = 'tipo';     // 'tipo' | 'longitud'

    // ── Modo "marcar corte": clic sobre una barra real para elegir dónde
    //    cortar, sin pelearse con el arrastre de OrbitControls (que solo
    //    dispara con un desplazamiento > 5px, un clic normal no cuenta).
    let _modoCorte = false;
    let _marcaCorte = null;
    let _onPiezaCorte = null;

    // ── Códigos de barras pintados sobre cada barra: opt-in y solo cerca de
    //    la cámara (LOD por distancia), porque con cientos de piezas dibujar
    //    todas las etiquetas a la vez sería ilegible y caro de renderizar.
    let _mostrarCodigos = false;
    const RADIO_CODIGOS = 5;   // metros: más lejos no se alcanzaría a leer igual

    let _datos = null;
    let _onPiezaClick = null;
    let _onPiezaHover = null;

    const _mouseNDC = new THREE.Vector2();
    const _tmpV = new THREE.Vector3();

    // ── Tema: se lee del layout, igual que el almacén virtual ────────────
    const TEMA = {};

    function _cssVar(nombre, alterno) {
        const v = getComputedStyle(document.documentElement).getPropertyValue(nombre).trim();
        return v || alterno;
    }

    function _leerTema() {
        const oscuro = (document.documentElement.getAttribute('data-theme') === 'dark');
        TEMA.oscuro = oscuro;
        TEMA.fondo = new THREE.Color(_cssVar('--bg-gray', oscuro ? '#0b1220' : '#eef2f7'));
        TEMA.piso = new THREE.Color(oscuro ? '#131c2b' : '#dfe6ee');
        TEMA.linea = new THREE.Color(oscuro ? '#24334a' : '#c3cedb');
        TEMA.acero = new THREE.Color(_cssVar('--accent-blue', '#3b82f6'));
        TEMA.laton = new THREE.Color(_cssVar('--warning-color', '#d97706'));
        TEMA.exito = new THREE.Color(_cssVar('--success-color', '#059669'));
        TEMA.peligro = new THREE.Color(_cssVar('--danger-color', '#dc2626'));
        TEMA.texto = new THREE.Color(_cssVar('--text-dark', oscuro ? '#e2e8f0' : '#0f172a'));
        TEMA.rack = new THREE.Color(oscuro ? '#1f2b3e' : '#94a3b8');
        TEMA.muro = new THREE.Color(oscuro ? '#101a29' : '#e8edf3');
        return TEMA;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Arranque
    // ═════════════════════════════════════════════════════════════════════
    function init(containerId) {
        if (_initFallo) return false;
        _container = document.getElementById(containerId || 'ac-3d-container');
        if (!_container || _renderer) return !!_renderer;

        _leerTema();

        try {
            _renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
        } catch (err) {
            console.warn('[Barras3D] WebGL no disponible:', err);
            _renderer = null;
            _initFallo = true;
            return false;
        }

        _renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        _renderer.setSize(_ancho(), _alto(), false);
        _renderer.outputColorSpace = THREE.SRGBColorSpace;
        _renderer.shadowMap.enabled = true;
        _renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        _container.appendChild(_renderer.domElement);

        _scene = new THREE.Scene();
        _scene.background = TEMA.fondo.clone();
        _scene.fog = new THREE.Fog(TEMA.fondo, 40, 220);

        _camera = new THREE.PerspectiveCamera(45, _ancho() / _alto(), 0.1, 1000);
        _camera.position.set(12, 9, 16);

        _controls = new OrbitControls(_camera, _renderer.domElement);
        _controls.enableDamping = true;
        _controls.dampingFactor = 0.08;
        _controls.minDistance = 2;
        _controls.maxDistance = 300;
        _controls.maxPolarAngle = Math.PI * 0.49;

        _raycaster = new THREE.Raycaster();
        _clock = new THREE.Clock();

        _grpMundo = new THREE.Group();
        _grpPiezas = new THREE.Group();
        _scene.add(_grpMundo, _grpPiezas);

        _luces();
        _bindEventos();

        _resizeObs = new ResizeObserver(_onResize);
        _resizeObs.observe(_container);

        _renderer.setAnimationLoop(_loop);
        return true;
    }

    function _ancho() { return Math.max(1, _container?.clientWidth || 1); }
    function _alto() { return Math.max(1, _container?.clientHeight || 1); }

    function _luces() {
        const hemi = new THREE.HemisphereLight(0xffffff, 0x404050, TEMA.oscuro ? 1.1 : 1.5);
        _scene.add(hemi);

        const sol = new THREE.DirectionalLight(0xffffff, TEMA.oscuro ? 1.5 : 1.9);
        sol.position.set(18, 26, 14);
        sol.castShadow = true;
        sol.shadow.mapSize.set(1024, 1024);
        sol.shadow.camera.near = 1;
        sol.shadow.camera.far = 120;
        [sol.shadow.camera.left, sol.shadow.camera.right] = [-40, 40];
        sol.shadow.camera.top = 40;
        sol.shadow.camera.bottom = -40;
        _scene.add(sol, sol.target);

        _scene.add(new THREE.AmbientLight(0xffffff, TEMA.oscuro ? 0.35 : 0.5));
    }

    function _onResize() {
        if (!_renderer || !_container) return;
        const w = _ancho(), h = _alto();
        if (w < 2 || h < 2) return;
        _renderer.setSize(w, h, false);
        _camera.aspect = w / h;
        _camera.updateProjectionMatrix();
        _dirty = true;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Construcción de la escena
    // ═════════════════════════════════════════════════════════════════════
    function build(productos, opts = {}) {
        if (!init()) return;
        _datos = productos || [];
        _leerTema();

        const camPrev = opts.mantenerCamara
            ? { pos: _camera.position.clone(), target: _controls.target.clone() }
            : null;

        _limpiar(_grpMundo);
        _limpiar(_grpPiezas);
        _piezaMeshes = [];
        _muros = [];
        _piezasIdx.clear();
        _labels = [];
        _codigoSprites = [];
        _hoverId = null; _selId = null; _halo = null;
        if (_marcaCorte) _marcaCorte.visible = false;

        // Solo entran productos que tengan barras físicas que enseñar
        const conMaterial = _datos
            .map(prod => ({
                prod,
                piezas: (prod.cortes || [])
                    .flatMap(c => (c.piezas || []).map(p => ({ ...p, folio: c.folio, corte: c })))
                    .sort((a, b) => parseFloat(b.longitud) - parseFloat(a.longitud)),
            }))
            .filter(x => x.piezas.length);

        // Todos los racks miden lo mismo: un almacén real no tiene una
        // estantería a medida por producto, y la nave queda regular.
        // Se mide sobre la longitud DIBUJADA, no la real: si no, una barra sin
        // configurar de 500 m estiraría el rack (y la nave) hasta lo absurdo.
        const largoRack = Math.max(
            6,
            ...conMaterial.flatMap(x => x.piezas.map(p => _largoDibujo(p)))
        ) + 0.9;

        // Cada rack aloja hasta `nivelesPorRack` productos, uno por nivel
        const racks = [];
        for (let i = 0; i < conMaterial.length; i += M.nivelesPorRack) {
            racks.push(conMaterial.slice(i, i + M.nivelesPorRack));
        }

        const pasoZ = M.profundidad + M.pasillo;
        racks.forEach((niveles, i) => _rack(niveles, i, i * pasoZ, largoRack));

        _bbox.setFromObject(_grpPiezas);
        if (_bbox.isEmpty()) {
            _bbox.set(new THREE.Vector3(-2, 0, -2), new THREE.Vector3(8, 3, 8));
        }

        // La nave se levanta alrededor de lo que hay: si se dimensionara antes
        // quedaría una explanada enorme con el material en una esquina.
        _nave(largoRack, Math.max(racks.length, 1) * pasoZ);

        _ajustarProfundidad();

        if (camPrev) {
            _camera.position.copy(camPrev.pos);
            _controls.target.copy(camPrev.target);
            _controls.update();
        } else {
            zoomFit(false);
        }
        _dirty = true;
    }

    /**
     * Un rack cantilever completo: postes, brazos y el material de cada nivel.
     * @param {Array} niveles  un producto (con sus piezas) por nivel
     */
    function _rack(niveles, indice, zBase, largoRack) {
        const g = new THREE.Group();
        g.position.set(0, 0, zBase);

        const matAcero = new THREE.MeshStandardMaterial({
            color: TEMA.rack, roughness: 0.7, metalness: 0.35,
        });

        const alturaTotal = M.alturaBase + M.nivelesPorRack * M.alturaNivel;
        const nPostes = Math.max(2, Math.round(largoRack / M.vanoPoste) + 1);

        for (let i = 0; i < nPostes; i++) {
            const x = (largoRack / (nPostes - 1)) * i;

            // Poste vertical, al fondo del rack
            const poste = new THREE.Mesh(
                new THREE.BoxGeometry(M.posteAncho, alturaTotal, M.posteAncho), matAcero);
            poste.position.set(x, alturaTotal / 2, -M.posteAncho / 2);
            poste.castShadow = true;
            g.add(poste);

            // Zapata: el pie que evita que el cantilever vuelque
            const zapata = new THREE.Mesh(
                new THREE.BoxGeometry(M.posteAncho * 2.2, 0.09, M.profundidad * 0.85), matAcero);
            zapata.position.set(x, 0.045, M.profundidad * 0.35);
            zapata.receiveShadow = true;
            g.add(zapata);

            // Un brazo en voladizo por nivel
            for (let n = 0; n < M.nivelesPorRack; n++) {
                const y = M.alturaBase + n * M.alturaNivel;
                const brazo = new THREE.Mesh(
                    new THREE.BoxGeometry(M.posteAncho * 0.8, M.brazoAlto, M.profundidad), matAcero);
                brazo.position.set(x, y + M.brazoAlto / 2, M.profundidad / 2);
                brazo.castShadow = true;
                g.add(brazo);

                // Tope del extremo, para que las barras no se salgan
                const tope = new THREE.Mesh(
                    new THREE.BoxGeometry(M.posteAncho * 0.7, 0.16, 0.05), matAcero);
                tope.position.set(x, y + 0.1, M.profundidad - 0.03);
                g.add(tope);
            }
        }

        // Material de cada nivel
        niveles.forEach((entrada, n) => {
            const y = M.alturaBase + n * M.alturaNivel + M.brazoAlto;
            _cargarNivel(g, entrada.prod, entrada.piezas, y);

            // Rótulo del nivel, en el costado del rack. Los niveles van cada
            // 0.85 m: el sprite tiene que medir menos que eso o se solapan.
            const piezasReales = entrada.piezas.filter(p => !p._sinDesglosar).length;
            const rotulo = _sprite(
                entrada.prod.cve_prod || '—',
                (entrada.prod.es_tubo ? 'TUBO' : 'BARRA') + ' · ' +
                    (piezasReales > 0 ? piezasReales + ' pz' : 'sin desglosar'),
                entrada.prod.es_tubo ? TEMA.acero : TEMA.laton
            );
            rotulo.scale.set(1.55, 0.48, 1);
            rotulo.position.set(-1.15, y + 0.26, M.profundidad / 2);
            g.add(rotulo);
            _labels.push(rotulo);
        });

        // Cartel del rack, sobre la cabecera
        const cartel = _sprite('RACK ' + (indice + 1), niveles.length + ' niveles', TEMA.exito);
        cartel.scale.set(1.5, 0.47, 1);
        cartel.position.set(-1.15, alturaTotal + 0.5, M.profundidad / 2);
        g.add(cartel);
        _labels.push(cartel);

        _grpPiezas.add(g);
    }

    /**
     * Coloca las barras de un producto sobre los brazos de un nivel.
     * Se alinean por su extremo izquierdo a propósito: los extremos derechos
     * forman una escalera que se lee de un vistazo como la distribución de
     * longitudes disponibles, que es lo que hay que mirar para decidir de qué
     * barra cortar.
     */
    function _cargarNivel(grupo, prod, piezas, yBase) {
        const paso = M.diametro + M.holgura;
        const porFila = Math.max(1, Math.floor((M.profundidad - 0.1) / paso));
        const capasMax = Math.max(1, Math.floor((M.alturaNivel - M.brazoAlto - 0.12) / paso));

        piezas.forEach((pieza, i) => {
            const enCapa = Math.floor(i / porFila);
            const col = i % porFila;

            // Si el nivel se llena, las últimas se apilan encima en vez de
            // desaparecer: es lo que pasa en el rack de verdad.
            const capa = Math.min(enCapa, capasMax - 1);

            const largo = _largoDibujo(pieza);
            const mesh = _cuerpoPieza(prod, pieza, largo);

            mesh.position.set(
                0.35 + largo / 2,
                yBase + M.diametro / 2 + capa * paso,
                0.08 + col * paso + M.diametro / 2
            );
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            mesh.userData = {
                idPieza: pieza.id_pieza, pieza, producto: prod,
                truncada: _estaTruncada(pieza), largoDibujo: largo,
            };

            grupo.add(mesh);
            _piezaMeshes.push(mesh);
            _piezasIdx.set(pieza.id_pieza, {
                mesh, pieza, producto: prod, grupo, truncada: _estaTruncada(pieza),
            });

            // Marca de continuidad: sin ella una barra de 500 m dibujada a 12
            // se leería como una barra de 12, que es peor que no dibujarla.
            if (_estaTruncada(pieza)) {
                grupo.add(_marcaTruncado(mesh.position, largo));
            }

            // Etiqueta del código de barras, pegada al extremo izquierdo (el
            // que da al pasillo): es donde se pega la etiqueta física de
            // verdad y donde un operario la buscaría primero.
            if (!pieza._sinDesglosar && pieza.codigo && window.CodigoBarras) {
                const sp = _spriteCodigo(pieza.codigo);
                sp.position.set(-largo / 2 - M.diametro * 0.9, 0, 0);
                mesh.add(sp);
                _codigoSprites.push(sp);
            }
        });
    }

    /** Sprite con el Code128 de una pieza, dibujado en un canvas aparte. */
    function _spriteCodigo(codigo) {
        const canvas = document.createElement('canvas');
        window.CodigoBarras.enCanvas(canvas, codigo, { modulo: 1.3, alto: 24, tamTexto: 9, margen: 3 });

        const tex = new THREE.CanvasTexture(canvas);
        tex.colorSpace = THREE.SRGBColorSpace;
        const sp = new THREE.Sprite(new THREE.SpriteMaterial({
            map: tex, transparent: true, depthTest: false, depthWrite: false,
        }));

        const anchoMundo = 0.5;
        sp.scale.set(anchoMundo, anchoMundo * (canvas.height / canvas.width), 1);
        sp.renderOrder = 12;
        sp.visible = false;   // solo se prende cerca y con el modo activo
        return sp;
    }

    /** Prende/apaga los códigos que quedan a tiro de cámara; el resto ni se calcula. */
    function _actualizarCodigos() {
        if (!_codigoSprites.length) return;
        if (!_mostrarCodigos) return;

        _codigoSprites.forEach(sp => {
            sp.getWorldPosition(_tmpV);
            const cerca = _camera.position.distanceTo(_tmpV) < RADIO_CODIGOS;
            if (sp.visible !== cerca) { sp.visible = cerca; _dirty = true; }
        });
    }

    /**
     * Tres cuñas en el extremo de una barra recortada, como el símbolo de
     * «continúa» de un plano acotado.
     */
    function _marcaTruncado(posicionBarra, largo) {
        const g = new THREE.Group();
        const mat = new THREE.MeshBasicMaterial({ color: TEMA.peligro });

        for (let i = 0; i < 3; i++) {
            const cuna = new THREE.Mesh(
                new THREE.ConeGeometry(M.diametro * 0.55, M.diametro * 0.9, 4), mat);
            cuna.rotation.z = -Math.PI / 2;   // la punta mira a +X, hacia fuera
            cuna.position.set(i * M.diametro * 0.75, 0, 0);
            g.add(cuna);
        }

        g.position.set(
            posicionBarra.x + largo / 2 + M.diametro * 0.6,
            posicionBarra.y,
            posicionBarra.z
        );
        return g;
    }

    /** Longitud con la que se dibuja una pieza, acotada por `largoMaxRender`. */
    function _largoDibujo(pieza) {
        const real = parseFloat(pieza.longitud) || 0;
        return Math.min(M.largoMaxRender, Math.max(M.largoMin, real));
    }

    function _estaTruncada(pieza) {
        return (parseFloat(pieza.longitud) || 0) > M.largoMaxRender + 0.001;
    }

    /** Cilindro si es tubo, prisma si es barra. */
    function _cuerpoPieza(prod, pieza, largo) {
        const color = _colorPieza(prod, pieza);
        const mat = new THREE.MeshStandardMaterial({
            color, roughness: prod.es_tubo ? 0.35 : 0.6,
            metalness: prod.es_tubo ? 0.75 : 0.35,
        });

        // La existencia sin desglosar todavía no es "una barra": se dibuja
        // translúcida y sin brillo metálico para que se lea como un bloque
        // provisional, no como una pieza física real.
        if (pieza._sinDesglosar) {
            mat.transparent = true;
            mat.opacity = 0.42;
            mat.metalness = 0;
            mat.roughness = 1;
        }

        if (prod.es_tubo) {
            // El cilindro nace en Y; se tumba sobre X, que es la dirección larga
            const geo = new THREE.CylinderGeometry(M.diametro / 2, M.diametro / 2, largo, 16, 1, false);
            geo.rotateZ(Math.PI / 2);
            return new THREE.Mesh(geo, mat);
        }

        const lado = M.diametro * 0.86;   // perfil cuadrado, algo menor que el tubo
        return new THREE.Mesh(new THREE.BoxGeometry(largo, lado, lado), mat);
    }

    function _colorPieza(prod, pieza) {
        if (pieza._sinDesglosar) return TEMA.rack.clone();

        if (_modo === 'longitud') {
            // Escala de calor: rojo lo más corto (recortes), verde lo más largo.
            // Se gradúa contra el tope de dibujo para que las barras sin
            // configurar no dejen todo lo demás en el mismo color.
            const L = parseFloat(pieza.longitud) || 0;
            const t = THREE.MathUtils.clamp(L / M.largoMaxRender, 0, 1);
            return new THREE.Color().setHSL(THREE.MathUtils.lerp(0, 0.33, t), 0.68, 0.5);
        }
        return (prod.es_tubo ? TEMA.acero : TEMA.laton).clone();
    }

    /**
     * Niebla y distancia máxima de órbita, en función del tamaño real de la
     * escena.
     *
     * Esto era un bug de verdad: con la niebla fija en 40-220 unidades, un
     * almacén grande quedaba ENTERO más allá del plano lejano y no se dibujaba
     * nada. Solo reaparecía el producto al que se acercaba la cámara con
     * `focusPieza`, lo que parecía «solo se ve el último producto».
     */
    function _ajustarProfundidad() {
        const tam = _bbox.getSize(new THREE.Vector3());
        const radio = Math.max(Math.max(tam.x, tam.z) / 2, 4);

        // Al encuadrar, la cámara se queda alrededor de 2.5·radio del centro:
        // la niebla solo debe morder bastante más lejos que eso.
        _scene.fog.near = radio * 3.5;
        _scene.fog.far = radio * 14;

        _controls.maxDistance = Math.max(300, radio * 12);
        _camera.far = Math.max(1000, radio * 30);
        _camera.updateProjectionMatrix();
    }

    /** Piso, retícula y muros perimetrales alrededor de los racks. */
    function _nave(largoRack, fondoRacks) {
        const w = largoRack + M.margenNave * 2;
        const d = fondoRacks + M.margenNave * 2;
        const cx = largoRack / 2;
        const cz = fondoRacks / 2 - M.pasillo / 2;

        const suelo = new THREE.Mesh(
            new THREE.PlaneGeometry(w, d),
            new THREE.MeshStandardMaterial({ color: TEMA.piso, roughness: 1, metalness: 0 })
        );
        suelo.rotation.x = -Math.PI / 2;
        suelo.position.set(cx, -0.01, cz);
        suelo.receiveShadow = true;
        _grpMundo.add(suelo);

        // Retícula de metro, recortada al piso
        const pts = [];
        const x0 = cx - w / 2, x1 = cx + w / 2;
        const z0 = cz - d / 2, z1 = cz + d / 2;
        for (let x = Math.ceil(x0); x <= x1; x++) pts.push(x, 0, z0, x, 0, z1);
        for (let z = Math.ceil(z0); z <= z1; z++) pts.push(x0, 0, z, x1, 0, z);

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
        const grid = new THREE.LineSegments(geo, new THREE.LineBasicMaterial({
            color: TEMA.linea, transparent: true, opacity: TEMA.oscuro ? 0.22 : 0.4,
        }));
        grid.position.y = 0.002;
        _grpMundo.add(grid);

        _murosPerimetrales(cx, cz, w, d);
    }

    /**
     * Los cuatro muros de la nave. Se desvanecen los que quedan entre la
     * cámara y el interior: sin eso, orbitando se mira siempre una pared.
     */
    function _murosPerimetrales(cx, cz, w, d) {
        const h = M.muroAlto;
        const mat = () => new THREE.MeshStandardMaterial({
            color: TEMA.muro, roughness: 0.95, metalness: 0.02,
            transparent: true, opacity: 0.95, side: THREE.DoubleSide,
        });

        const defs = [
            { w: w, x: cx, z: cz - d / 2, rot: 0, normal: new THREE.Vector3(0, 0, -1) },
            { w: w, x: cx, z: cz + d / 2, rot: 0, normal: new THREE.Vector3(0, 0, 1) },
            { w: d, x: cx - w / 2, z: cz, rot: Math.PI / 2, normal: new THREE.Vector3(-1, 0, 0) },
            { w: d, x: cx + w / 2, z: cz, rot: Math.PI / 2, normal: new THREE.Vector3(1, 0, 0) },
        ];

        defs.forEach(def => {
            const muro = new THREE.Mesh(new THREE.PlaneGeometry(def.w, h), mat());
            muro.position.set(def.x, h / 2, def.z);
            muro.rotation.y = def.rot;
            muro.receiveShadow = true;
            _grpMundo.add(muro);
            _muros.push({ mesh: muro, normal: def.normal });
        });
    }

    // Los rótulos se dibujan con depthTest:false para que no los tape el rack.
    // El precio es que, metido en el pasillo, un cartel a metro y medio ocupa
    // media pantalla: se les pone tope de tamaño aparente.
    const PX_ROTULO_MAX = 44;

    function _actualizarRotulos() {
        if (!_labels.length) return;

        const tanFov = Math.tan(THREE.MathUtils.degToRad(_camera.fov) / 2);
        const altoPx = _alto();

        _labels.forEach(sp => {
            if (!sp.userData.escalaBase) sp.userData.escalaBase = sp.scale.clone();
            const base = sp.userData.escalaBase;

            sp.getWorldPosition(_tmpV);
            const d = Math.max(0.001, _camera.position.distanceTo(_tmpV));
            const px = base.y * altoPx / (2 * d * tanFov);
            const k = px > PX_ROTULO_MAX ? PX_ROTULO_MAX / px : 1;

            sp.scale.set(base.x * k, base.y * k, 1);
        });
    }

    /** Atenúa los muros que tapan la vista, según hacia dónde mira la cámara. */
    function _actualizarMuros() {
        if (!_muros.length) return;
        _camera.getWorldDirection(_tmpV);

        _muros.forEach(({ mesh, normal }) => {
            // La normal exterior del muro cercano apunta contra la dirección
            // de vista: ese es el que estorba.
            const tapa = normal.dot(_tmpV) < -0.12;
            const objetivo = tapa ? 0.04 : 0.92;
            if (Math.abs(mesh.material.opacity - objetivo) > 0.01) {
                mesh.material.opacity += (objetivo - mesh.material.opacity) * 0.18;
                _dirty = true;
            }
            mesh.visible = mesh.material.opacity > 0.02;
        });
    }

    function _sprite(titulo, sub, color) {
        const c = document.createElement('canvas');
        c.width = 512; c.height = 160;
        const x = c.getContext('2d');

        x.fillStyle = TEMA.oscuro ? 'rgba(11,18,32,.88)' : 'rgba(255,255,255,.92)';
        _rectRedondo(x, 4, 4, 504, 152, 18);
        x.fill();
        x.strokeStyle = '#' + color.getHexString();
        x.lineWidth = 5;
        x.stroke();

        x.fillStyle = '#' + TEMA.texto.getHexString();
        x.font = 'bold 62px system-ui, sans-serif';
        x.textAlign = 'center';
        x.fillText(titulo, 256, 76);

        x.fillStyle = '#' + color.getHexString();
        x.font = 'bold 34px system-ui, sans-serif';
        x.fillText(sub, 256, 126);

        const tex = new THREE.CanvasTexture(c);
        tex.colorSpace = THREE.SRGBColorSpace;
        const sp = new THREE.Sprite(new THREE.SpriteMaterial({
            map: tex, transparent: true, depthTest: false, depthWrite: false,
        }));
        sp.scale.set(2.2, 0.69, 1);
        sp.renderOrder = 20;
        return sp;
    }

    function _rectRedondo(x, px, py, w, h, r) {
        x.beginPath();
        x.moveTo(px + r, py);
        x.arcTo(px + w, py, px + w, py + h, r);
        x.arcTo(px + w, py + h, px, py + h, r);
        x.arcTo(px, py + h, px, py, r);
        x.arcTo(px, py, px + w, py, r);
        x.closePath();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Interacción
    // ═════════════════════════════════════════════════════════════════════
    function _bindEventos() {
        const dom = _renderer.domElement;
        let down = null;

        dom.addEventListener('pointerdown', e => { down = { x: e.clientX, y: e.clientY }; });

        dom.addEventListener('pointermove', e => {
            _actualizarNDC(e);
            const hit = _intersectar();
            const id = hit ? hit.object.userData.idPieza : null;

            if (_modoCorte) {
                const corte = _actualizarMarcaCorte(hit);
                dom.style.cursor = corte ? 'crosshair' : '';
                if (id !== _hoverId) { _hoverId = id; _dirty = true; }
                _onPiezaHover?.(id ? _piezasIdx.get(id) : null, e, corte);
                return;
            }

            if (id !== _hoverId) {
                _hoverId = id;
                dom.style.cursor = id ? 'pointer' : '';
                _onPiezaHover?.(id ? _piezasIdx.get(id) : null, e);
                _dirty = true;
            } else if (id) {
                _onPiezaHover?.(_piezasIdx.get(id), e);
            }
        });

        dom.addEventListener('pointerleave', () => {
            _hoverId = null;
            _onPiezaHover?.(null);
            _actualizarMarcaCorte(null);
            _dirty = true;
        });

        dom.addEventListener('pointerup', e => {
            // Un arrastre para orbitar no debe contar como clic de selección
            if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) > 5) { down = null; return; }
            down = null;

            _actualizarNDC(e);
            const hit = _intersectar();
            if (!hit) { seleccionar(null); return; }

            const entrada = _piezasIdx.get(hit.object.userData.idPieza);

            if (_modoCorte && entrada && !entrada.pieza._sinDesglosar) {
                const corte = _calcularCorte(hit);
                if (corte) _onPiezaCorte?.(entrada, corte.longitudCorte, corte.sobrante);
                return;
            }

            seleccionar(hit.object.userData.idPieza);
            _onPiezaClick?.(entrada);
        });

        dom.addEventListener('contextmenu', e => e.preventDefault());
        _controls.addEventListener('change', () => { _dirty = true; });
    }

    function _actualizarNDC(e) {
        const r = _renderer.domElement.getBoundingClientRect();
        _mouseNDC.x = ((e.clientX - r.left) / r.width) * 2 - 1;
        _mouseNDC.y = -((e.clientY - r.top) / r.height) * 2 + 1;
    }

    function _intersectar() {
        if (!_piezaMeshes.length) return null;
        _raycaster.setFromCamera(_mouseNDC, _camera);
        const hits = _raycaster.intersectObjects(_piezaMeshes, false);
        return hits.length ? hits[0] : null;
    }

    /**
     * A partir del punto donde el rayo tocó la barra, la distancia desde su
     * extremo izquierdo (el origen físico) hasta ese punto: es la longitud
     * que se cortaría ahí. Piezas dibujadas acortadas (>largoMaxRender) solo
     * se pueden marcar dentro del tramo visible; el resto se ajusta a mano
     * en el modal, que sigue totalmente editable.
     */
    function _calcularCorte(hit) {
        const mesh = hit.object;
        const largoDibujo = mesh.userData.largoDibujo || 0;
        if (largoDibujo <= 0) return null;

        const local = mesh.worldToLocal(hit.point.clone());
        const desdeInicio = THREE.MathUtils.clamp(local.x + largoDibujo / 2, 0.01, largoDibujo);

        const longitudReal = parseFloat(mesh.userData.pieza.longitud) || 0;
        const longitudCorte = Math.min(Math.round(desdeInicio * 100) / 100, longitudReal);
        return { longitudCorte, sobrante: Math.max(0, longitudReal - longitudCorte) };
    }

    /** Anillo que marca sobre la barra dónde caería el corte mientras se apunta. */
    function _actualizarMarcaCorte(hit) {
        const valido = hit && !hit.object.userData.pieza._sinDesglosar;
        if (!valido) {
            if (_marcaCorte) _marcaCorte.visible = false;
            _dirty = true;
            return null;
        }

        const corte = _calcularCorte(hit);
        if (!corte) { if (_marcaCorte) _marcaCorte.visible = false; return null; }

        if (!_marcaCorte) {
            const geo = new THREE.TorusGeometry(M.diametro * 0.85, M.diametro * 0.09, 8, 20);
            const mat = new THREE.MeshBasicMaterial({
                color: TEMA.peligro, transparent: true, opacity: 0.9, depthTest: false,
            });
            _marcaCorte = new THREE.Mesh(geo, mat);
            _marcaCorte.rotation.y = Math.PI / 2;
            _marcaCorte.renderOrder = 15;
            _scene.add(_marcaCorte);
        }

        const mesh = hit.object;
        const largoDibujo = mesh.userData.largoDibujo;
        const localX = THREE.MathUtils.clamp(corte.longitudCorte - largoDibujo / 2, -largoDibujo / 2, largoDibujo / 2);
        _marcaCorte.position.copy(mesh.localToWorld(new THREE.Vector3(localX, 0, 0)));
        _marcaCorte.visible = true;
        _dirty = true;
        return corte;
    }

    /** Marca una pieza con un halo que la distingue del resto. */
    function seleccionar(idPieza) {
        if (_halo) {
            _halo.parent?.remove(_halo);
            _halo.geometry.dispose();
            _halo.material.dispose();
            _halo = null;
        }

        _selId = idPieza;
        if (idPieza == null) { _dirty = true; return; }

        const e = _piezasIdx.get(idPieza);
        if (!e) { _dirty = true; return; }

        const caja = new THREE.Box3().setFromObject(e.mesh);
        const tam = caja.getSize(new THREE.Vector3());
        const centro = caja.getCenter(new THREE.Vector3());

        const halo = new THREE.Mesh(
            new THREE.BoxGeometry(tam.x + 0.1, tam.y + 0.1, tam.z + 0.1),
            new THREE.MeshBasicMaterial({
                color: TEMA.exito, transparent: true, opacity: 0.28,
                depthTest: false, depthWrite: false,
            })
        );
        halo.position.copy(centro);
        halo.renderOrder = 18;
        _scene.add(halo);
        _halo = halo;
        _dirty = true;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Cámara
    // ═════════════════════════════════════════════════════════════════════
    function _volarA(pos, target, dur = 700) {
        _flying?.resolve?.();

        if (!_mounted) {
            _camera.position.copy(pos);
            _controls.target.copy(target);
            _controls.update();
            _flying = null; _dirty = true;
            return Promise.resolve();
        }

        _flying = {
            p0: _camera.position.clone(), t0: _controls.target.clone(),
            p1: pos.clone(), t1: target.clone(),
            inicio: performance.now(), dur,
        };
        _dirty = true;
        return new Promise(res => { _flying.resolve = res; });
    }

    function _tickVuelo() {
        if (!_flying) return;
        const t = Math.min(1, (performance.now() - _flying.inicio) / _flying.dur);
        const e = 1 - Math.pow(1 - t, 3);
        _camera.position.lerpVectors(_flying.p0, _flying.p1, e);
        _controls.target.lerpVectors(_flying.t0, _flying.t1, e);
        _controls.update();
        _dirty = true;

        if (t >= 1) {
            const done = _flying.resolve;
            _flying = null;
            done?.();
        }
    }

    function zoomFit(animar = true) {
        if (!_camera) return Promise.resolve();

        const caja = _bbox.isEmpty()
            ? new THREE.Box3(new THREE.Vector3(-2, 0, -2), new THREE.Vector3(8, 3, 8))
            : _bbox.clone();

        const centro = caja.getCenter(new THREE.Vector3());
        const tam = caja.getSize(new THREE.Vector3());

        // El encuadre lo manda la diagonal en planta: las barras son mucho más
        // largas que anchas y ajustar solo por el lado mayor deja media escena
        // fuera al mirarla en diagonal.
        const radio = Math.hypot(tam.x, tam.z) / 2 || 4;

        const fov = THREE.MathUtils.degToRad(_camera.fov);
        const dist = (radio / Math.tan(fov / 2)) * 0.95 + tam.y;

        const target = new THREE.Vector3(centro.x, tam.y * 0.45, centro.z);
        const pos = new THREE.Vector3(
            centro.x - dist * 0.42,
            target.y + dist * 0.5,
            centro.z + dist * 0.72
        );

        if (animar) return _volarA(pos, target, 700);

        _camera.position.copy(pos);
        _controls.target.copy(target);
        _controls.update();
        _dirty = true;
        return Promise.resolve();
    }

    /** Acerca la cámara a una pieza concreta y la deja marcada. */
    function focusPieza(idPieza) {
        const e = _piezasIdx.get(idPieza);
        if (!e) return Promise.resolve(false);

        seleccionar(idPieza);

        const caja = new THREE.Box3().setFromObject(e.mesh);
        const centro = caja.getCenter(new THREE.Vector3());
        const largo = Math.max(caja.getSize(new THREE.Vector3()).x, 1);

        const dist = largo * 0.9 + 2.5;
        const pos = new THREE.Vector3(centro.x - dist * 0.25, centro.y + dist * 0.5, centro.z + dist);

        return _volarA(pos, centro, 800).then(() => true);
    }

    function setPreset(nombre) {
        const caja = _bbox.isEmpty()
            ? new THREE.Box3(new THREE.Vector3(-2, 0, -2), new THREE.Vector3(8, 3, 8))
            : _bbox.clone();

        const centro = caja.getCenter(new THREE.Vector3());
        const tam = caja.getSize(new THREE.Vector3());

        // Vista de operario: dentro del primer pasillo, a la altura de los ojos
        // y mirando a lo largo del rack. Es donde de verdad se elige la barra.
        if (nombre === 'pasillo') {
            const zPasillo = caja.min.z + M.profundidad + M.pasillo / 2;
            const pos = new THREE.Vector3(caja.min.x - 2.5, 1.7, zPasillo);
            const mira = new THREE.Vector3(caja.max.x, 1.4, zPasillo);
            return _volarA(pos, mira, 800);
        }

        if (nombre === 'cenital') {
            const radio = Math.hypot(tam.x, tam.z) / 2 || 4;
            const fov = THREE.MathUtils.degToRad(_camera.fov);
            const dist = (radio / Math.tan(fov / 2)) * 1.05;
            const target = new THREE.Vector3(centro.x, 0, centro.z);
            // Un pelo desviado del cenit exacto: justo encima, OrbitControls
            // pierde la referencia de giro y la cámara da un salto.
            return _volarA(new THREE.Vector3(centro.x + 0.01, dist, centro.z + 0.01), target, 700);
        }

        return zoomFit(true);
    }

    function setModo(modo) {
        _modo = modo === 'longitud' ? 'longitud' : 'tipo';
        _piezasIdx.forEach(({ mesh, pieza, producto }) => {
            mesh.material.color.copy(_colorPieza(producto, pieza));
        });
        _dirty = true;
    }

    function getModo() { return _modo; }

    /** Activa/desactiva el modo "clic sobre una barra para marcar un corte". */
    function setModoCorte(activo) {
        _modoCorte = !!activo;
        if (!_modoCorte && _marcaCorte) _marcaCorte.visible = false;
        _dirty = true;
    }

    /** Activa/desactiva pintar el código de barras sobre las piezas cercanas. */
    function setMostrarCodigos(activo) {
        _mostrarCodigos = !!activo;
        if (!_mostrarCodigos) _codigoSprites.forEach(sp => { sp.visible = false; });
        _dirty = true;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Ciclo de vida
    // ═════════════════════════════════════════════════════════════════════
    function _loop() {
        if (!_mounted) return;
        _tickVuelo();

        if (_halo) {
            const t = _clock.getElapsedTime();
            _halo.material.opacity = 0.18 + Math.sin(t * 3.2) * 0.14;
            _dirty = true;
        }

        _actualizarMuros();
        _actualizarCodigos();

        const movio = _controls.update();
        if (movio || _dirty) {
            _actualizarRotulos();
            _renderer.render(_scene, _camera);
            _dirty = false;
        }
    }

    function mount() {
        _mounted = true;
        _onResize();
        _dirty = true;
    }

    function unmount() {
        _mounted = false;
        _flying?.resolve?.();
        _flying = null;
    }

    function _limpiar(g) {
        if (!g) return;
        for (let i = g.children.length - 1; i >= 0; i--) {
            const o = g.children[i];
            o.traverse?.(n => {
                n.geometry?.dispose?.();
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
        _resizeObs?.disconnect();
        _renderer?.setAnimationLoop(null);
        [_grpMundo, _grpPiezas].forEach(_limpiar);
        if (_marcaCorte) {
            _marcaCorte.geometry.dispose();
            _marcaCorte.material.dispose();
            _marcaCorte = null;
        }
        _renderer?.dispose();
        _renderer = null;
    }

    function refrescarTema() {
        if (!_renderer) return;
        _leerTema();
        _scene.background = TEMA.fondo.clone();
        _scene.fog.color.copy(TEMA.fondo);
        if (_datos) build(_datos, { mantenerCamara: true });
    }

    // ═════════════════════════════════════════════════════════════════════
    //  API pública
    // ═════════════════════════════════════════════════════════════════════
    return {
        init, build, mount, unmount, dispose,
        zoomFit, setPreset, focusPieza, seleccionar,
        setModo, getModo, refrescarTema,
        get listo() { return !!_renderer; },
        get totalPiezas() { return _piezaMeshes.length; },
        piezaPorCodigo(codigo) {
            if (codigo == null) return null;
            let res = null;
            _piezasIdx.forEach(e => {
                if (!res && e.pieza.codigo != null &&
                    String(e.pieza.codigo).toUpperCase() === String(codigo).toUpperCase()) res = e;
            });
            return res;
        },
        piezaPorId(idPieza) { return _piezasIdx.get(idPieza) || null; },
        /** Extensión de la escena y planos de niebla (para depurar encuadres). */
        limitesEscena() {
            if (_bbox.isEmpty()) return null;
            const tam = _bbox.getSize(new THREE.Vector3());
            return {
                ancho: tam.x, alto: tam.y, fondo: tam.z,
                fogNear: _scene.fog?.near ?? 0,
                fogFar: _scene.fog?.far ?? 0,
                largoMaxRender: M.largoMaxRender,
            };
        },
        estadoCamara() {
            if (!_camera) return null;
            const p = _camera.position, t = _controls.target;
            return { pos: { x: p.x, y: p.y, z: p.z }, mira: { x: t.x, y: t.y, z: t.z } };
        },
        setModoCorte,
        setMostrarCodigos,
        set onPiezaClick(fn) { _onPiezaClick = fn; },
        set onPiezaHover(fn) { _onPiezaHover = fn; },
        set onPiezaCorte(fn) { _onPiezaCorte = fn; },
    };
})();

window.Barras3D = Barras3D;
document.dispatchEvent(new CustomEvent('barras3d:ready'));
