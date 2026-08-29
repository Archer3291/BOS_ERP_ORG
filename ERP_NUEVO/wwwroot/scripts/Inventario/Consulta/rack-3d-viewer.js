/**
 * rack-3d-viewer.js
 * ─────────────────────────────────────────────────────────────────
 * Visores 3D del modal Inspector:
 *
 *   Rack3D   → rack completo navegable. Cada posición (columna × nivel)
 *              lleva su etiqueta de `ulocation` en la viga frontal y
 *              cada tarima es un objeto seleccionable por separado.
 *
 *   Tarima3D → una sola tarima con una caja por producto, clicable.
 *
 * Comparte tema y cálculo de ocupación con `almacen-virtual-3d.js`
 * (importa su `lib`) para no duplicar esa lógica una cuarta vez.
 */

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import * as BGU from 'three/addons/utils/BufferGeometryUtils.js';
import { lib } from './almacen-virtual-3d.js';

const mergeGeoms = BGU.mergeGeometries || BGU.mergeBufferGeometries;

// ═══════════════════════════════════════════════════════════════════
//  MEDIDAS DEL RACK DEL MODAL
//  No son las del plano: aquí interesa que se lea bien, así que se usan
//  proporciones de estantería real independientes de la escala del plano.
// ═══════════════════════════════════════════════════════════════════
const R = {
    bayL: 1.55,   // ancho de módulo (una columna)
    prof: 1.15,   // profundidad del rack
    nivelH: 1.35,   // separación entre niveles
    baseH: 0.10,
    posteW: 0.085,
    vigaH: 0.11,
    vigaD: 0.05,
    deckH: 0.03,
    tarimaH: 0.13,
    cargaMax: 0.82,
    cargaMin: 0.28,
};

// Paleta para diferenciar productos dentro de una tarima
const PALETA_PROD = [
    '#38bdf8', '#f59e0b', '#a78bfa', '#34d399',
    '#f472b6', '#fbbf24', '#60a5fa', '#4ade80',
];

// ═══════════════════════════════════════════════════════════════════
//  VISOR GENÉRICO — escena, cámara, luces, picking y bucle
// ═══════════════════════════════════════════════════════════════════
function crearVisor(container, opts = {}) {
    const T = lib.tema();

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.1;
    renderer.setSize(_w(), _h(), false);
    renderer.domElement.classList.add('av3dv-canvas');
    container.appendChild(renderer.domElement);

    const scene = new THREE.Scene();

    const camera = new THREE.PerspectiveCamera(42, _w() / _h(), 0.05, 400);
    camera.position.set(4, 3, 6);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.minDistance = 1.2;
    controls.maxDistance = 60;
    controls.maxPolarAngle = Math.PI * 0.495;
    controls.mouseButtons = {
        LEFT: THREE.MOUSE.ROTATE,
        MIDDLE: THREE.MOUSE.DOLLY,
        RIGHT: THREE.MOUSE.PAN,
    };

    // Luz: clave + relleno + cenital, sin shadow maps (escena pequeña,
    // el "apoyo" visual lo da la sombra de contacto falsa del suelo).
    scene.add(new THREE.HemisphereLight(
        T.oscuro ? 0xa8c6e4 : 0xffffff,
        T.oscuro ? 0x243049 : 0x9aa4b2,
        T.oscuro ? 2.0 : 1.7));
    scene.add(new THREE.AmbientLight(0xffffff, T.oscuro ? 0.5 : 0.45));

    const key = new THREE.DirectionalLight(0xfff4e6, 1.9);
    key.position.set(5, 8, 6);
    scene.add(key);

    const fill = new THREE.DirectionalLight(0xdbeafe, 0.75);
    fill.position.set(-6, 4, -4);
    scene.add(fill);

    const raycaster = new THREE.Raycaster();
    const ndc = new THREE.Vector2();

    let contenido = null;
    // Capas de picking en orden de prioridad: la caja de un nivel envuelve a
    // sus tarimas, así que hay que probar primero las tarimas o nunca ganarían.
    let capas = [];
    let hover = null;
    let pulsos = [];   // animaciones registradas por el contenido actual
    let montado = false;
    let sucio = true;
    let radio = 3;
    let esquinas = [];              // 8 vértices de la caja del contenido
    let centroContenido = null;

    const tooltip = opts.tooltipId ? document.getElementById(opts.tooltipId) : null;

    function _w() { return Math.max(1, container.clientWidth || 1); }
    function _h() { return Math.max(1, container.clientHeight || 1); }

    // ── Suelo con sombra de contacto ─────────────────────────────
    function _suelo(r) {
        const cv = document.createElement('canvas');
        cv.width = cv.height = 128;
        const ctx = cv.getContext('2d');
        const g = ctx.createRadialGradient(64, 64, 4, 64, 64, 62);
        g.addColorStop(0, 'rgba(0,0,0,0.42)');
        g.addColorStop(0.55, 'rgba(0,0,0,0.16)');
        g.addColorStop(1, 'rgba(0,0,0,0)');
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, 128, 128);

        const tex = new THREE.CanvasTexture(cv);
        const m = new THREE.Mesh(
            new THREE.PlaneGeometry(r * 3.2, r * 3.2),
            new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false })
        );
        m.rotation.x = -Math.PI / 2;
        m.position.y = 0.002;
        m.renderOrder = -1;
        return m;
    }

    // ── Contenido ────────────────────────────────────────────────
    function setContenido(grupo, picables) {
        if (contenido) {
            _liberar(contenido);
            scene.remove(contenido);
        }
        contenido = grupo;
        const lista = picables || [];
        capas = Array.isArray(lista[0]) ? lista : [lista];
        hover = null;
        pulsos = [];
        scene.add(grupo);

        const box = new THREE.Box3().setFromObject(grupo);
        const tam = box.getSize(new THREE.Vector3());
        const centro = box.getCenter(new THREE.Vector3());
        radio = Math.max(tam.x, tam.y, tam.z) / 2 || 1;

        // Esquinas del contenido: el encuadre las proyecta para medir
        // exactamente cuánto ocupa en pantalla.
        esquinas = [];
        for (let i = 0; i < 8; i++) {
            esquinas.push(new THREE.Vector3(
                i & 1 ? box.max.x : box.min.x,
                i & 2 ? box.max.y : box.min.y,
                i & 4 ? box.max.z : box.min.z));
        }
        centroContenido = centro.clone();

        grupo.add(_suelo(radio));

        encuadrar(centro, false);
        // El contenedor puede estar aún sin medidas definitivas (el modal
        // acaba de abrirse): se reencuadra cuando el layout ya asentó.
        requestAnimationFrame(() => {
            if (contenido === grupo) encuadrar(centro, false);
        });
        sucio = true;
    }

    const DIR_VISTA = new THREE.Vector3(0.62, 0.46, 1).normalize();
    const MARGEN_NDC = 0.94;   // 1.0 = pegado al borde del contenedor

    /**
     * Encuadre exacto: coloca la cámara, proyecta las 8 esquinas del contenido
     * y corrige la distancia hasta que llenan el viewport. Converge en 2-3
     * vueltas y, a diferencia de ajustar por radio, aprovecha el aspecto real
     * del contenedor (un rack ancho no tiene por qué encogerse por su altura).
     */
    function encuadrar(centro, animar = true) {
        const c = centro || centroContenido || new THREE.Vector3();
        if (!esquinas.length) return;

        const prueba = camera.clone();
        prueba.aspect = _w() / _h();

        let dist = radio * 3;
        for (let i = 0; i < 4; i++) {
            prueba.position.copy(c).addScaledVector(DIR_VISTA, dist);
            prueba.lookAt(c);
            prueba.updateProjectionMatrix();
            prueba.updateMatrixWorld(true);

            let maxNdc = 0;
            for (const esq of esquinas) {
                const p = esq.clone().project(prueba);
                maxNdc = Math.max(maxNdc, Math.abs(p.x), Math.abs(p.y));
            }
            if (maxNdc < 1e-4) break;

            const factor = maxNdc / MARGEN_NDC;
            dist *= factor;
            if (Math.abs(factor - 1) < 0.005) break;
        }

        const destino = c.clone().addScaledVector(DIR_VISTA, dist);

        if (!animar) {
            camera.position.copy(destino);
            controls.target.copy(c);
            controls.update();
        } else {
            _volar(destino, c);
        }
        sucio = true;
    }

    /** Acerca la cámara a un punto concreto del contenido ya cargado. */
    function encuadrarPunto(punto, distancia) {
        if (!contenido) return;
        const pos = punto.clone().addScaledVector(DIR_VISTA, distancia || radio * 1.2);
        _volar(pos, punto.clone(), 850);
    }

    let vuelo = null;
    function _volar(pos, target, dur = 550) {
        vuelo = {
            p0: camera.position.clone(), t0: controls.target.clone(),
            p1: pos.clone(), t1: target.clone(), ini: performance.now(), dur,
        };
        sucio = true;
    }

    function _tickVuelo() {
        if (!vuelo) return;
        const t = Math.min(1, (performance.now() - vuelo.ini) / vuelo.dur);
        const e = 1 - Math.pow(1 - t, 3);
        camera.position.lerpVectors(vuelo.p0, vuelo.p1, e);
        controls.target.lerpVectors(vuelo.t0, vuelo.t1, e);
        controls.update();
        sucio = true;
        if (t >= 1) vuelo = null;
    }

    // ── Picking ──────────────────────────────────────────────────
    function _ndc(ev) {
        const r = renderer.domElement.getBoundingClientRect();
        ndc.x = ((ev.clientX - r.left) / r.width) * 2 - 1;
        ndc.y = -((ev.clientY - r.top) / r.height) * 2 + 1;
    }

    function _picar() {
        raycaster.setFromCamera(ndc, camera);
        for (const capa of capas) {
            if (!capa.length) continue;
            const hits = raycaster.intersectObjects(capa, true);
            if (!hits.length) continue;
            // El objeto útil es el que lleva userData.pick (puede ser un ancestro)
            let o = hits[0].object;
            while (o && !o.userData?.pick) o = o.parent;
            if (o) return o;
        }
        return null;
    }

    function _aplicarHover(obj) {
        if (hover === obj) return;
        if (hover) _resaltar(hover, false);
        hover = obj;
        if (hover) _resaltar(hover, true);
        renderer.domElement.style.cursor = hover ? 'pointer' : 'grab';
        sucio = true;
    }

    /**
     * Resalta lo apuntado. Los volúmenes invisibles (la caja de un nivel) se
     * revelan con opacidad; los sólidos se iluminan con emisivo.
     */
    function _resaltar(obj, on) {
        if (obj.userData.opacidadHover !== undefined && obj.material) {
            obj.material.opacity = on ? obj.userData.opacidadHover : 0;
            return;
        }
        obj.traverse(n => {
            if (!n.isMesh || !n.material?.emissive) return;
            if (on) {
                n.userData._emis = n.material.emissive.getHex();
                n.material.emissive.set(lib.tema().accent);
                n.material.emissiveIntensity = 0.45;
            } else if (n.userData._emis !== undefined) {
                n.material.emissive.setHex(n.userData._emis);
                n.material.emissiveIntensity = 1;
            }
        });
    }

    function _tooltip(obj, ev) {
        if (!tooltip) return;
        if (!obj || !obj.userData.tooltip) { tooltip.classList.remove('show'); return; }

        tooltip.innerHTML = obj.userData.tooltip;
        tooltip.classList.add('show');

        const r = container.getBoundingClientRect();
        let x = ev.clientX - r.left + 14;
        let y = ev.clientY - r.top + 14;
        if (x + tooltip.offsetWidth > r.width - 6) x = ev.clientX - r.left - tooltip.offsetWidth - 14;
        if (y + tooltip.offsetHeight > r.height - 6) y = r.height - tooltip.offsetHeight - 6;
        tooltip.style.left = Math.max(6, x) + 'px';
        tooltip.style.top = Math.max(6, y) + 'px';
    }

    let abajo = null;
    const dom = renderer.domElement;

    dom.addEventListener('pointerdown', e => { abajo = { x: e.clientX, y: e.clientY }; });
    dom.addEventListener('pointermove', e => {
        _ndc(e);
        const o = _picar();
        _aplicarHover(o);
        _tooltip(o, e);
    });
    dom.addEventListener('pointerleave', () => { _aplicarHover(null); _tooltip(null); });
    dom.addEventListener('pointerup', e => {
        if (!abajo) return;
        const mov = Math.hypot(e.clientX - abajo.x, e.clientY - abajo.y);
        abajo = null;
        if (mov > 5 || e.button !== 0) return;
        _ndc(e);
        const o = _picar();
        if (o && typeof opts.onPick === 'function') opts.onPick(o.userData.pick, o);
    });
    dom.addEventListener('contextmenu', e => e.preventDefault());
    controls.addEventListener('change', () => { sucio = true; });

    // ── Bucle ────────────────────────────────────────────────────
    function _loop() {
        if (!montado) return;
        _tickVuelo();

        if (pulsos.length) {
            const t = performance.now() * 0.001;
            pulsos.forEach(fn => fn(t));
            sucio = true;
        }

        const movio = controls.update();
        if (movio || sucio) {
            renderer.render(scene, camera);
            sucio = false;
        }
    }

    const ro = new ResizeObserver(() => {
        const w = _w(), h = _h();
        if (w < 2 || h < 2) return;
        renderer.setSize(w, h, false);
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
        sucio = true;
    });
    ro.observe(container);

    function _liberar(obj) {
        obj.traverse(n => {
            if (n.geometry) n.geometry.dispose();
            if (n.material) {
                const ms = Array.isArray(n.material) ? n.material : [n.material];
                ms.forEach(m => { m.map?.dispose?.(); m.dispose(); });
            }
        });
    }

    return {
        scene, camera, controls,
        setContenido,
        encuadrar,
        encuadrarPunto,
        /** Registra una animación por frame; se limpia al cambiar de contenido. */
        registrarPulso: (fn) => { pulsos.push(fn); sucio = true; },
        volarA: (pos, target) => _volar(pos, target),
        invalidar: () => { sucio = true; },
        montar() {
            if (montado) return;
            montado = true;
            const w = _w(), h = _h();
            if (w > 1 && h > 1) {
                renderer.setSize(w, h, false);
                camera.aspect = w / h;
                camera.updateProjectionMatrix();
            }
            sucio = true;
            renderer.setAnimationLoop(_loop);
        },
        desmontar() {
            montado = false;
            renderer.setAnimationLoop(null);
            _tooltip(null);
        },
        destruir() {
            this.desmontar();
            ro.disconnect();
            if (contenido) _liberar(contenido);
            renderer.dispose();
            renderer.domElement.remove();
        },
    };
}

/**
 * Marcador de "esto es lo que buscabas": escuadras en las esquinas + tinte,
 * latiendo. Devuelve el grupo y la función de animación para registrarla.
 */
function _marcadorDestacado(w, h, d, visor) {
    const T = lib.tema();
    const g = new THREE.Group();

    // Con test de profundidad: dentro del modal nada tapa al objetivo
    const escuadras = lib.escuadras(w * 1.14, h * 1.14, d * 1.14, T.accent, 0.28, false);
    g.add(escuadras);

    const tinte = new THREE.Mesh(
        new THREE.BoxGeometry(w * 1.05, h * 1.05, d * 1.05),
        new THREE.MeshBasicMaterial({
            color: T.accent, transparent: true, opacity: 0.14, depthWrite: false,
        })
    );
    tinte.renderOrder = 26;
    g.add(tinte);

    visor.registrarPulso(t => {
        const p = 0.5 + Math.sin(t * 3.6) * 0.5;
        escuadras.material.opacity = 0.6 + p * 0.4;
        tinte.material.opacity = 0.12 + p * 0.16;
        const s = 1 + p * 0.045;
        escuadras.scale.set(s, s, s);
    });

    return g;
}

// ═══════════════════════════════════════════════════════════════════
//  ETIQUETAS PLANAS (se "imprimen" sobre la viga, no son billboards)
// ═══════════════════════════════════════════════════════════════════
function _placaTexto(texto, { ancho = 0.62, alto = 0.16, color, fondo } = {}) {
    const T = lib.tema();
    const PX = 256;
    const cv = document.createElement('canvas');
    cv.width = PX;
    cv.height = Math.round(PX * (alto / ancho));
    const ctx = cv.getContext('2d');

    ctx.fillStyle = fondo || (T.oscuro ? '#0d1728' : '#ffffff');
    ctx.fillRect(0, 0, cv.width, cv.height);

    ctx.strokeStyle = color || '#' + T.accent.getHexString();
    ctx.lineWidth = 6;
    ctx.strokeRect(3, 3, cv.width - 6, cv.height - 6);

    // El texto se ajusta al ancho disponible
    let fs = Math.round(cv.height * 0.58);
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    do {
        ctx.font = `700 ${fs}px "Segoe UI", sans-serif`;
        if (ctx.measureText(texto).width <= cv.width - 22) break;
        fs -= 2;
    } while (fs > 8);

    ctx.fillStyle = color || '#' + T.accent.getHexString();
    ctx.fillText(texto, cv.width / 2, cv.height / 2 + 1);

    const tex = new THREE.CanvasTexture(cv);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;

    return new THREE.Mesh(
        new THREE.PlaneGeometry(ancho, alto),
        new THREE.MeshBasicMaterial({ map: tex, transparent: true, toneMapped: false })
    );
}

// ═══════════════════════════════════════════════════════════════════
//  RACK 3D
// ═══════════════════════════════════════════════════════════════════
const Rack3D = (() => {

    let _visor = null;
    let _cont = null;
    let _handlers = {};
    let _destacar = null;   // id de tarima a señalar dentro del rack
    let _timerDestacar = null;

    /**
     * @param {{destacarTarimaId?:number}} [opts]  al llegar desde una búsqueda,
     *        señala en qué hueco del rack está la tarima buscada.
     */
    function render(containerId, rack, handlers = {}, opts = {}) {
        _cont = document.getElementById(containerId);
        if (!_cont) return;
        _handlers = handlers;
        _destacar = opts.destacarTarimaId ?? null;

        if (!_visor) {
            _visor = crearVisor(_cont, {
                tooltipId: 'av-rack3d-tooltip',
                onPick: (pick) => {
                    if (!pick) return;
                    if (pick.tipo === 'tarima') _handlers.onTarima?.(pick.tarima, pick.nivel, pick.col);
                    else if (pick.tipo === 'nivel') _handlers.onNivel?.(pick.nivel, pick.col);
                },
            });
        }

        const { grupo, capas, destacada } = _construir(rack);
        _visor.setContenido(grupo, capas);
        _visor.montar();

        // Si hay una tarima señalada, primero se ve el rack completo y luego
        // la cámara se acerca a ella: así se entiende dónde está dentro del rack.
        if (destacada) {
            clearTimeout(_timerDestacar);
            // Distancia suficiente para seguir viendo los huecos vecinos: si se
            // pega demasiado se pierde justo lo que se quiere comunicar, el "dónde".
            _timerDestacar = setTimeout(() => _visor.encuadrarPunto(
                new THREE.Vector3(destacada.x, destacada.y, 0), 5.6), 550);
        }
    }

    function _construir(rack) {
        const T = lib.tema();
        const cols = rack.columnas || [];
        const nCols = Math.max(cols.length, 1);
        const nNiv = Math.max(1, ...cols.map(c => (c.niveles || []).length), 1);

        const L = nCols * R.bayL;
        const P = R.prof;
        const H = R.baseH + nNiv * R.nivelH;

        const grupo = new THREE.Group();
        const capaTarimas = [];
        const capaNiveles = [];
        let destacada = null;   // posición de la tarima señalada, si la hay

        const gAcero = [];
        const gViga = [];

        const zF = P / 2 - R.posteW / 2;   // plano frontal
        const zB = -zF;

        // Postes
        for (let i = 0; i <= nCols; i++) {
            const x = -L / 2 + i * R.bayL;
            [zF, zB].forEach(z => gAcero.push(lib.caja(R.posteW, H, R.posteW, x, H / 2, z)));
        }

        // Bastidores extremos: travesaños + diagonales
        [0, nCols].forEach(i => {
            const x = -L / 2 + i * R.bayL;
            const luz = P - R.posteW;
            for (let k = 0; k <= nNiv; k++) {
                const y = R.baseH + k * R.nivelH;
                if (y <= H) gAcero.push(lib.caja(R.posteW * 0.7, R.posteW * 0.7, luz, x, y, 0));
            }
            const dl = Math.hypot(luz, R.nivelH);
            const ang = Math.atan2(R.nivelH, luz);
            for (let k = 0; k < nNiv; k++) {
                const y = R.baseH + k * R.nivelH + R.nivelH / 2;
                const d = lib.caja(R.posteW * 0.45, R.posteW * 0.45, dl, 0, 0, 0);
                d.rotateX(ang * (k % 2 ? 1 : -1));
                d.translate(x, y, 0);
                gAcero.push(d);
            }
        });

        // Largueros base
        [zF, zB].forEach(z => gAcero.push(lib.caja(L, R.vigaH * 0.8, R.vigaD, 0, R.baseH / 2, z)));

        // Niveles
        cols.forEach((col, ci) => {
            const niveles = [...(col.niveles || [])]
                .sort((a, b) => (a.num_nivel || 0) - (b.num_nivel || 0));
            const xBay = -L / 2 + ci * R.bayL + R.bayL / 2;

            niveles.forEach((nivel, k) => {
                const y = R.baseH + k * R.nivelH;
                const yDeck = y + R.vigaH + R.deckH;

                [zF, zB].forEach(z =>
                    gViga.push(lib.caja(R.bayL - R.posteW, R.vigaH, R.vigaD, xBay, y + R.vigaH / 2, z)));

                gAcero.push(lib.caja(R.bayL - R.posteW - 0.02, R.deckH, P - R.posteW - 0.04,
                    xBay, y + R.vigaH + R.deckH / 2, 0));

                // ── Etiqueta de ULOCATION ────────────────────────
                // Va en el EXTREMO de la viga, no centrada: así no tapa el
                // hueco del nivel ni la tarima, que es justo donde se mira.
                const texto = nivel.ulocation
                    || `C${col.num_col ?? ci + 1}-N${nivel.num_nivel ?? k + 1}`;
                const pctN = lib.ocupNivel(nivel);
                const colEtq = '#' + lib.colorOcup(pctN).getHexString();

                const anchoEtq = Math.min(R.bayL * 0.42, 0.7);
                const dx = R.bayL / 2 - anchoEtq / 2 - R.posteW;
                [
                    [xBay - dx, zF + R.vigaD / 2 + 0.004, 0],          // frente, a la izquierda
                    [xBay + dx, zB - R.vigaD / 2 - 0.004, Math.PI],    // fondo, espejado
                ].forEach(([x, z, rotY]) => {
                    const placa = _placaTexto(texto, {
                        ancho: anchoEtq, alto: anchoEtq * 0.3, color: colEtq,
                    });
                    placa.position.set(x, y + R.vigaH / 2, z);
                    placa.rotation.y = rotY;
                    grupo.add(placa);
                });

                // ── Zona clicable del nivel (hueco del módulo) ──
                const slot = new THREE.Mesh(
                    new THREE.BoxGeometry(R.bayL - R.posteW - 0.04, R.nivelH - R.vigaH - 0.06, P - R.posteW),
                    new THREE.MeshBasicMaterial({
                        color: T.accent, transparent: true, opacity: 0, depthWrite: false,
                    })
                );
                slot.position.set(xBay, yDeck + (R.nivelH - R.vigaH) / 2 - 0.03, 0);
                slot.userData = {
                    pick: { tipo: 'nivel', nivel, col },
                    tooltip: _ttNivel(nivel, col, pctN),
                    opacidadHover: 0.14,
                };
                grupo.add(slot);
                capaNiveles.push(slot);

                // ── Tarimas: cada una es su propio objeto seleccionable ──
                const tarimas = nivel.tarimas || [];
                if (!tarimas.length) return;

                const nSlots = Math.min(tarimas.length, 3);
                const tarW = Math.min(1.15, (R.bayL - 0.16) / nSlots);
                const tarD = Math.min(1.0, P - R.posteW - 0.12);

                tarimas.slice(0, nSlots).forEach((tarima, s) => {
                    const xt = xBay - (nSlots - 1) * tarW / 2 + s * tarW;
                    const obj = _tarimaEnRack(tarima, tarW, tarD, pctN);
                    obj.position.set(xt, yDeck, 0);
                    obj.userData = {
                        pick: { tipo: 'tarima', tarima, nivel, col },
                        tooltip: _ttTarima(tarima, nivel, col),
                    };
                    grupo.add(obj);
                    capaTarimas.push(obj);

                    // La tarima que motivó la búsqueda se señala entre todas
                    if (_destacar != null && tarima.id === _destacar) {
                        const altoCarga = R.tarimaH + R.cargaMax * 0.8;
                        const marca = _marcadorDestacado(tarW, altoCarga, tarD, _visor);
                        marca.position.set(xt, yDeck + altoCarga / 2, 0);
                        grupo.add(marca);
                        destacada = { x: xt, y: yDeck + altoCarga / 2 };
                    }
                });
            });
        });

        // Mallas fusionadas de la estructura
        const matAcero = new THREE.MeshStandardMaterial({
            color: T.acero, roughness: 0.5, metalness: 0.7,
        });
        const matViga = new THREE.MeshStandardMaterial({
            color: T.viga, roughness: 0.45, metalness: 0.35,
        });

        [[gAcero, matAcero], [gViga, matViga]].forEach(([gs, mat]) => {
            if (!gs.length) return;
            const merged = mergeGeoms(gs, false);
            gs.forEach(x => x.dispose());
            if (merged) grupo.add(new THREE.Mesh(merged, mat));
        });

        // Rótulo de columna POR ENCIMA del rack: al pie quedaba tapado por las
        // tarimas del nivel inferior y por el larguero base.
        cols.forEach((col, ci) => {
            const x = -L / 2 + ci * R.bayL + R.bayL / 2;
            const ancho = Math.min(R.bayL * 0.5, 0.62);
            [[zF + 0.02, 0], [zB - 0.02, Math.PI]].forEach(([z, rotY]) => {
                const p = _placaTexto(`C${col.num_col ?? ci + 1}`, {
                    ancho, alto: ancho * 0.42, color: '#' + T.accent.getHexString(),
                });
                p.position.set(x, H + ancho * 0.32, z);
                p.rotation.y = rotY;
                grupo.add(p);
            });
        });

        // Al señalar una tarima se apagan las demás. Las escuadras solas se
        // pierden entre decenas de cajas del mismo color; el contraste es lo
        // que hace que se vea de un golpe cuál es.
        if (_destacar != null && destacada) {
            capaTarimas.forEach(obj => {
                if (obj.userData?.pick?.tarima?.id === _destacar) return;
                obj.traverse(n => {
                    // Se oscurece el color en vez de usar transparencia: así no
                    // aparecen artefactos de ordenación entre decenas de cajas.
                    if (n.isMesh && n.material?.color) n.material.color.multiplyScalar(0.4);
                });
            });
        }

        // Las tarimas se prueban antes que el hueco del nivel que las contiene
        return { grupo, capas: [capaTarimas, capaNiveles], destacada };
    }

    /** Tarima con su carga, como objeto independiente. */
    function _tarimaEnRack(tarima, w, d, pctNivel) {
        const T = lib.tema();
        const g = new THREE.Group();

        const matMadera = new THREE.MeshStandardMaterial({
            color: T.madera, roughness: 0.94, metalness: 0,
        });

        // Tablero + patines
        const piezas = [
            lib.caja(w * 0.94, 0.04, d * 0.96, 0, R.tarimaH - 0.02, 0),
        ];
        [-d * 0.34, 0, d * 0.34].forEach(dz =>
            piezas.push(lib.caja(w * 0.92, R.tarimaH - 0.05, d * 0.13, 0, (R.tarimaH - 0.05) / 2, dz)));

        const madera = mergeGeoms(piezas, false);
        piezas.forEach(p => p.dispose());
        if (madera) g.add(new THREE.Mesh(madera, matMadera));

        // Carga
        const prods = tarima.productos || [];
        const llenado = prods.length ? Math.min(1, 0.4 + prods.length * 0.2) : 0.35;
        const cargaH = R.cargaMin + (R.cargaMax - R.cargaMin) * llenado;

        const carga = new THREE.Mesh(
            new THREE.BoxGeometry(w * 0.86, cargaH, d * 0.88),
            new THREE.MeshStandardMaterial({
                color: lib.colorOcup(pctNivel || 0.2),
                roughness: 0.8, metalness: 0.04,
            })
        );
        carga.position.y = R.tarimaH + cargaH / 2;
        g.add(carga);

        // Fleje decorativo
        const fleje = new THREE.Mesh(
            new THREE.BoxGeometry(w * 0.88, 0.025, d * 0.9),
            new THREE.MeshStandardMaterial({ color: 0x1f2937, roughness: 0.6 })
        );
        fleje.position.y = R.tarimaH + cargaH * 0.62;
        g.add(fleje);

        return g;
    }

    function _ttNivel(nivel, col, pct) {
        const tarimas = nivel.tarimas || [];
        const prods = tarimas.reduce((s, t) => s + (t.productos || []).length, 0);
        return `
            <div class="av3d-tt-title">
                <i class="fas fa-layer-group"></i> Nivel ${nivel.num_nivel ?? '—'}
                <span>${lib.esc(nivel.ulocation || '')}</span>
            </div>
            <div class="av3d-tt-row"><span>Columna</span><b>${col.num_col ?? '—'}</b></div>
            <div class="av3d-tt-row"><span>Tarimas</span><b>${tarimas.length}</b></div>
            <div class="av3d-tt-row"><span>Productos</span><b>${prods}</b></div>
            <div class="av3d-tt-row"><span>Ocupación</span>
                <b style="color:#${lib.colorOcup(pct).getHexString()}">${lib.etiquetaOcup(pct)}</b></div>
            <div class="av3d-tt-hint">Clic para ver el nivel</div>`;
    }

    function _ttTarima(tarima, nivel, col) {
        const prods = tarima.productos || [];
        return `
            <div class="av3d-tt-title av3d-tt-tarima">
                <i class="fas fa-pallet"></i> ${lib.esc(tarima.codigo || 'Tarima ' + tarima.id)}
                <span>${lib.esc(nivel.ulocation || '')}</span>
            </div>
            <div class="av3d-tt-row"><span>Col · Nivel</span><b>${col.num_col ?? '—'} · ${nivel.num_nivel ?? '—'}</b></div>
            <div class="av3d-tt-row"><span>Productos</span><b>${prods.length}</b></div>
            <div class="av3d-tt-hint">Clic para abrir la tarima en 3D</div>`;
    }

    function montar() { _visor?.montar(); }
    function desmontar() { _visor?.desmontar(); }
    function encuadrar() { _visor?.encuadrar(); }

    return { render, montar, desmontar, encuadrar };
})();

// ═══════════════════════════════════════════════════════════════════
//  TARIMA 3D — una caja por producto
// ═══════════════════════════════════════════════════════════════════
const Tarima3D = (() => {

    const MODO_KEY = 'av3d.tarima.modo';

    /**
     * Formas de disponer los productos. Con muchas partidas el apilado
     * realista los amontona y dejan de distinguirse.
     *
     * Todas las disposiciones respetan la HUELLA de la tarima: lo que cambia
     * es cómo se reparte la carga en altura. Sacar las cajas fuera de la
     * tarima las separaba, sí, pero quedaban flotando y se perdía la lectura
     * de "esto es lo que lleva esta tarima".
     */
    const MODOS = [
        {
            id: 'apilado', nombre: 'Apilado', icono: 'fa-boxes-stacked',
            ayuda: 'Como iría la carga realmente sobre la tarima',
        },
        {
            id: 'explosion', nombre: 'Explosión', icono: 'fa-arrows-up-down',
            ayuda: 'Las mismas cajas, con las capas separadas para verlas todas',
        },
        {
            id: 'capas', nombre: 'Capas', icono: 'fa-layer-group',
            ayuda: 'Una capa por producto, con el grosor según su cantidad',
        },
        {
            id: 'barras', nombre: 'Barras', icono: 'fa-chart-simple',
            ayuda: 'Altura proporcional a la cantidad, para compararlas',
        },
    ];

    let _visor = null;
    let _handlers = {};
    let _tarima = null;
    let _modo = 'apilado';
    let _destacarProd = null;   // id del producto que motivó abrir la tarima

    function _modoGuardado() {
        try { return localStorage.getItem(MODO_KEY); } catch { return null; }
    }

    function _guardarModo(m) {
        try { localStorage.setItem(MODO_KEY, m); } catch { /* noop */ }
    }

    /**
     * @param {{destacarProductoId?:number}} [opts]  señala qué caja corresponde
     *        al producto buscado/escaneado dentro de la tarima.
     */
    function render(containerId, tarima, handlers = {}, opts = {}) {
        const cont = document.getElementById(containerId);
        if (!cont) return;
        _handlers = handlers;
        _tarima = tarima;
        _destacarProd = opts.destacarProductoId ?? null;

        // Sin preferencia guardada se elige según cuántas partidas hay: pocas
        // se leen bien apiladas; con muchas, 'capas' es la única que las deja
        // todas rotuladas y visibles sin salirse de la tarima.
        const guardado = _modoGuardado();
        _modo = MODOS.some(m => m.id === guardado)
            ? guardado
            : ((tarima.productos || []).length <= 4 ? 'apilado' : 'capas');

        if (!_visor) {
            _visor = crearVisor(cont, {
                tooltipId: 'av-tarima3d-tooltip',
                onPick: (pick) => {
                    if (pick?.tipo === 'producto') _handlers.onProducto?.(pick.producto);
                },
            });
        }

        _pintar();
    }

    /** Cambia la disposición y recuerda la elección del usuario. */
    function setModo(modo) {
        if (!MODOS.some(m => m.id === modo) || modo === _modo) return;
        _modo = modo;
        _guardarModo(modo);
        if (_tarima) _pintar();
    }

    function getModo() { return _modo; }

    function _pintar() {
        if (!_visor || !_tarima) return;
        const { grupo, capas } = _construir(_tarima, _modo);
        _visor.setContenido(grupo, capas);
        _visor.montar();
    }

    // ── Disposiciones ────────────────────────────────────────────
    const PAL = { W: 1.2, D: 1.0, patin: 0.1, tabla: 0.022 };

    /** Rejilla 2×2 sobre la tarima que usan 'apilado' y 'explosión'. */
    function _celdaApilado(n) {
        const porCapa = n <= 1 ? 1 : n <= 2 ? 2 : 4;
        const gx = porCapa === 1 ? 1 : 2;
        const gz = porCapa === 4 ? 2 : 1;
        return {
            porCapa, gx, gz,
            cw: (PAL.W - 0.06) / gx,
            cd: (PAL.D - 0.06) / gz,
        };
    }

    /**
     * Calcula dónde va cada producto según el modo. Ninguna disposición se
     * sale de la huella de la tarima.
     *
     * @returns {Array<{x,y,z,w,h,d,caraEtiqueta}>} `y` es el centro de la caja.
     */
    function _disponer(prods, modo, yBase) {
        const n = prods.length;
        const cants = prods.map(p => Math.max(0, parseFloat(p.cantidad) || 0));
        const maxCant = Math.max(...cants, 1);

        // ── Capas: cada producto es una plancha del ancho de la tarima ──
        // Es la que mejor escala: con 20 partidas siguen siendo 20 franjas
        // rotuladas en el canto, todas visibles y sin salirse de la tarima.
        if (modo === 'capas') {
            const w = PAL.W - 0.06;
            const d = PAL.D - 0.06;
            const tMin = 0.11, tMax = 0.30;

            let acum = 0;
            return prods.map((_, i) => {
                const t = tMin + (tMax - tMin) * (cants[i] / maxCant);
                const y = yBase + acum + t / 2;
                acum += t;
                return { x: 0, z: 0, y, w, h: t, d, caraEtiqueta: 'canto' };
            });
        }

        // ── Barras: rejilla sobre la tarima, altura = cantidad ──────────
        if (modo === 'barras') {
            const gx = Math.ceil(Math.sqrt(n * (PAL.W / PAL.D)));
            const gz = Math.ceil(n / gx);
            const cw = (PAL.W - 0.06) / gx;
            const cd = (PAL.D - 0.06) / gz;

            // Las más altas al fondo: si quedaran delante taparían al resto
            // y precisamente este modo existe para poder compararlas.
            const orden = prods.map((_, i) => i)
                .sort((a, b) => cants[b] - cants[a]);

            const puestos = new Array(n);
            orden.forEach((idx, celda) => {
                const ix = celda % gx, iz = Math.floor(celda / gx);
                puestos[idx] = {
                    x: -PAL.W / 2 + 0.03 + cw * (ix + 0.5),
                    z: -PAL.D / 2 + 0.03 + cd * (iz + 0.5),
                    y: yBase + (0.14 + 0.9 * (cants[idx] / maxCant)) / 2,
                    w: cw * 0.74,
                    h: 0.14 + 0.9 * (cants[idx] / maxCant),
                    d: cd * 0.74,
                    caraEtiqueta: 'tapa',
                };
            });
            return puestos;
        }

        // ── Apilado / Explosión: misma rejilla, distinta separación ─────
        const { porCapa, gx, cw, cd } = _celdaApilado(n);
        const hCaja = 0.32;
        const sep = modo === 'explosion' ? hCaja + 0.30 : hCaja + 0.04;

        return prods.map((_, i) => {
            const capa = Math.floor(i / porCapa);
            const k = i % porCapa;
            const ix = k % gx, iz = Math.floor(k / gx);
            return {
                x: -PAL.W / 2 + 0.03 + cw * (ix + 0.5),
                z: -PAL.D / 2 + 0.03 + cd * (iz + 0.5),
                y: yBase + capa * sep + hCaja / 2,
                w: cw * 0.9, h: hCaja, d: cd * 0.9,
                caraEtiqueta: 'tapa',
            };
        });
    }

    /**
     * Guías verticales que enhebran las capas separadas de la explosión,
     * para que se siga leyendo que pertenecen a la misma pila.
     */
    function _guiasExplosion(puestos, yBase) {
        if (!puestos.length) return null;

        // Una guía por posición de la rejilla, no por caja
        const porPosicion = new Map();
        puestos.forEach(p => {
            const clave = `${p.x.toFixed(3)}|${p.z.toFixed(3)}`;
            const alto = p.y + p.h / 2;
            porPosicion.set(clave, {
                x: p.x, z: p.z,
                alto: Math.max(porPosicion.get(clave)?.alto || 0, alto),
            });
        });

        const piezas = [];
        porPosicion.forEach(({ x, z, alto }) => {
            const h = Math.max(0.01, alto - yBase);
            piezas.push(lib.caja(0.014, h, 0.014, x, yBase + h / 2, z));
        });

        const geo = mergeGeoms(piezas, false);
        piezas.forEach(p => p.dispose());
        if (!geo) return null;

        return new THREE.Mesh(geo, new THREE.MeshBasicMaterial({
            color: lib.tema().accent, transparent: true, opacity: 0.3,
            depthWrite: false,
        }));
    }

    function _construir(tarima, modo) {
        const T = lib.tema();
        const grupo = new THREE.Group();
        const cajas = [];

        const W = PAL.W, D = PAL.D, altoPatin = PAL.patin;

        // ── Tarima de madera con listones ────────────────────────
        const matMadera = new THREE.MeshStandardMaterial({
            color: T.madera, roughness: 0.92, metalness: 0,
        });
        const piezas = [];

        // Tablas superiores
        const nTablas = 6;
        for (let i = 0; i < nTablas; i++) {
            const z = -D / 2 + D / nTablas * (i + 0.5);
            piezas.push(lib.caja(W, 0.022, D / nTablas * 0.78, 0, altoPatin + 0.011, z));
        }
        // Tacos
        [-W / 2 + 0.09, 0, W / 2 - 0.09].forEach(x =>
            [-D / 2 + 0.09, 0, D / 2 - 0.09].forEach(z =>
                piezas.push(lib.caja(0.14, altoPatin, 0.14, x, altoPatin / 2, z))));
        // Tablas inferiores
        [-D / 2 + 0.09, 0, D / 2 - 0.09].forEach(z =>
            piezas.push(lib.caja(W, 0.02, 0.16, 0, 0.01, z)));

        const madera = mergeGeoms(piezas, false);
        piezas.forEach(p => p.dispose());
        if (madera) grupo.add(new THREE.Mesh(madera, matMadera));

        // ── Una caja por producto ────────────────────────────────
        const prods = tarima.productos || [];
        const yBase = altoPatin + 0.022;

        if (!prods.length) {
            const vacio = _placaTexto('TARIMA VACÍA', { ancho: 0.8, alto: 0.2 });
            vacio.position.set(0, yBase + 0.3, 0);
            grupo.add(vacio);
            return { grupo, capas: [cajas] };
        }

        const puestos = _disponer(prods, modo, yBase);

        if (modo === 'explosion') {
            const guias = _guiasExplosion(puestos, yBase);
            if (guias) grupo.add(guias);
        }

        prods.forEach((p, i) => {
            const pos = puestos[i];
            const color = new THREE.Color(PALETA_PROD[i % PALETA_PROD.length]);

            const g = new THREE.Group();
            g.position.set(pos.x, 0, pos.z);

            const caja = new THREE.Mesh(
                new THREE.BoxGeometry(pos.w, pos.h, pos.d),
                new THREE.MeshStandardMaterial({ color, roughness: 0.72, metalness: 0.05 })
            );
            caja.position.y = pos.y;
            g.add(caja);

            g.userData = {
                pick: { tipo: 'producto', producto: p },
                tooltip: _ttProducto(p, color),
            };
            grupo.add(g);
            cajas.push(g);

            // Las etiquetas cuelgan del grupo raíz, no de la caja, para que
            // no hereden transformaciones y se lean igual en todos los modos.
            _etiquetasProducto(grupo, p, pos, color);

            // Caja del producto que motivó abrir la tarima
            if (_destacarProd != null && p.id === _destacarProd) {
                const marca = _marcadorDestacado(pos.w, pos.h, pos.d, _visor);
                marca.position.set(pos.x, pos.y, pos.z);
                grupo.add(marca);
            }
        });

        return { grupo, capas: [cajas] };
    }

    /** Rótulo del producto, en la tapa o en los cantos según la disposición. */
    function _etiquetasProducto(grupo, p, pos, color) {
        const texto = p.cve_prod || '—';
        const hex = '#' + color.getHexString();

        if (pos.caraEtiqueta === 'canto') {
            // En 'capas' la tapa queda tapada por la plancha de arriba, así que
            // el rótulo va en los dos cantos visibles desde la cámara inicial.
            const ancho = Math.min(pos.w * 0.5, 0.6);
            const alto = Math.min(pos.h * 0.62, ancho * 0.3);

            const frente = _placaTexto(texto, { ancho, alto, color: hex });
            frente.position.set(pos.x, pos.y, pos.z + pos.d / 2 + 0.004);
            grupo.add(frente);

            const lado = _placaTexto(texto, { ancho, alto, color: hex });
            lado.rotation.y = Math.PI / 2;
            lado.position.set(pos.x + pos.w / 2 + 0.004, pos.y, pos.z);
            grupo.add(lado);
            return;
        }

        const ancho = Math.min(pos.w * 0.86, 0.62);
        const etq = _placaTexto(texto, { ancho, alto: ancho * 0.34, color: hex });
        etq.rotation.x = -Math.PI / 2;
        etq.position.set(pos.x, pos.y + pos.h / 2 + 0.004, pos.z);
        grupo.add(etq);
    }

    function _ttProducto(p, color) {
        return `
            <div class="av3d-tt-title" style="color:#${color.getHexString()}">
                <i class="fas fa-box"></i> ${lib.esc(p.cve_prod || '—')}
            </div>
            <div class="av3d-tt-desc">${lib.esc(p.descr_prod || '')}</div>
            <div class="av3d-tt-row"><span>Cantidad</span><b>${lib.fmt(p.cantidad)} ${lib.esc(p.udm || '')}</b></div>
            ${p.lin_prod ? `<div class="av3d-tt-row"><span>Línea</span><b>${lib.esc(p.lin_prod)}</b></div>` : ''}
            <div class="av3d-tt-hint">Clic para ver la ficha del producto</div>`;
    }

    function montar() { _visor?.montar(); }
    function desmontar() { _visor?.desmontar(); }
    function encuadrar() { _visor?.encuadrar(); }

    // Se expone la paleta para que la lista de productos del modal
    // use exactamente los mismos colores que las cajas 3D.
    return {
        render, montar, desmontar, encuadrar,
        setModo, getModo, MODOS,
        paleta: PALETA_PROD,
    };
})();

window.Rack3D = Rack3D;
window.Tarima3D = Tarima3D;
document.dispatchEvent(new CustomEvent('av3dviewer:ready'));

export { Rack3D, Tarima3D };
