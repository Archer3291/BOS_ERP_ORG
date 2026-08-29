const formTicket = document.getElementById('formTicket');
const btnEnviar = document.getElementById('btnEnviarTicket');
const inputAdjuntos = document.getElementById('adjuntos');
const zonaDrop = document.getElementById('zona-drop');
const listaArchivos = document.getElementById('lista-archivos');
const mensaje = document.getElementById('mensaje');
const mensajeCount = document.getElementById('mensajeCount');

let archivosSeleccionados = []; // Aquí guardamos el estado real de los archivos

// Espejo de SoporteAdjuntos.MaximoArchivos / TamanoMaximoBytes.
const MAX_ARCHIVOS = 5;
const MAX_BYTES = 5 * 1024 * 1024;

formTicket.addEventListener('submit', async function (e) {
    e.preventDefault();

    const form = e.target;

    // La validacion del navegador se comprueba aqui y no solo en el listener de
    // Bootstrap: ese corre despues, asi que un formulario invalido se enviaba igual
    // por fetch y el error solo aparecia al volver del servidor.
    form.classList.add('was-validated');
    if (!form.checkValidity()) {
        const primerInvalido = form.querySelector(':invalid');
        if (primerInvalido) {
            primerInvalido.focus({ preventScroll: true });
            primerInvalido.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
        return;
    }

    const formData = new FormData(form);

    // La categoria ya viaja como campo oculto del formulario; anadirla aqui ademas
    // mandaba el valor dos veces y el binding de "int categoria" se quedaba en 0.

    // Estado de envio: sin esto un doble clic creaba dos tickets.
    const contenidoBoton = btnEnviar.innerHTML;
    btnEnviar.disabled = true;
    btnEnviar.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Enviando...';

    try {
        const response = await fetch('/EnviarTickets/EnviarTicket', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error("Error HTTP: " + response.status);
        }

        const data = await response.json();

        // Recargar el captcha pase lo que pase
        document.getElementById('captchaImg').src = '/Captcha/GenerarCaptcha?' + Math.random();

        if (data.success) {
            form.reset();
            form.classList.remove('was-validated');
            archivosSeleccionados = [];
            inputAdjuntos.value = '';
            mostrarListaArchivos();
            actualizarContador();

            mostrarResumenTicket(data);
        } else {
            toastr.error(data.error || 'Error al enviar el formulario.');
        }
    } catch (error) {
        // Captura errores de red, de parsing o del servidor
        toastr.error('Error del servidor: ' + error.message);
        // Recargar captcha si hubo un error grave también
        document.getElementById('captchaImg').src = '/Captcha/GenerarCaptcha?' + Math.random();
    } finally {
        btnEnviar.disabled = false;
        btnEnviar.innerHTML = contenidoBoton;
    }
});

// La prioridad se elige con pastillas de radio que ya se pintan del color del catalogo
// (prio.color) por CSS. Antes era un <select> con un cuadrito que un script coloreaba;
// el switch por id daba azul al 1 y rojo al 3, al reves de lo que significan en la base.

// ── Comprobante de alta ──────────────────────────────────────────────────
// El toast anterior se iba solo en unos segundos y no dejaba a la vista ni el folio del
// documento ni el id con el que se le da seguimiento al ticket.
function escaparHtml(texto) {
    const div = document.createElement('div');
    div.textContent = texto ?? '';
    return div.innerHTML;
}

function mostrarResumenTicket(data) {
    const seguimiento = escaparHtml(data.folioTicket);
    const documento = escaparHtml(data.folioDocumento) || '<span style="color:#888">sin documento</span>';
    const urlTicket = data.urlTicket || ('/Soporte/Ticket/' + encodeURIComponent(data.folioTicket ?? ''));

    Swal.fire({
        icon: 'success',
        title: 'Ticket registrado',
        html: `
            <p style="margin:0 0 1rem;color:#666">Guarda el ID de seguimiento: con él puedes consultar el avance.</p>
            <table style="width:100%;border-collapse:collapse;text-align:left;font-size:.95rem">
                <tr>
                    <td style="padding:.55rem .25rem;color:#888;border-bottom:1px solid #eee">ID de seguimiento</td>
                    <td style="padding:.55rem .25rem;border-bottom:1px solid #eee;font-weight:700;letter-spacing:.03em">${seguimiento}</td>
                </tr>
                <tr>
                    <td style="padding:.55rem .25rem;color:#888;border-bottom:1px solid #eee">Folio del documento</td>
                    <td style="padding:.55rem .25rem;border-bottom:1px solid #eee;font-weight:700">${documento}</td>
                </tr>
                <tr>
                    <td style="padding:.55rem .25rem;color:#888">Ticket</td>
                    <td style="padding:.55rem .25rem;font-weight:700">#${escaparHtml(data.idTicket)}</td>
                </tr>
            </table>`,
        showCancelButton: true,
        confirmButtonText: 'Ver ticket',
        cancelButtonText: 'Crear otro',
        confirmButtonColor: '#4f6bf6',
        cancelButtonColor: '#94a3b8',
        reverseButtons: true
    }).then((resultado) => {
        if (resultado.isConfirmed) {
            window.location.href = urlTicket;
        }
    });
}

// ── Contador de caracteres de la descripcion ─────────────────────────────
function actualizarContador() {
    if (mensajeCount) {
        mensajeCount.textContent = mensaje.value.length;
    }
}

if (mensaje) {
    mensaje.addEventListener('input', actualizarContador);
}

// ── Adjuntos ─────────────────────────────────────────────────────────────

// Click para abrir selector. Se ignora el click sintetico del propio input: al estar
// dentro de la zona vuelve a burbujear hasta aqui.
zonaDrop.addEventListener('click', (e) => {
    if (e.target !== inputAdjuntos) inputAdjuntos.click();
});

// Arrastrar archivos al área. El resaltado va por clase y no por style inline: los
// colores fijos que se ponian aqui (#e2f0ff / #f1f9ff) se veian rotos en tema oscuro.
zonaDrop.addEventListener('dragover', (e) => {
    e.preventDefault();
    zonaDrop.classList.add('is-drag');
});

zonaDrop.addEventListener('dragleave', () => {
    zonaDrop.classList.remove('is-drag');
});

zonaDrop.addEventListener('drop', (e) => {
    e.preventDefault();
    zonaDrop.classList.remove('is-drag');

    const nuevosArchivos = Array.from(e.dataTransfer.files);
    agregarArchivos(nuevosArchivos);
});

// Cuando se seleccionan archivos con el selector (click)
inputAdjuntos.addEventListener('change', () => {
    const nuevosArchivos = Array.from(inputAdjuntos.files);
    agregarArchivos(nuevosArchivos);
});

// Función para agregar archivos y actualizar input y lista
function agregarArchivos(nuevosArchivos) {
    // Concatenar sin duplicados por nombre
    nuevosArchivos.forEach(nuevoArchivo => {
        if (!archivosSeleccionados.some(f => f.name === nuevoArchivo.name)) {
            archivosSeleccionados.push(nuevoArchivo);
        }
    });

    // Estos límites deben coincidir con SoporteAdjuntos (servidor): antes el front
    // decía 2 archivos de 2MB y el backend aceptaba 5MB sin tope de cantidad.
    if (archivosSeleccionados.length > MAX_ARCHIVOS) {
        toastr.error(`Solo puedes subir hasta ${MAX_ARCHIVOS} archivos.`);
        archivosSeleccionados = archivosSeleccionados.slice(0, MAX_ARCHIVOS);
    }

    // Validar tamaños
    for (let archivo of [...archivosSeleccionados]) {
        if (archivo.size > MAX_BYTES) {
            toastr.error(`${archivo.name} excede el límite de ${MAX_BYTES / (1024 * 1024)}MB.`);
            archivosSeleccionados = archivosSeleccionados.filter(f => f !== archivo);
        }
    }

    sincronizarInput();
    mostrarListaArchivos();
}

// El input file no se puede modificar a mano: se reconstruye con DataTransfer para que
// lo que se envia coincida con lo que ve el usuario en la lista.
function sincronizarInput() {
    const dt = new DataTransfer();
    archivosSeleccionados.forEach(f => dt.items.add(f));
    inputAdjuntos.files = dt.files;
}

function formatearTamano(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(0) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

function iconoArchivo(nombre) {
    const ext = nombre.split('.').pop().toLowerCase();
    if (['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp'].includes(ext)) return 'fa-regular fa-file-image';
    if (ext === 'pdf') return 'fa-regular fa-file-pdf';
    if (['xls', 'xlsx', 'csv'].includes(ext)) return 'fa-regular fa-file-excel';
    if (['doc', 'docx'].includes(ext)) return 'fa-regular fa-file-word';
    if (['zip', 'rar', '7z'].includes(ext)) return 'fa-regular fa-file-zipper';
    return 'fa-regular fa-file-lines';
}

// Mostrar lista de archivos con botón para quitar cada uno. Se arma con nodos y
// textContent en lugar de interpolar el nombre dentro de innerHTML.
function mostrarListaArchivos() {
    listaArchivos.innerHTML = "";

    archivosSeleccionados.forEach((archivo, index) => {
        const item = document.createElement("div");
        item.className = "tkf-file";

        const icono = document.createElement("i");
        icono.className = iconoArchivo(archivo.name);

        const nombre = document.createElement("span");
        nombre.className = "tkf-file__name";
        nombre.textContent = archivo.name;
        nombre.title = archivo.name;

        const tamano = document.createElement("span");
        tamano.className = "tkf-file__size";
        tamano.textContent = formatearTamano(archivo.size);

        const quitar = document.createElement("button");
        quitar.type = "button";
        quitar.className = "tkf-file__x";
        quitar.dataset.index = index;
        quitar.setAttribute("aria-label", "Quitar " + archivo.name);
        quitar.innerHTML = '<i class="fa-solid fa-xmark"></i>';

        item.append(icono, nombre, tamano, quitar);
        listaArchivos.appendChild(item);
    });
}

// Eliminar archivo por índice
listaArchivos.addEventListener('click', (e) => {
    const boton = e.target.closest('.tkf-file__x');
    if (!boton) return;

    archivosSeleccionados.splice(Number(boton.dataset.index), 1);
    sincronizarInput();
    mostrarListaArchivos();
});
