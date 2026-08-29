// El boton de alta nace oculto en la vista. Se enciende solo si el servidor dice que
// esta persona puede crear categorias (Sistemas). Es cortesia, no seguridad: quien
// decide de verdad es CrearCategoria.
document.addEventListener('DOMContentLoaded', function () {
    var boton = document.getElementById('btnAgregarCategoria');
    if (!boton) return;

    fetch('/Categoria/Datos')
        .then(res => res.json())
        .then(data => {
            if (data.permisos && data.permisos[0] && data.permisos[0].puedeCrear) {
                boton.classList.remove('d-none');
            }
        })
        .catch(() => { /* Sin respuesta se queda oculto: es el lado seguro. */ });
});

$('#btnAgregarCategoria').on('click', function () {
    const currentTheme = localStorage.getItem('theme') || 'light';

    fetch('/Categoria/Datos')
        .then(res => res.json())
        .then(data => {
            const opcionesPrioridades = data.prioridad.map(p =>
                `<option value="${p.id_prio}">${p.n}</option>`
            ).join('');

            const opcionesResponsables = data.responsable.map(r =>
                `<option value="${r.usuarioid}">${r.nombreusuario}</option>`
            ).join('');

            Swal.fire({
                title: 'Agregar Categoría',
                theme: currentTheme,
                html: `
                    <div class="form-floating mb-2">
                        <input id="nombre" class="form-control" placeholder="Nombre">
                        <label for="nombre">Nombre</label>
                    </div>

                    <div class="form-floating mb-2">
                        <select id="responsable" class="form-select">
                            <option value="">Seleccione...</option>
                            ${opcionesResponsables}
                        </select>
                        <label for="responsable">Responsable</label>
                    </div>

                    <div class="form-floating mb-2">
                        <select id="prioridad" class="form-select">
                            <option value="">Seleccione...</option>
                            ${opcionesPrioridades}
                        </select>
                        <label for="prioridad">Prioridad</label>
                    </div>

                    <div class="form-floating mb-2">
                        <input id="icono" class="form-control" placeholder="fas fa-tag" value="fas fa-tag">
                        <label for="icono">Clase del icono</label>
                    </div>

                    <div class="mt-3">
                        <strong>Vista previa del icono:</strong>
                        <div id="iconPreview" style="font-size: 24px; margin-top: 5px;">
                            <i class="fas fa-tag"></i>
                        </div>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: 'Crear',
                didOpen: () => {
                    const iconInput = Swal.getPopup().querySelector('#icono');
                    iconInput.addEventListener('input', function () {
                        document.getElementById('iconPreview').innerHTML = `<i class="${this.value}"></i>`;
                    });
                },
                preConfirm: () => {
                    const nombre = Swal.getPopup().querySelector('#nombre').value;
                    const responsable = Swal.getPopup().querySelector('#responsable').value;
                    const prioridad = Swal.getPopup().querySelector('#prioridad').value;
                    const icono = Swal.getPopup().querySelector('#icono').value;

                    if (!nombre || !responsable || !prioridad || !icono) {
                        Swal.showValidationMessage('Todos los campos son obligatorios');
                        return false;
                    }

                    return { nombre, responsable, prioridad, icono };
                }
            }).then(result => {
                if (result.isConfirmed && result.value) {
                    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

                    const formData = new FormData();
                    formData.append('__RequestVerificationToken', token);
                    for (const key in result.value) {
                        formData.append(key, result.value[key]);
                    }

                    fetch('/Categoria/CrearCategoria', {
                        method: 'POST',
                        body: formData
                    })
                        .then(res => res.json())
                        .then(data => {
                            if (data.success) {
                                Swal.fire({
                                    icon: 'success',
                                    title: 'Creado',
                                    text: 'La categoría fue creada correctamente.',
                                    theme: currentTheme
                                });
                                tabla.reload();
                            } else {
                                Swal.fire({
                                    icon: 'error',
                                    title: 'Error',
                                    text: data.error || 'No se pudo crear.',
                                    theme: currentTheme
                                });
                            }
                        })
                        .catch(err => {
                            Swal.fire({
                                icon: 'error',
                                title: 'Error',
                                text: 'Error de red o servidor.',
                                theme: currentTheme
                            });
                            console.error(err);
                        });
                }
            });
        });
});

var tabla;
// La tabla la construye createTable (TableBuilder): genera su propio <thead> desde
// "columns", asi que no hay encabezados que sincronizar a mano. Los datos llegan
// paginados desde /Categoria/CategoriasTodos.
document.addEventListener('DOMContentLoaded', function () {
    if (!document.querySelector('#tablaCategoria')) return;

    tabla = createTable({
        selector: '#tablaCategoria',
        path: '/Categoria/CategoriasTodos',
        rowKey: 'id_cat',
        defaultPageSize: 10,
        searchPlaceholder: 'Buscar categoria...',
        exports: ['excel'],
        columns: [
            { title: 'ID', data: 'id_cat' },
            {
                title: 'Nombre',
                data: 'n_cat',
                formatter: (v, row) =>
                    `<span><i class="${row.icon_class || 'fas fa-tag'} me-2"></i>${escaparHtmlCat(v)}</span>`
            },
            {
                title: 'Prioridad',
                data: 'prioridad',
                formatter: (v) => `<span class="badge bg-primary">${escaparHtmlCat(v)}</span>`
            },
            {
                title: 'Responsable',
                data: 'responsable_nombre',
                formatter: (v) => escaparHtmlCat(v || '—')
            },
            {
                title: 'Tickets',
                data: 'total_tickets',
                formatter: (v, row) =>
                    `<div>${v ?? 0} <small class="text-muted">(${parseFloat(row.porcentaje || 0).toFixed(2)}%)</small></div>`
            }
        ],
        actions: [
            { icon: 'fa-pen-to-square', color: 'green-500', title: 'Editar',  onClick: row => editarCategoria(row) },
            { icon: 'fa-trash',         color: 'red-500',   title: 'Eliminar', onClick: row => eliminarCategoria(row) }
        ]
    });
});

function escaparHtmlCat(t) {
    return String(t ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

// La llama el boton "Editar" de createTable, que ya entrega la fila completa;
// antes se rescataba del DOM con la API de DataTables.
function editarCategoria(row) {
    const currentTheme = localStorage.getItem('theme') || 'light';

    fetch('/Categoria/Datos')
        .then(res => res.json())
        .then(data => {
            const opcionesPrioridades = data.prioridad.map(p =>
                `<option value="${p.id_prio}" ${p.id_prio === row.id_prio ? 'selected' : ''}>${p.n}</option>`
            ).join('');

            const opcionesResponsables = data.responsable.map(r =>
                `<option value="${r.usuarioid}" ${r.usuarioid === row.responsableid ? 'selected' : ''}>${r.nombreusuario}</option>`
            ).join('');

            Swal.fire({
                title: 'Editar Categoría',
                theme: currentTheme,
                html: `
                    <div class="form-floating mb-2">
                        <input id="nombre" class="form-control" placeholder="Nombre" value="${row.n_cat}">
                        <label for="nombre">Nombre</label>
                    </div>

                    <div class="form-floating mb-2">
                        <select id="responsable" class="form-select">
                            ${opcionesResponsables}
                        </select>
                        <label for="responsable">Responsable</label>
                    </div>

                    <div class="form-floating mb-2">
                        <select id="prioridad" class="form-select">
                            ${opcionesPrioridades}
                        </select>
                        <label for="prioridad">Prioridad</label>
                    </div>

                    <div class="form-floating mb-2">
                        <input id="icono" class="form-control" value="${row.icon_class}" placeholder="Clase del icono">
                        <label for="icono">Clase del icono (ej. fas fa-bug)</label>
                    </div>

                    <div class="mt-3">
                        <strong>Vista previa del icono:</strong>
                        <div id="iconPreview" style="font-size: 24px; margin-top: 5px;">
                            <i class="${row.icon_class}"></i>
                        </div>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: 'Editar',
                didOpen: () => {
                    const iconInput = Swal.getPopup().querySelector('#icono');
                    iconInput.addEventListener('input', function () {
                        document.getElementById('iconPreview').innerHTML = `<i class="${this.value}"></i>`;
                    });
                },
                preConfirm: () => {
                    const nombre = Swal.getPopup().querySelector('#nombre').value;
                    const responsable = Swal.getPopup().querySelector('#responsable').value;
                    const prioridad = Swal.getPopup().querySelector('#prioridad').value;
                    const icono = Swal.getPopup().querySelector('#icono').value;

                    if (!nombre || !responsable || !prioridad || !icono) {
                        Swal.showValidationMessage('Todos los campos son obligatorios');
                        return false;
                    }

                    return { id: row.id_cat, nombre, responsable, prioridad, icono };
                }
            }).then(result => {
                if (result.isConfirmed && result.value) {
                    const formData = new FormData();
                    formData.append('__RequestVerificationToken', $('input[name="__RequestVerificationToken"]')[0].value);
                    for (const key in result.value) {
                        formData.append(key, result.value[key]);
                    }

                    fetch('/Categoria/EditarCategoria', {
                        method: 'POST',
                        body: formData
                    })
                        .then(res => res.json())
                        .then(data => {
                            if (data.success) {
                                Swal.fire({ icon: 'success', theme: currentTheme, title: 'Actualizado', text: 'La categoría fue actualizada.' });
                                tabla.reload();
                            } else {
                                Swal.fire({ icon: 'error', theme: currentTheme, title: 'Error', text: data.error || 'No se pudo actualizar.' });
                            }
                        })
                        .catch(err => {
                            Swal.fire({ icon: 'error', theme: currentTheme, title: 'Error', text: 'Error de red o servidor.' });
                            console.error(err);
                        });
                }
            });
        });
}

function eliminarCategoria(fila) {
    const id = fila.id_cat;
    const currentTheme = localStorage.getItem('theme') || 'light';
    Swal.fire({
        title: '¿Estás seguro?',
        text: 'Esta acción no se puede deshacer.',
        icon: 'warning',
        theme: currentTheme,
        showCancelButton: true,
        confirmButtonText: 'Sí, borrar',
        cancelButtonText: 'Cancelar'
    }).then(result => {
        if (result.isConfirmed) {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

            const formData = new FormData();
            formData.append('__RequestVerificationToken', token);
            formData.append('id', id);

            fetch('/Categoria/EliminarCategoria', {
                method: 'POST',
                body: formData
            })
                .then(res => res.json())
                .then(data => {
                    if (data.success) {
                        Swal.fire({
                            icon: 'success',
                            title: 'Eliminado',
                            text: 'La categoría ha sido eliminada.',
                            theme: currentTheme
                        });
                        tabla.reload();
                    } else {
                        Swal.fire({
                            icon: 'error',
                            title: 'Error',
                            text: data.error || 'No se pudo eliminar.',
                            theme: currentTheme
                        });
                    }
                })
                .catch(err => {
                    Swal.fire({
                        icon: 'error',
                        title: 'Error',
                        text: 'Error de red o servidor.',
                        theme: currentTheme
                    });
                    console.error(err);
                });
        }
    });
}
