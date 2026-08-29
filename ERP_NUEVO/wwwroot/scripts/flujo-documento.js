document.addEventListener('DOMContentLoaded', async () => {
    const procesos = {};
    let TPDOC;
    let FLUJO;

    const AREA_CONFIG = {
        1: { color: "#1e3a5f", textColor: "#1e3a5f", bg: "#e1e7ef" },
        2: { color: "#d97706", textColor: "#d97706", bg: "#fef3c7" },
        3: { color: "#059669", textColor: "#059669", bg: "#d1fae5" },
    };

    const data = await GetData({
        path: '/Requisicion/GetFlujoDocumento',
        data: {
            controller: _CURRENT_CONTORLLER,
            action: _CURRENT_ACTION,
        }
    });
    TPDOC = data.tpdoc;
    FLUJO = data.flujo;

    FLUJO.forEach(f => {
        if (!procesos[f.id_proceso]) {
            procesos[f.id_proceso] = { nombre: f.nombre, flujos: [] };
        }
        procesos[f.id_proceso].flujos.push(f);
    });

    let activeId = null;
    const container = document.getElementById("flujo-container");

    // ── Render de todos los procesos ────────────────────────────────────────
    function renderProcesos() {
        container.innerHTML = "";

        Object.values(procesos).forEach(proceso => {
            const chain = buildChain(proceso);

            const wrapper = document.createElement("div");
            wrapper.className = "proceso-wrapper";

            const titulo = document.createElement("div");
            titulo.className = "proceso-titulo";
            titulo.innerHTML = `<i class="bi bi-diagram-2"></i>${proceso.nombre}`;
            wrapper.appendChild(titulo);

            const scroll = document.createElement("div");
            scroll.className = "flujo-scroll";

            const row = document.createElement("div");
            row.className = "flujo-inner";
            scroll.appendChild(row);
            wrapper.appendChild(scroll);

            chain.forEach((doc, idx) => {
                const flujo = proceso.flujos.find(f => f.idtpdoc_origen === doc.idtpdoc);
                const cfg = AREA_CONFIG[doc.idarea] || AREA_CONFIG[1];
                const isActive = activeId === doc.idtpdoc;
                const isRelated = activeId && FLUJO.some(f =>
                    (f.idtpdoc_origen === activeId && f.idtpdoc_destino === doc.idtpdoc) ||
                    (f.idtpdoc_destino === activeId && f.idtpdoc_origen === doc.idtpdoc)
                );

                // ── Tarjeta ──────────────────────────────────────────────
                const card = document.createElement("div");
                card.className = `card doc-card area-${doc.idarea}${isActive ? " active" : ""}${isRelated ? " related" : ""}`;

                card.innerHTML = `
                    <div class="card-body">
                        <span class="area-badge" style="color:${cfg.textColor};">
                            ${doc.abreviaturatpdoc}
                        </span>
                        <p class="doc-title">${doc.tpdoc}</p>
                        <p class="doc-area mb-0">
                            <i class="bi bi-building" style="font-size:0.68rem;"></i>
                            ${doc.area}
                        </p>
                    </div>
                    ${isActive
                        ? `<div class="selected-pill" style="background:${cfg.color};">SELECCIONADO</div>`
                        : ""}
                `;

                card.addEventListener("click", () => {
                    activeId = isActive ? null : doc.idtpdoc;
                    renderProcesos();
                    renderDetail();
                });

                row.appendChild(card);

                // ── Flecha ───────────────────────────────────────────────
                if (flujo) {
                    const arrow = document.createElement("div");
                    arrow.className = "arrow-wrap";

                    // ← AÑADE ESTO: delay escalonado por posición
                    const totalArrows = chain.length - 1;
                    const segmentDuration = 2.4; // segundos — duración total del ciclo
                    const delay = (idx / totalArrows) * segmentDuration;

                    arrow.innerHTML = `
                        <div class="arrow-line${flujo.obligatorio ? "" : " optional"}"
                             style="--arrow-delay: ${delay}s; --arrow-offset: ${(idx / totalArrows) * 100}%;">
                            <div class="line"></div>
                            <i class="bi bi-caret-right-fill caret"></i>
                        </div>
                    `;
                    row.appendChild(arrow);
                }
            });

            container.appendChild(wrapper);
        });
    }

    // ── Build chain ──────────────────────────────────────────────────────────
    function buildChain(proceso) {
        const flujos = proceso.flujos;
        const ids = new Set();
        flujos.forEach(f => { ids.add(f.idtpdoc_origen); ids.add(f.idtpdoc_destino); });

        const docs = TPDOC.filter(d => ids.has(d.idtpdoc));
        const destinos = new Set(flujos.map(f => f.idtpdoc_destino));
        let current = docs.find(d => !destinos.has(d.idtpdoc));

        const chain = [];
        while (current) {
            chain.push(current);
            const nextFlujo = flujos.find(f => f.idtpdoc_origen === current.idtpdoc);
            if (!nextFlujo) break;
            current = docs.find(d => d.idtpdoc === nextFlujo.idtpdoc_destino);
            if (!current || chain.some(c => c.idtpdoc === current.idtpdoc)) break;
        }
        return chain;
    }

    // ── Panel de detalle ─────────────────────────────────────────────────────
    function renderDetail() {
        const panel = document.getElementById("detail-panel");
        if (!activeId) { panel.classList.remove("show"); return; }

        const doc = TPDOC.find(d => d.idtpdoc === activeId);
        const cfg = AREA_CONFIG[doc.idarea] || AREA_CONFIG[1];
        const flujoIn = FLUJO.find(f => f.idtpdoc_destino === activeId);
        const flujoOut = FLUJO.find(f => f.idtpdoc_origen === activeId);
        const docOrigen = flujoIn ? TPDOC.find(d => d.idtpdoc === flujoIn.idtpdoc_origen) : null;
        const docDestino = flujoOut ? TPDOC.find(d => d.idtpdoc === flujoOut.idtpdoc_destino) : null;

        // Header con color del área
        const header = document.getElementById("detail-header");
        header.style.background = cfg.color;
        header.innerHTML = `
            <i class="bi bi-file-earmark-text me-2"></i>${doc.tpdoc}
            <span class="badge bg-white text-dark ms-2 fw-normal" style="font-size:0.7rem;opacity:0.9;">${doc.abreviaturatpdoc}</span>
        `;

        // Info general
        document.getElementById("detail-info").innerHTML = `
            <div class="detail-section-label"><i class="bi bi-info-circle"></i> Descripción</div>
            <div class="detail-block">
                <p class="mb-2" style="color:var(--text-dark);font-size:0.83rem;">${doc.descr || "Sin descripción registrada."}</p>
                <span class="area-badge" style="color:${cfg.textColor};border-color:${cfg.textColor};opacity:0.8;font-size:0.65rem;padding:2px 7px;border-radius:5px;border:1px solid;font-family:'DM Mono',monospace;letter-spacing:0.06em;">
                    ${doc.area}
                </span>
            </div>
        `;

        // Origen
        document.getElementById("detail-origen").innerHTML = `
            <div class="detail-section-label"><i class="bi bi-arrow-left-circle"></i> Generado por</div>
            <div class="detail-block">
                ${docOrigen
                ? `<p class="doc-name">${docOrigen.tpdoc}</p>
                       <p class="doc-rel mb-0">${flujoIn.descripcion}</p>`
                : `<p class="doc-rel mb-0" style="display:flex;align-items:center;gap:6px;">
                           <i class="bi bi-flag-fill text-success"></i> Documento inicial del flujo
                       </p>`
            }
            </div>
        `;

        // Destino
        document.getElementById("detail-destino").innerHTML = `
            <div class="detail-section-label"><i class="bi bi-arrow-right-circle"></i> Genera</div>
            <div class="detail-block" style="${docDestino ? `border-left:3px solid ${cfg.color};` : ""}">
                ${docDestino
                ? `<p class="doc-name">${docDestino.tpdoc}</p>
                       <p class="doc-rel mb-0">${flujoOut.descripcion}</p>
                       ${!flujoOut.obligatorio
                    ? `<span class="badge mt-2" style="background:${cfg.bg};color:${cfg.textColor};font-size:0.65rem;font-family:'DM Mono',monospace;">OPCIONAL</span>`
                    : ""}`
                : `<p class="doc-rel mb-0" style="display:flex;align-items:center;gap:6px;">
                           <i class="bi bi-check-circle-fill text-primary"></i> Documento final del flujo
                       </p>`
            }
            </div>
        `;

        panel.classList.add("show");
    }

    // Limpiar al cerrar modal
    document.getElementById("modalFlujo").addEventListener("hidden.bs.modal", () => {
        activeId = null;
        renderProcesos();
        renderDetail();
    });

    renderProcesos();
});