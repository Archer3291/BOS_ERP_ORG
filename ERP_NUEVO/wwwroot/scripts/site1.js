$(document).ready(function () {
    const html = document.documentElement;
    const themeToggle = $('#themeToggle');

    // Leer tema guardado
    const savedTheme = localStorage.getItem('theme') || 'light';
    html.setAttribute('data-bs-theme', savedTheme);

    // Cambiar tema al hacer toggle
    themeToggle.change(function () {
        const isDark = themeToggle.prop('checked');
        const newTheme = isDark ? 'dark' : 'light';
        html.setAttribute('data-bs-theme', newTheme);
        localStorage.setItem('theme', newTheme);
    });
});

$(document).ready(function () {
    function attachTooltips() {
        // 1. Eliminar del body cualquier tooltip huérfano (sin fila viva asociada)
        $('body > span.desc-tooltip').each(function () {
            $(this).remove();
        });

        // 2. Mover los spans nuevos al body y asociarlos a su fila
        $('.modern-table tbody .desc-tooltip').each(function () {
            const $span = $(this);
            const $tr = $span.closest('tr');
            $tr.data('tooltip', $span);
            $span.hide().appendTo('body'); // asegurarse de que arranque oculto
        });
    }

    attachTooltips();

    const tbody = document.querySelector('.modern-table tbody');
    if (tbody) {
        new MutationObserver(() => {
            // Ocultar y destruir cualquier tooltip visible antes del re-render
            $('body > span.desc-tooltip').hide().remove();
            attachTooltips();
        }).observe(tbody, { childList: true, subtree: false });
    }

    $(document).on('mouseenter', '.modern-table tbody tr', function () {
        const $tt = $(this).data('tooltip');
        if ($tt) $tt.show();
    });

    $(document).on('mousemove', '.modern-table tbody tr', function (e) {
        const $tt = $(this).data('tooltip');
        if (!$tt) return;

        let x = e.clientX + 18;
        let y = e.clientY + 14;

        const ttW = $tt.outerWidth();
        const ttH = $tt.outerHeight();
        if (x + ttW > window.innerWidth - 10) x = e.clientX - ttW - 12;
        if (y + ttH > window.innerHeight - 10) y = e.clientY - ttH - 12;

        $tt.css({ top: y + 'px', left: x + 'px' });
    });

    $(document).on('mouseleave', '.modern-table tbody tr', function () {
        const $tt = $(this).data('tooltip');
        if ($tt) $tt.hide();
    });
});

document.querySelectorAll('.list-group-item').forEach(item => {
    item.addEventListener('click', function () {
        // Elimina la clase 'active' de todos los elementos
        document.querySelectorAll('.list-group-item').forEach(link => {
            link.classList.remove('active');
        });
        // Agrega la clase 'active' al ítem clickeado
        item.classList.add('active');
    });
});

let Currency = new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
});

let CurrencyUSD = new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
});

let CurrencyEUR = new Intl.NumberFormat('es-ES', {
    style: 'currency',
    currency: 'EUR',
});

var toastMixin = Swal.mixin({
    toast: true,
    icon: 'success',
    title: 'General Title',
    position: 'top-right',
    showConfirmButton: false,
    timer: 3000,
    timerProgressBar: true,
    didOpen: (toast) => {
        toast.addEventListener('mouseenter', Swal.stopTimer)
        toast.addEventListener('mouseleave', Swal.resumeTimer)
    }
});

function round(value, decimals = 2) {
    return Number(Math.round(value + "e" + decimals) + "e-" + decimals);
}

var _Swal = Swal.mixin({
    confirmButtonText: 'Aceptar',
    cancelButtonText: 'Cancelar',
    showCancelButton: true,
    confirmButtonColor: "#3085d6",
    cancelButtonColor: "#d33",
})

async function GetData(options) {
    if (!options.hasOwnProperty('path')) {
        _Swal.fire({
            icon: 'error',
            title: 'Missing path',
            text: 'You need to add the path to get the data'
        });
        return;
    }

    let _data = new FormData();

    if ($('input[name=__RequestVerificationToken]').length > 0) {
        _data.append('__RequestVerificationToken', $('input[name=__RequestVerificationToken]')[0].value);
    }

    if (options.hasOwnProperty('data')) {
        Object.entries(options.data).forEach(([k, v]) => {
            if (v instanceof FileList) {
                for (const file of v) {
                    _data.append(k, file);
                }
            } else if (Array.isArray(v)) {
                v.forEach(item => _data.append(k, item));
            } else {
                _data.append(k, v);
            }
        });
    }

    const fetchOptions = {
        method: options.method ?? 'POST'
    };

    if (fetchOptions.method !== 'GET') {
        fetchOptions.body = _data;
    }

    return fetch(options.path, fetchOptions)
        .then(response => {
            if (response.status == 400 || response.status == 200) {
                return response.text();
            }
            throw new Error(response.status);
        })
        .then(text => {
            try {
                const data = JSON.parse(text);
                if (options.swalResponse) {
                    if (data.icon === "success") {
                        if (options.swalType == 'modal') {
                            _SwalReload(data, options.swalPath);
                        } else {
                            toastMixinReload(data, options.swalPath);
                        }
                    } else {
                        if (options.swalType == 'modal') {
                            _Swal.fire(data);
                        } else {
                            toastMixin.fire(data);
                        }
                    }
                }
                return data;
            } catch (err) {
                return text;
            }
        })
        .catch(error => {
            toastMixin.fire({
                title: error,
                icon: 'error'
            });
        });
}


async function postFormData(formId, url) {
    const form = document.forms[formId];
    if (!form) {
        _Swal.fire({
            icon: 'error',
            title: 'Error al recuperar el formulario',
            text: `Ocurrió un error al recuperar el formulario con el id '${formId}'.`
        });
        return;
    }

    const formData = new FormData();

    // Recorrer todos los campos del formulario
    form.querySelectorAll('input, select, textarea').forEach(el => {
        const name = el.name;
        if (!name) return; // ignorar si no tiene name

        switch (el.type) {
            case 'checkbox':
                // enviar 1 si está marcado, 0 si no
                formData.append(name, el.checked ? "1" : "0");
                break;
            case 'radio':
                if (el.checked) {
                    formData.append(name, el.value);
                }
                break;
            case 'file':
                // Solo enviar si hay un archivo seleccionado
                if (el.files.length > 0) {
                    formData.append(name, el.files[0]);
                }
                break;
            default:
                // incluir el valor aunque esté vacío
                formData.append(name, el.value ?? "");
        }
    });

    try {
        const response = await fetch(url, {
            method: 'POST',
            body: formData
        });
        return await response.json();
    } catch (error) {
        console.error('Error al enviar el formulario:', error);
        toastMixin.fire({
            icon: 'error',
            title: 'Error en la comunicación con el servidor'
        });
        return null;
    }
}

$(document).ready(function () {
    let mixin_data = localStorage.getItem("mixin_data");
    if (mixin_data) {
        toastMixin.fire(JSON.parse(mixin_data));
        localStorage.removeItem("mixin_data");
    }
    let swal_data = localStorage.getItem("swal_data");
    if (swal_data) {
        _Swal.fire(JSON.parse(swal_data));
        localStorage.removeItem("swal_data");
    }
})

function toastMixinReload(data, path) {
    localStorage.setItem("mixin_data", JSON.stringify(data));
    if (path)
        window.location.href = path;
    else
        location.reload();
}
function _SwalReload(data, path) {
    localStorage.setItem("swal_data", JSON.stringify(data));
    if (path)
        window.location.href = path;
    else
        location.reload();
}

async function convertToBase64(imagePath) {
    try {
        // Cargar la imagen como Blob utilizando fetch
        const response = await fetch(imagePath);
        const blob = await response.blob();

        // Convertir el Blob a Base64 usando FileReader
        return await new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = function () {
                resolve(reader.result); // Retorna el string Base64
            };
            reader.onerror = function () {
                reject(new Error('Error al convertir la imagen a Base64'));
            };
            reader.readAsDataURL(blob); // Leer el Blob como Data URL (Base64)
        });
    } catch (error) {
        console.error('Error al convertir la imagen:', error);
        throw error; // Propagar el error
    }
}

function dateFormatter(date) {
    // // Extraer el número de milisegundos
    // const timestamp = parseInt(date.match(/\d+/)[0], 10);

    // // Crear objeto Date
    // const dateObj = new Date(timestamp);

    // // Formatear a "dd/mm/yy hh-mm"
    // const dd = String(dateObj.getDate()).padStart(2, '0');
    // const mm = String(dateObj.getMonth() + 1).padStart(2, '0'); // enero es 0
    // const yy = String(dateObj.getFullYear()).slice(-2);

    // const hh = String(dateObj.getHours()).padStart(2, '0');
    // const min = String(dateObj.getMinutes()).padStart(2, '0');
    // let dateFormatted


    // if (hh != '00')
    //     dateFormatted = `${dd}/${mm}/${yy} ${hh}:${min}`;
    // else
    //     dateFormatted = `${dd}/${mm}/${yy}`;

    return date.replace('T', ' ').substring(0, 16);
}

function parseTimestamp(date) {
    const timestamp = parseInt(date.match(/\d+/)[0], 10);
    return new Date(timestamp); // <-- devolvemos Date real
}


function parseJsonDate(jsonDate) {
    const timestamp = parseInt(jsonDate.match(/\d+/)[0], 10);
    return new Date(timestamp);
}

function getIconFile(extension) {
    if (!extension) return 'fa-file-xmark'; // o 'default'

    let ext = extension.toLowerCase();

    if (!ext.startsWith('.')) {
        ext = '.' + ext;
    }

    const iconMap = {
        '.pdf': 'fa-file-pdf',
        '.jpg': 'fa-file-image',
        '.jpeg': 'fa-file-image',
        '.png': 'fa-file-image',
        '.gif': 'fa-file-image',
        '.doc': 'fa-file-word',
        '.docx': 'fa-file-word',
        '.xls': 'fa-file-excel',
        '.xlsx': 'fa-file-excel',
        '.ppt': 'fa-file-powerpoint',
        '.pptx': 'fa-file-powerpoint',
        '.txt': 'fa-file-lines',
        '.zip': 'fa-file-zipper',
        '.rar': 'fa-file-zipper',
        '.xml': 'fa-file-code',
        '.json': 'fa-file-code',
        '.csv': 'fa-file-csv',
        'default': 'fa-file'
    };

    return iconMap[ext] || iconMap['default'];
}



function getTrimmedCanvas(canvas) {
    const ctx = canvas.getContext('2d');
    const width = canvas.width;
    const height = canvas.height;
    const imageData = ctx.getImageData(0, 0, width, height).data;

    let top = null, bottom = null, left = null, right = null;

    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const alpha = imageData[(y * width + x) * 4 + 3];
            if (alpha > 0) {
                if (top === null) top = y;
                bottom = y;
                if (left === null || x < left) left = x;
                if (right === null || x > right) right = x;
            }
        }
    }

    if (top === null) return null; // canvas vacío

    const trimmedWidth = right - left;
    const trimmedHeight = bottom - top;
    const trimmed = document.createElement('canvas');
    trimmed.width = trimmedWidth;
    trimmed.height = trimmedHeight;

    trimmed.getContext('2d').drawImage(
        canvas,
        left, top, trimmedWidth, trimmedHeight,
        0, 0, trimmedWidth, trimmedHeight
    );

    return trimmed;
}

function calcularMinutosHabiles(inicio, fin) {
    const INICIO_HORA = 9;
    const FIN_HORA = 16;

    if (fin <= inicio) return 0;

    let totalMinutos = 0;

    let current = new Date(inicio);
    current.setSeconds(0, 0); // Quitar segundos/milisegundos

    const end = new Date(fin);
    end.setSeconds(0, 0);

    while (current <= end) {
        const dia = current.getDay();

        // Si es día hábil
        if (dia >= 1 && dia <= 5) {
            const fechaActual = new Date(current);
            fechaActual.setHours(0, 0, 0, 0);

            const inicioLaboral = new Date(fechaActual);
            inicioLaboral.setHours(INICIO_HORA, 0, 0, 0);

            const finLaboral = new Date(fechaActual);
            finLaboral.setHours(FIN_HORA, 0, 0, 0);

            // Inicio del día laboral: max entre inicio laboral y current
            const desde = new Date(Math.max(current, inicioLaboral));
            // Fin del día laboral: min entre fin laboral y fin real
            const hasta = new Date(Math.min(finLaboral, end));

            if (desde < hasta) {
                const diffMin = Math.floor((hasta - desde) / (1000 * 60));
                totalMinutos += diffMin;
            }
        }

        // Avanzar al siguiente día
        current.setDate(current.getDate() + 1);
        current.setHours(0, 0, 0, 0);
    }

    return totalMinutos;
}

function calcularDiferencia(fecha1, fecha2) {
    const f1 = new Date(dateFormatter(fecha1));
    const f2 = new Date(dateFormatter(fecha2));
    const diffMs = f2 - f1;

    if (isNaN(diffMs) || diffMs < 0) return ''; // Evita errores

    const totalSeconds = Math.floor(diffMs / 1000);
    const days = Math.floor(totalSeconds / 86400);
    const hours = Math.floor((totalSeconds % 86400) / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    const partes = [];
    if (days) partes.push(`${days}d`);
    if (hours) partes.push(`${hours}h`);
    if (minutes) partes.push(`${minutes}m`);
    if (seconds || partes.length === 0) partes.push(`${seconds}s`);

    return partes.join(' ');
}

$(document).on('change input keyup paste', '.lock-max', function () {
    const max = parseFloat(this.max);
    const value = this.value;

    if (!isNaN(value) && value !== "" && isFinite(value)) {
        const num = parseFloat(value);
        if (!isNaN(num) && !isNaN(max) && num > max) {
            this.value = max;
        }
    }
});

$(document).on('change input keyup paste', '.lock-min', function () {
    const min = parseFloat(this.min) || 0;
    const value = this.value;

    if (!isNaN(value) && value !== "" && isFinite(value)) {
        const num = parseFloat(value);
        if (!isNaN(num) && num < min) {
            this.value = min;
        }
    }
});




class TomSelectManager {
    constructor() {
        this.instances = new Map();
        this.defaultConfig = {
            create: false,
            maxItems: 1,
            sortField: {
                field: "text",
                direction: "asc"
            }
        };
    }

    /**
     * Valida que exista el elemento DOM
     */
    _validateElement(selector) {
        const element = document.querySelector(selector);
        if (!element) {
            console.warn(`TomSelectManager: Elemento '${selector}' no encontrado en el DOM`);
            return false;
        }
        return true;
    }

    /**
     * Valida que los datos existan y tengan el formato correcto
     */
    _validateData(data, dataProperty) {
        if (!data || !data[dataProperty]) {
            console.warn(`TomSelectManager: Datos '${dataProperty}' no encontrados o vacíos`);
            return false;
        }
        if (!Array.isArray(data[dataProperty])) {
            console.warn(`TomSelectManager: '${dataProperty}' debe ser un array`);
            return false;
        }
        return true;
    }

    /**
     * Destruye una instancia específica de TomSelect
     */
    destroy(id) {
        if (this.instances.has(id)) {
            try {
                this.instances.get(id).destroy();
                this.instances.delete(id);
            } catch (error) {
                console.error(`Error al destruir TomSelect '${id}':`, error);
            }
        }
    }

    /**
     * Destruye todas las instancias
     */
    destroyAll() {
        this.instances.forEach((instance, id) => {
            this.destroy(id);
        });
    }

    /**
     * Crea o actualiza un TomSelect
     */
    create(id, config) {
        try {
            if (!id || !config.selector || (!config.options && !config.load)) {
                throw new Error(
                    'Configuración inválida: se requiere id, selector y (options o load)'
                );
            }

            if (!this._validateElement(config.selector)) {
                return null;
            }

            this.destroy(id);

            if (!config.options) {
                config.options = [];
            }

            const finalConfig = {
                ...this.defaultConfig,
                ...config,
                render: {
                    option: config.render?.option || this._getDefaultOptionTemplate(config),
                    item: config.render?.item || this._getDefaultItemTemplate(config),
                    ...config.render
                }
            };

            const tomSelectInstance = new TomSelect(config.selector, finalConfig);
            this.instances.set(id, tomSelectInstance);

            return tomSelectInstance;

        } catch (error) {
            console.error(`Error al crear TomSelect '${id}':`, error);
            return null;
        }
    }

    /**
     * Template por defecto para opciones
     */
    _getDefaultOptionTemplate(config) {
        return function (item, escape) {
            const displayField = config.displayField || config.valueField;
            const secondaryField = config.secondaryField;

            return `
                <div>
                    <span class="title">${escape(item[displayField] || '')}</span>
                    ${secondaryField ? `<span class="url"> - ${escape(item[secondaryField] || '')}</span>` : ''}
                </div>`;
        };
    }

    /**
     * Template por defecto para elementos seleccionados
     */
    _getDefaultItemTemplate(config) {
        return function (item, escape) {
            const displayField = config.displayField || config.valueField;
            const secondaryField = config.secondaryField;

            const displayText = secondaryField
                ? `${escape(item[displayField] || '')} - ${escape(item[secondaryField] || '')}`
                : escape(item[displayField] || '');

            return `
                <div>
                    <span style="color:var(--text-dark)">${displayText}</span>
                </div>`;
        };
    }

    /**
     * Obtiene una instancia específica
     */
    getInstance(id) {
        return this.instances.get(id) || null;
    }

    /**
     * Inicializa múltiples TomSelects desde un objeto de configuración
     */
    initializeFromConfig(data, configurations) {
        const results = {};

        Object.entries(configurations).forEach(([id, config]) => {
            // Validar datos si se especifica dataProperty
            if (config.dataProperty && !this._validateData(data, config.dataProperty)) {
                results[id] = { success: false, error: 'Datos inválidos' };
                return;
            }

            // Preparar configuración final
            const finalConfig = {
                ...config,
                options: config.dataProperty ? data[config.dataProperty] : config.options
            };

            const instance = this.create(id, finalConfig);
            results[id] = { success: !!instance, instance };
        });

        return results;
    }
}


// EJEMPLOS DE USO ADICIONAL:

// 1. Crear un TomSelect individual con configuración personalizada
/*
tomManager.create('miSelect', {
    selector: '#miElemento',
    options: [{ id: 1, nombre: 'Opción 1' }],
    valueField: 'id',
    displayField: 'nombre',
    placeholder: 'Seleccione...',
    maxItems: 3, // Permitir múltiples selecciones
    create: true, // Permitir crear nuevas opciones
    render: {
        option: function(item, escape) {
            return `<div class="custom-option">${escape(item.nombre)}</div>`;
        }
    }
});
*/

// 2. Acceder a una instancia específica
/*
const agenciaSelect = tomManager.getInstance('agencia');
if (agenciaSelect) {
    agenciaSelect.setValue('some_value');
}
*/

// 3. Destruir instancias específicas
/*
tomManager.destroy('agencia'); // Destruir solo agencia
tomManager.destroyAll(); // Destruir todas
*/

// 4. Configuración dinámica
/*
function createDynamicSelect(selectId, endpoint, config) {
    fetch(endpoint)
        .then(response => response.json())
        .then(data => {
            tomManager.create(selectId, {
                selector: `#${selectId}`,
                options: data,
                ...config
            });
        });
}
*/

async function generarEtiquetas(productos, orden) {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF({
        orientation: "portrait",
        unit: "mm",
        format: [102, 127],
    });

    const logoBase64 = await convertToBase64(`/Content/img/${empresa}/logo.png`);
    const pdfWidth = doc.internal.pageSize.getWidth();

    productos.forEach((p, i) => {
        if (i > 0) doc.addPage();

        const logoX = 5;
        const logoY = 5;
        const logoWidth = 20;
        const logoHeight = 10;
        doc.addImage(logoBase64, "PNG", logoX, logoY, logoWidth, logoHeight);

        // ----- TEXTO DEL ENCABEZADO A LA DERECHA -----
        const textX = logoX + logoWidth + 5; // 5mm de separación
        const textY = logoY + logoHeight / 2; // centrado vertical respecto al logo
        const pageWidth = doc.internal.pageSize.getWidth();

        // Preparar texto
        const title = "SELLOS Y RETENES";
        const address = "Av. periférico oriente 200, Col. Prados de San Vicente 2da. Sección, C.P. 78394";
        const phone = "Tel: 444 567 5559   RFC: SRS080522T77";

        // Título
        doc.setFont("helvetica", "bold");
        doc.setFontSize(12);
        doc.text(title, textX, textY - 3); // un poco más arriba para centrar

        // Dirección
        doc.setFont("helvetica", "normal");
        doc.setFontSize(9);
        const splitAddress = doc.splitTextToSize(address, pageWidth - textX - 5); // 5mm margen derecho
        doc.text(splitAddress, textX, textY + 3); // un poco más abajo para centrar
        const splitPhone = doc.splitTextToSize(phone, pageWidth - textX - 5); // 5mm margen derecho
        doc.text(splitPhone, textX, textY + 10); // un poco más abajo para centrar

        // Línea divisoria debajo del encabezado
        doc.line(5, logoY + logoHeight + 7, pageWidth - 5, logoY + logoHeight + 7);

        // ----- BLOQUE IZQUIERDO (Producto) -----
        let xLeft = 8;
        let yStart = 38;

        doc.setFont("helvetica", "bold");
        doc.setFontSize(10);
        doc.text("Producto:", xLeft, yStart);
        doc.setFont("helvetica", "normal");
        doc.setFontSize(9);
        doc.text(p.descripcion, xLeft, yStart + 4, { maxWidth: 40 });

        doc.setFont("helvetica", "bold");
        doc.text("Código:", xLeft, yStart + 18);
        doc.setFont("helvetica", "normal");
        doc.text(p.codigo, xLeft, yStart + 22);

        doc.setFont("helvetica", "bold");
        doc.text("Contenido:", xLeft, yStart + 34);
        doc.setFont("helvetica", "normal");
        doc.text(`${p.cantidad} ${p.ud}`, xLeft, yStart + 38);

        // ----- BLOQUE DERECHO (Importador) -----
        let xRight = 55;
        let yRight = 38;

        doc.setFont("helvetica", "bold");
        doc.setFontSize(10);
        doc.text("Proveedor:", xRight, yRight);
        doc.setFont("helvetica", "normal");
        doc.setFontSize(9);
        doc.text(orden.n_prov, xRight, yRight + 4, { maxWidth: 40 });

        doc.setFont("helvetica", "bold");
        doc.text("Dirección:", xRight, yRight + 16);
        doc.setFont("helvetica", "normal");
        doc.text(
            `${orden.dir}, ${orden.col}, ${orden.pob}`,
            xRight,
            yRight + 20,
            { maxWidth: 40 }
        );
        doc.text(`CP ${orden.cp}`, xRight, yRight + 31);

        doc.setFont("helvetica", "bold");
        doc.text("RFC:", xRight, yRight + 38);
        doc.setFont("helvetica", "normal");
        doc.text(orden.rfc, xRight, yRight + 42);

        // ----- PIE DE PÁGINA -----
        doc.setFont("helvetica", "bold");
        doc.setFontSize(12);
        doc.text(`Hecho en ${orden.pais}`, 51, 110, { align: "center" });
    });

    doc.save(`etiqueta_nom050.pdf`);
}

function validarEmail(email) {
    const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return re.test(email);
}

async function hasPermission(permission) {
    const res = await GetData({
        path: '/Requisicion/HasPermission',
        data: {
            permission: permission
        }
    });

    return res.has === true;
}

async function hasAnyPermission(perms = []) {
    const res = await GetData({
        path: '/Requisicion/HasAnyPermission',
        data: {
            permissions: perms
        }
    });

    return res.has === true;
}



function actualizarUIPreferenciaSidebar() {
    const pref = localStorage.getItem('sidebarDefaultState') || 'expanded';
    const icon = document.getElementById('sidebar-pref-icon');
    const text = document.getElementById('sidebar-pref-text');

    if (pref === 'collapsed') {
        icon.className = 'fas fa-eye-slash me-2';
        text.textContent = 'Sidebar: oculto al inicio';
    } else {
        icon.className = 'fas fa-eye me-2';
        text.textContent = 'Sidebar: visible al inicio';
    }
}

function toggleSidebarPreference() {
    const current = localStorage.getItem('sidebarDefaultState') || 'expanded';
    const next = current === 'expanded' ? 'collapsed' : 'expanded';
    localStorage.setItem('sidebarDefaultState', next);
    actualizarUIPreferenciaSidebar();

    const msg = next === 'collapsed'
        ? '🙈 El sidebar iniciará oculto en tu próxima visita'
        : '👀 El sidebar iniciará visible en tu próxima visita';
    toastr.info(msg, 'Preferencia guardada', { timeOut: 3000 });
}

document.addEventListener('wheel', function (e) {
    if (document.activeElement.type === 'number') {
        e.preventDefault();
    }
}, { passive: false });

document.addEventListener('DOMContentLoaded', actualizarUIPreferenciaSidebar);


document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll('input').forEach(input => {
        input.setAttribute("autocomplete", "off");
    });
});

document.querySelectorAll('.copy').forEach(div => {
    const button = document.createElement('button');
    button.className = 'copy-btn';
    button.innerHTML = '<i class="fa-light fa-copy"></i>';

    button.addEventListener('click', async () => {
        const text = div.dataset.copy;

        try {
            await navigator.clipboard.writeText(text);
            button.innerHTML = '<i class="fa-solid fa-check-double"></i>';

            setTimeout(() => {
                button.innerHTML = '<i class="fa-light fa-copy"></i>';
            }, 1500);

        } catch (err) {
            console.error('No se pudo copiar:', err);
        }
    });

    div.appendChild(button);
});