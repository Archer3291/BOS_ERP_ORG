// handlers/datos-fiscales.js
// Depende de: utils.js, catalogos.js, state.js

var DatosFiscalesHandler = {

    // ── Metadatos que usa step4.js y step5.js ────────────────────────────────

    tipoRelacion: '04',

    // Recolecta los cambios para el back. Las claves deben coincidir con las que
    // lee DatosFiscalesTypeHandler (rfc, razonSocial, regimen, cp, usoCfdi).
    collectChanges: function (cfdi) {
        return {
            rfc: (document.getElementById('newRFC')?.value || cfdi.rfc || '').trim().toUpperCase(),
            razonSocial: (document.getElementById('newRazonSocial')?.value || cfdi.receptor || '').trim(),
            regimen: document.getElementById('newRegimen')?.value || cfdi.regimen || '',
            cp: (document.getElementById('newCP')?.value || cfdi.cp || '').trim(),
            usoCfdi: document.getElementById('newUsoCFDI')?.value || cfdi.usoCFDI || '',
        };
    },

    tabs: [
        {
            id: 'receptor',
            label: 'Datos receptor',
            build: function (el, cfdi) { DatosFiscalesHandler._buildTabReceptor(el, cfdi); }
        },
        {
            id: 'fiscal',
            label: 'Datos emisión',
            build: function (el, cfdi) { DatosFiscalesHandler._buildTabFiscal(el, cfdi); }
        },
    ],

    // Resumen que muestra el Step 5
    buildSummary: function (cfdi) {
        const newRFC = document.getElementById('newRFC')?.value || cfdi.rfc;
        const newRS = document.getElementById('newRazonSocial')?.value || cfdi.receptor;
        const newRegimen = document.getElementById('newRegimen')?.value || cfdi.regimen;
        const newCP = document.getElementById('newCP')?.value || cfdi.cp;
        const newUso = document.getElementById('newUsoCFDI')?.value || cfdi.usoCFDI;

        return `
            <div class="row g-3">
                <div class="col-md-6">
                    <div class="form-label-custom">RFC nuevo</div>
                    <div style="font-weight:600;font-family:'JetBrains Mono',monospace">
                        ${esc(newRFC)}
                    </div>
                </div>
                <div class="col-md-6">
                    <div class="form-label-custom">Razón social nueva</div>
                    <div style="font-weight:600">${esc(newRS)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Régimen</div>
                    <div>${esc(newRegimen)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">CP fiscal</div>
                    <div>${esc(newCP)}</div>
                </div>
                <div class="col-md-4">
                    <div class="form-label-custom">Uso CFDI</div>
                    <div>${esc(newUso)}</div>
                </div>
            </div>`;
    },

    // Pasos de ejecución específicos para Step 5
    execSteps: [
        { text: 'Verificar datos incorrectos', sub: 'RFC, régimen, CP, uso CFDI' },
        { text: 'Cancelar CFDI original (motivo 01)', sub: 'Envío al PAC certificado' },
        { text: 'Esperar aceptación del receptor', sub: 'Hasta 72 hrs hábiles' },
        { text: 'Emitir nuevo CFDI con datos corregidos', sub: 'Generación y sellado digital' },
        { text: 'Relacionar con UUID original (rel. 04)', sub: 'Nodo CfdiRelacionados' },
        { text: 'Notificar al receptor', sub: 'XML y PDF por correo' },
    ],

    // Checks adicionales en Step 5 (además de los comunes)
    confirmChecks: [
        // Este tipo no necesita checks extras, pero otros sí
    ],

    // ── Tabs internos (privados, solo los llama this.tabs) ───────────────────

    _buildTabReceptor: function (el, cfdi) {
        el.innerHTML = `
            ${fiscalAlert('info', 'fa-circle-info',
            'CFDI 4.0 — Datos del receptor obligatorios.',
            'Desde el 1°/ene/2022, el RFC, Razón Social, Régimen y CP del receptor son ' +
            'validados ante el SAT en tiempo real.')}

            <div class="row g-3">
                <div class="col-md-4">
                    ${formRow('RFC Receptor *',
                fInput('newRFC', cfdi.rfc,
                    'maxlength="13" style="text-transform:uppercase" ' +
                    'oninput="this.value=this.value.toUpperCase();clearError(\'newRFC\')" ' +
                    'onblur="DatosFiscalesHandler._validateRFC()"'),
                'Personas Físicas: 13 chars · Personas Morales: 12 chars')}
                </div>
                <div class="col-md-5">
                    ${formRow('Nombre / Razón Social *',
                    fInput('newRazonSocial', cfdi.receptor,
                        'style="text-transform:uppercase" ' +
                        'oninput="this.value=this.value.toUpperCase()"'),
                    'Debe coincidir exactamente con el RFC ante el SAT.')}
                </div>
                <div class="col-md-3">
                    ${formRow('Código Postal *',
                        fInput('newCP', cfdi.cp,
                            'maxlength="5" pattern="[0-9]{5}" ' +
                            'oninput="soloNums(this)" ' +
                            'onblur="DatosFiscalesHandler._validateCP()"'),
                        'CP registrado en el SAT como domicilio fiscal.')}
                </div>
                <div class="col-md-6">
                    ${formRow('Régimen Fiscal del Receptor *',
                            fSelect('newRegimen',
                                catOpts(CAT_REGIMEN_FISCAL, cfdi.regimen),
                                'onchange="DatosFiscalesHandler._onRegimenChange()"'),
                            'Régimen bajo el que tributa el receptor.')}
                </div>
                <div class="col-md-6">
                    ${formRow('Uso del CFDI *',
                                fSelect('newUsoCFDI',
                                    catOpts(CAT_USO_CFDI, cfdi.usoCFDI),
                                    'onchange="DatosFiscalesHandler._validateUsoCFDI()"'),
                                'Debe ser compatible con el régimen fiscal del receptor.')}
                </div>
            </div>

            ${sectionTitle('Compatibilidad RFC ↔ Régimen ↔ Uso CFDI')}
            <div id="compatMatrix" class="row g-2"></div>`;

        // Inicializar validaciones después de renderizar
        setTimeout(() => {
            addValidationEvents([{
                id: 'newRFC',
                fn: validarRFC,
                msg: 'RFC con formato inválido.'
            }]);
            DatosFiscalesHandler._onRegimenChange();
            DatosFiscalesHandler._renderCompatMatrix();
        }, 80);
    },

    _buildTabFiscal: function (el, cfdi) {
        const hoy = new Date().toISOString().split('T')[0];
        el.innerHTML = `
            ${fiscalAlert('warning', 'fa-calendar-exclamation',
            'Fecha de emisión.',
            'La fecha debe ser del mes en curso o del mes anterior como máximo.')}

            <div class="row g-3">
                <div class="col-md-4">
                    ${formRow('Fecha y hora de emisión *',
                `<input class="form-control-custom" type="datetime-local" id="newFechaEmision"
                            value="${cfdi.fecha}T12:00:00" max="${hoy}T23:59:59"
                            onchange="DatosFiscalesHandler._validateFecha()">`,
                'No puede ser futura.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Exportación *',
                    fSelect('newExportacion', catOpts(CAT_EXPORTACION, '01')),
                    'Para operaciones nacionales selecciona 01.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Serie',
                        fInput('newSerie', cfdi.tipo,
                            'maxlength="25" style="text-transform:uppercase"'),
                        'Alfanumérico, sin caracteres especiales.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Folio',
                            fInput('newFolio', cfdi.folio, 'maxlength="40"'),
                            'Recomendado consecutivo por serie.')}
                </div>
                <div class="col-md-4">
                    ${formRow('Lugar de expedición (CP emisor) *',
                                fInput('newLugarExp', '',
                                    'maxlength="5" placeholder="CP domicilio fiscal emisor" ' +
                                    'oninput="soloNums(this)"'),
                                'CP del domicilio fiscal del emisor registrado en el SAT.')}
                </div>
            </div>
            <div id="fechaAlert" style="display:none" class="mt-3"></div>`;

        setTimeout(() => DatosFiscalesHandler._validateFecha(), 100);
    },

    // ── Métodos de validación internos ───────────────────────────────────────

    _validateRFC: function () {
        const el = document.getElementById('newRFC');
        if (!el) return;
        const v = el.value.trim().toUpperCase();
        if (!v) { clearError('newRFC'); return; }
        if (!validarRFC(v)) { markError('newRFC', 'Formato de RFC inválido.'); return; }
        clearError('newRFC');
        DatosFiscalesHandler._renderCompatMatrix();
    },

    _validateCP: function () {
        const el = document.getElementById('newCP');
        if (!el) return;
        const v = el.value.trim();
        if (v && (v.length !== 5 || !/^\d{5}$/.test(v)))
            markError('newCP', 'El CP debe tener exactamente 5 dígitos.');
        else
            clearError('newCP');
    },

    _onRegimenChange: function () {
        this._validateUsoCFDI();
        this._renderCompatMatrix();
    },

    _validateUsoCFDI: function () {
        const uso = document.getElementById('newUsoCFDI')?.value;
        const rfcEl = document.getElementById('newRFC');
        if (!uso || !rfcEl) return;
        const isF = rfcEl.value.trim().length === 13;
        const cat = CAT_USO_CFDI.find(u => u.clave === uso);
        if (cat) {
            const warn = (isF && !cat.persona.includes('F')) ||
                (!isF && !cat.persona.includes('M'));
            if (warn)
                markError('newUsoCFDI',
                    `Este uso CFDI no aplica para ${isF ? 'Persona Física' : 'Persona Moral'}.`);
            else
                clearError('newUsoCFDI');
        }
        this._renderCompatMatrix();
    },

    _renderCompatMatrix: function () {
        const el = document.getElementById('compatMatrix');
        if (!el) return;
        const rfc = document.getElementById('newRFC')?.value.trim() || '';
        const regimen = document.getElementById('newRegimen')?.value || '';
        const uso = document.getElementById('newUsoCFDI')?.value || '';
        if (!rfc || !regimen || !uso) { el.innerHTML = ''; return; }

        const isF = rfc.length === 13;
        const tipoPers = isF ? 'F' : 'M';
        const catReg = CAT_REGIMEN_FISCAL.find(r => r.clave === regimen);
        const catUso = CAT_USO_CFDI.find(u => u.clave === uso);
        const okReg = catReg?.persona.includes(tipoPers) ?? false;
        const okUso = catUso?.persona.includes(tipoPers) ?? false;

        const row = (label, ok, msg) => `
            <div class="col-12">
                <div class="checklist-item ${ok ? 'checked' : 'error'}">
                    <div class="check-icon">
                        <i class="fas ${ok ? 'fa-check' : 'fa-xmark'}"></i>
                    </div>
                    <div>
                        <div style="font-size:.83rem;font-weight:500">${label}</div>
                        ${!ok ? `<div style="font-size:.73rem;color:var(--danger-color)">${msg}</div>` : ''}
                    </div>
                </div>
            </div>`;

        el.innerHTML =
            row(`Régimen ${regimen} compatible con ${isF ? 'P.Física' : 'P.Moral'}`,
                okReg, `El régimen no aplica para ${isF ? 'P.Física' : 'P.Moral'}.`) +
            row(`Uso CFDI ${uso} compatible con ${isF ? 'P.Física' : 'P.Moral'}`,
                okUso, `El uso no aplica para ${isF ? 'P.Física' : 'P.Moral'}.`);
    },

    _validateFecha: function () {
        const el = document.getElementById('newFechaEmision');
        const alertEl = document.getElementById('fechaAlert');
        if (!el || !alertEl) return;
        const val = new Date(el.value);
        const now = new Date();
        const menos3m = new Date(now);
        menos3m.setMonth(now.getMonth() - 3);

        if (val > now) {
            alertEl.style.display = 'block';
            alertEl.innerHTML = fiscalAlert('danger', 'fa-triangle-exclamation',
                'Fecha futura no permitida.',
                'El SAT rechaza CFDI con fecha posterior a la actual.');
            markError('newFechaEmision', 'No puede ser una fecha futura.');
        } else if (val < menos3m) {
            alertEl.style.display = 'block';
            alertEl.innerHTML = fiscalAlert('warning', 'fa-calendar-xmark',
                'Fecha muy antigua.',
                'Emitir con fecha de más de 3 meses atrás puede generar observaciones.');
            clearError('newFechaEmision');
        } else {
            alertEl.style.display = 'none';
            clearError('newFechaEmision');
        }
    },
};