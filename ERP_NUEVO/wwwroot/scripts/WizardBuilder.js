function createWizard(options) {
    const {
        selector,                 // contenedor donde se dibuja la BARRA de pasos (obligatorio)
        panelsSelector = selector, // contenedor donde YA viven los paneles (obligatorio si es distinto a selector)
        steps = [],                // [{ id, label, panel, formId, validate, beforeLeave, onEnter, hideNext, errorMessage }]
        startStep = 1,
        validation = true,         // si true, valida checkValidity() del formId de cada paso automáticamente
        allowSkipForward = false,  // si true, se puede saltar a cualquier paso desde la barra aunque no se haya visitado
        showProgress = true,
        navButtons = true,         // si el plugin crea sus propios botones Anterior/Siguiente
        labels = { next: 'Siguiente', prev: 'Anterior' },
        onStepChange = null,       // (stepNumber, stepConfig) => {}
        onValidationError = null,  // (stepConfig, stepNumber) => {}  (si no se define, usa toastMixin)
        onComplete = null,         // () => {}  se dispara al entrar al último paso
    } = options;

    if (!options.hasOwnProperty('selector')) {
        _Swal.fire({ icon: 'error', title: `Propiedad faltante: 'selector'`, text: `Debes especificar la propiedad 'selector'.` });
        console.error(`Falta la propiedad 'selector'.`);
        return;
    }
    if (!options.hasOwnProperty('steps') || !Array.isArray(steps) || !steps.length) {
        _Swal.fire({ icon: 'error', title: `Propiedad faltante: 'steps'`, text: `Debes especificar al menos un paso en 'steps'.` });
        console.error(`Falta la propiedad 'steps'.`);
        return;
    }

    const barContainer = document.querySelector(selector);
    if (!barContainer) {
        _Swal.fire({ icon: 'error', title: `Contenedor no encontrado`, text: `No se encontró el selector '${selector}'.` });
        console.error(`No se encontró el contenedor con el selector: ${selector}`);
        return;
    }

    const panelsContainer = document.querySelector(panelsSelector);
    if (!panelsContainer) {
        _Swal.fire({ icon: 'error', title: `Contenedor no encontrado`, text: `No se encontró el selector de paneles '${panelsSelector}'.` });
        console.error(`No se encontró el contenedor de paneles: ${panelsSelector}`);
        return;
    }

    const wizardId = selector.replace('#', '');

    // ── Normalizar pasos ─────────────────────────────────────────
    const normalizedSteps = steps.map((s, i) => ({
        id: s.id ?? (i + 1),
        label: s.label ?? `Paso ${i + 1}`,
        panel: s.panel ?? `#step-${s.id ?? (i + 1)}`,
        formId: s.formId ?? null,
        validate: typeof s.validate === 'function' ? s.validate : null,
        beforeLeave: typeof s.beforeLeave === 'function' ? s.beforeLeave : null,
        onEnter: typeof s.onEnter === 'function' ? s.onEnter : null,
        hideNext: !!s.hideNext,
        errorMessage: s.errorMessage ?? 'Completa los campos requeridos antes de continuar.',
    }));

    let currentIndex = 0;      // 0-based
    let maxIndexReached = 0;

    // ── Barra de pasos ───────────────────────────────────────────
    barContainer.classList.add('wizard-bar');
    barContainer.innerHTML = normalizedSteps.map((s, i) => `
        ${i > 0 ? '<div class="wizard-divider"></div>' : ''}
        <div class="rq-wizard-step" data-step-index="${i}">
            <div class="rq-step-num">${i + 1}</div>
            <span class="rq-step-label">${s.label}</span>
        </div>
    `).join('');

    barContainer.querySelectorAll('.rq-wizard-step').forEach(el => {
        el.addEventListener('click', () => {
            const target = parseInt(el.dataset.stepIndex);
            if (target === currentIndex) return;
            if (target < currentIndex || allowSkipForward || target <= maxIndexReached) {
                goToStep(target + 1);
            }
        });
    });

    // ── Barra de progreso ────────────────────────────────────────
    let progressBarEl = null;
    if (showProgress) {
        const progressWrap = document.createElement('div');
        progressWrap.className = 'rq-progress';
        progressWrap.innerHTML = `<div class="rq-progress-bar" id="${wizardId}-progress-bar"></div>`;
        barContainer.insertAdjacentElement('afterend', progressWrap);
        progressBarEl = progressWrap.querySelector('.rq-progress-bar');
    }

    // ── Botones Anterior / Siguiente ─────────────────────────────
    let btnPrev = null, btnNext = null;
    if (navButtons) {
        const navWrap = document.createElement('div');
        navWrap.className = 'rq-wizard-nav';
        navWrap.innerHTML = `
            <button type="button" class="wizard-btn prev" style="display:none">
                <i class="fas fa-arrow-left"></i> ${labels.prev}
            </button>
            <button type="button" class="wizard-btn next">
                ${labels.next} <i class="fas fa-arrow-right"></i>
            </button>`;
        panelsContainer.insertAdjacentElement('afterend', navWrap);
        btnPrev = navWrap.querySelector('.prev');
        btnNext = navWrap.querySelector('.next');
        btnPrev.addEventListener('click', () => goToStep(currentIndex));
        btnNext.addEventListener('click', () => goToStep(currentIndex + 2));
    }

    // ── Estado inicial de paneles ────────────────────────────────
    normalizedSteps.forEach((s, i) => {
        const panel = document.querySelector(s.panel);
        if (panel) panel.classList.toggle('wizard-panel-active', i === 0);
    });

    // =========================================================================
    function validateStep(index) {
        const step = normalizedSteps[index];
        if (!step) return true;

        if (validation && step.formId) {
            const form = document.getElementById(step.formId);
            if (form && !form.checkValidity()) {
                form.classList.add('was-validated');
                markStepError(index, true);
                if (onValidationError) onValidationError(step, index + 1);
                else if (typeof toastMixin !== 'undefined') {
                    toastMixin.fire({ icon: 'error', title: step.errorMessage });
                }
                return false;
            }
        }

        if (step.validate && !step.validate()) {
            markStepError(index, true);
            return false;
        }

        markStepError(index, false);
        return true;
    }

    function markStepError(index, hasError) {
        const el = barContainer.querySelector(`.rq-wizard-step[data-step-index="${index}"]`);
        if (el) el.classList.toggle('error', hasError);
    }

    function goToStep(targetOneBased, silent = false) {
        const targetIndex = targetOneBased - 1;
        if (targetIndex < 0 || targetIndex >= normalizedSteps.length) return false;
        if (targetIndex === currentIndex) return true;

        if (targetIndex > currentIndex) {
            const leaving = normalizedSteps[currentIndex];
            if (!validateStep(currentIndex)) return false;
            if (leaving.beforeLeave) leaving.beforeLeave();
        }

        const currentPanel = document.querySelector(normalizedSteps[currentIndex].panel);
        const targetPanel = document.querySelector(normalizedSteps[targetIndex].panel);
        if (currentPanel) currentPanel.classList.remove('wizard-panel-active');
        if (targetPanel) targetPanel.classList.add('wizard-panel-active');

        currentIndex = targetIndex;
        maxIndexReached = Math.max(maxIndexReached, currentIndex);

        const entering = normalizedSteps[currentIndex];
        if (entering.onEnter) entering.onEnter();

        updateUI();

        if (!silent && onStepChange) onStepChange(currentIndex + 1, entering);
        if (currentIndex === normalizedSteps.length - 1 && onComplete) onComplete();

        return true;
    }

    function updateUI() {
        barContainer.querySelectorAll('.rq-wizard-step').forEach((el, i) => {
            el.classList.toggle('active', i === currentIndex);
            el.classList.toggle('done', i < currentIndex);
        });

        if (progressBarEl) {
            const pct = Math.round(((currentIndex + 1) / normalizedSteps.length) * 100);
            progressBarEl.style.width = pct + '%';
        }

        if (btnPrev) btnPrev.style.display = currentIndex > 0 ? 'inline-flex' : 'none';
        if (btnNext) {
            const hideNext = normalizedSteps[currentIndex].hideNext || currentIndex === normalizedSteps.length - 1;
            btnNext.style.display = hideNext ? 'none' : 'inline-flex';
        }
    }

    updateUI();
    if (startStep > 1) goToStep(startStep, true);

    // =========================================================================
    // API PÚBLICA
    // =========================================================================
    return {
        next: () => goToStep(currentIndex + 2),
        prev: () => goToStep(currentIndex),
        goToStep: (n) => goToStep(n),
        getCurrentStep: () => currentIndex + 1,
        isValid: (n) => validateStep((n ?? currentIndex + 1) - 1),
        reset: () => goToStep(1, true),
    };
}