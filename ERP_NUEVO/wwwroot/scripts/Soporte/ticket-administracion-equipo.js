// Tabla del equipo de soporte con createTable (TableBuilder). Sustituye a DataTables:
// genera su propio <thead> desde "columns", asi que no hay encabezados que sincronizar
// a mano. Los datos llegan paginados desde /Equipo/UsuariosTodos.
var tabla1;
let usuarioActual = null;

function escaparHtmlEq(t) {
    return String(t ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

$(document).ready(function () {
    if (!document.querySelector('#tablaUsuarios')) return;

    tabla1 = createTable({
        selector: '#tablaUsuarios',
        path: '/Equipo/UsuariosTodos',
        rowKey: 'usuarioid',
        defaultPageSize: 10,
        searchPlaceholder: 'Buscar por nombre, correo o usuario...',
        exports: ['excel'],
        columns: [
            { title: 'Nombre', data: 'nombre',
              formatter: (v, row) => escaparHtmlEq(`${v ?? ''} ${row.apellido ?? ''}`.trim()) },
            { title: 'Email', data: 'email' },
            { title: 'Nombre de usuario', data: 'nombreusuario' },
            { title: 'Rol', data: 'nombrerol',
              formatter: (v) => `<span class="badge bg-secondary">${escaparHtmlEq(v)}</span>` },
            // Que categorias atiende. El rol dice QUE puede hacer; esto dice DONDE, y
            // sin la segunda mitad no se entiende por que alguien no aparece en un combo.
            { title: 'Categorias que atiende', data: 'categorias', sortable: false,
              formatter: (v) => {
                  if (!v) return '<span class="text-muted fst-italic">Ninguna</span>';
                  return String(v).split(', ')
                      .map(c => `<span class="badge bg-light text-dark border me-1">${escaparHtmlEq(c)}</span>`)
                      .join('');
              } }
        ],
        actions: [
            { icon: 'fa-layer-group', color: 'blue-500', title: 'Categorias que atiende',
              onClick: row => abrirCategoriasUsuario(row) },
            { icon: 'fa-pen-to-square', color: 'green-500', title: 'Editar rol de tickets',
              onClick: row => abrirEditarUsuario(row) },
            { icon: 'fa-user-minus', color: 'red-500', title: 'Retirar del equipo de soporte',
              onClick: row => abrirRetirarUsuario(row) }
        ]
    });
});

// ── Categorias que atiende ──────────────────────────────────────────────────
// Se listan TODAS las categorias activas, no solo las que el que administra puede
// tocar: esconder las demas haria creer que la persona no las atiende. Las que no
// le corresponden llegan con editable=false y se pintan deshabilitadas.
function abrirCategoriasUsuario(fila) {
    usuarioActual = fila;

    const cuerpo = document.getElementById('listaCategoriasUsuario');
    if (!cuerpo) return;

    $('#nombreUsuarioCategorias').text(fila.nombreusuario || '');
    cuerpo.innerHTML = '<div class="text-muted small py-2">Cargando...</div>';
    new bootstrap.Modal(document.getElementById('modalCategoriasUsuario')).show();

    const formData = new FormData();
    formData.append('idUsuario', fila.usuarioid);

    fetch('/Equipo/CategoriasDe', { method: 'POST', body: formData })
        .then(r => r.json())
        .then(data => {
            if (!data.success) {
                cuerpo.innerHTML =
                    `<div class="alert alert-warning mb-0">${escaparHtmlEq(data.message || 'No se pudo cargar.')}</div>`;
                return;
            }

            if (!data.data || !data.data.length) {
                cuerpo.innerHTML = '<div class="text-muted small py-2">No hay categorias activas.</div>';
                return;
            }

            cuerpo.innerHTML = data.data.map(c => {
                const marcada = c.atiende ? 'checked' : '';
                const bloqueada = c.editable ? '' : 'disabled';
                // El asignador de una categoria la atiende por definicion: se muestra
                // marcado y fijo para que no parezca que se le puede quitar aqui.
                const nota = c.es_asignador
                    ? ' <span class="badge bg-primary-subtle text-primary-emphasis ms-1">Asignador</span>'
                    : (c.editable ? '' : ' <span class="badge bg-light text-muted border ms-1">Otra area</span>');

                return `<div class="form-check py-1">
                            <input class="form-check-input cat-usuario" type="checkbox"
                                   value="${c.id_cat}" id="catUsr${c.id_cat}" ${marcada} ${bloqueada}>
                            <label class="form-check-label" for="catUsr${c.id_cat}">
                                ${escaparHtmlEq(c.n_cat)}${nota}
                            </label>
                        </div>`;
            }).join('');
        })
        .catch(() => {
            cuerpo.innerHTML = '<div class="alert alert-danger mb-0">Error al comunicarse con el servidor.</div>';
        });
}

// ── Editar ──────────────────────────────────────────────────────────────────
function abrirEditarUsuario(fila) {
    usuarioActual = fila;

    $('#editNombreUsuario').val(fila.nombreusuario);
    $('#editNombre').val(fila.nombre);
    $('#editEmail').val(fila.email);

    fetch('/Equipo/DatosSelect')
        .then(response => response.json())
        .then(data => {
            llenarSelect('editRol', data.roles, 'id_rol_tkt', 'nombre');
            $('#editRol').val(String(fila.id_rol_tkt)).trigger('change');
            new bootstrap.Modal(document.getElementById('modalEditarUsuario')).show();
        })
        .catch(error => console.error('Error al obtener los datos:', error));
}

// ── Retirar del equipo ──────────────────────────────────────────────────────
function abrirRetirarUsuario(fila) {
    usuarioActual = fila;
    $('#nombreUsuarioEliminar').text(fila.nombreusuario);
    new bootstrap.Modal(document.getElementById('modalEliminarUsuario')).show();
}

$(document).ready(function () {

    // Los botones de la fila los pone createTable y llaman directamente a
    // abrirEditarUsuario / abrirRetirarUsuario con la fila ya resuelta, asi que aqui
    // ya no hacen falta handlers delegados sobre el <tbody>.

    $('#formEditarUsuario').on('submit', async function (e) {
        e.preventDefault();

        const dto = {
            Id: usuarioActual.usuarioid,
            NombreUsuario: $('#editNombreUsuario').val().trim(),
            Nombre: $('#editNombre').val().trim(),
            Email: $('#editEmail').val().trim(),
            Rol: $('#editRol').val().trim()
        };
        if (!dto.Nombre || !dto.Email || !dto.Rol) {
            toastr.warning('Todos los campos son obligatorios.');
            return;
        }

        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(dto.Email)) {
            toastr.error('Correo electrónico no válido.');
            return;
        }

        // FormData en vez de JSON: la accion no lleva [FromBody], asi que con
        // application/json el DTO llegaba vacio y siempre respondia "Datos invalidos".
        // Ademas por aqui viaja el token antiforgery.
        const formData = new FormData();
        formData.append('Id', dto.Id);
        formData.append('NombreUsuario', dto.NombreUsuario);
        formData.append('Nombre', dto.Nombre);
        formData.append('Email', dto.Email);
        formData.append('Rol', dto.Rol);
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        try {
            const response = await fetch('/Equipo/Editar', {
                method: 'POST',
                body: formData
            });

            const result = await response.json();

            if (response.ok && result.success) {
                bootstrap.Modal.getInstance(document.getElementById('modalEditarUsuario')).hide();
                toastr.success('Usuario actualizado correctamente');
                tabla1.reload();
            } else {
                toastr.error(result.message || 'Error al guardar los cambios.');
            }

        } catch (error) {
            console.error(error);
            toastr.error('Error al comunicarse con el servidor.');
        }
    });

    $('#btnGuardarCategoriasUsuario').on('click', async function () {
        if (!usuarioActual || !usuarioActual.usuarioid) {
            toastr.warning('Usuario no válido.');
            return;
        }

        const formData = new FormData();
        formData.append('idUsuario', usuarioActual.usuarioid);

        // Se mandan sólo las habilitadas. Las deshabilitadas son de categorías que
        // este administrador no maneja: el servidor las deja como estaban, no las
        // interpreta como "quítamelas".
        document.querySelectorAll('#listaCategoriasUsuario .cat-usuario:not(:disabled)')
            .forEach(chk => { if (chk.checked) formData.append('categorias', chk.value); });

        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        try {
            const response = await fetch('/Equipo/GuardarCategorias', { method: 'POST', body: formData });
            const result = await response.json();

            if (response.ok && result.success) {
                bootstrap.Modal.getInstance(document.getElementById('modalCategoriasUsuario')).hide();
                toastr.success(result.message || 'Categorías actualizadas.');
                tabla1.reload();
            } else {
                toastr.error(result.message || 'No se pudieron guardar las categorías.');
            }
        } catch (error) {
            console.error(error);
            toastr.error('Error al comunicarse con el servidor.');
        }
    });

    $('#btnConfirmarEliminar').on('click', async function () {
        if (!usuarioActual || !usuarioActual.usuarioid) {
            toastr.warning('Usuario no válido.');
            return;
        }

        // Se manda el Id: el backend identifica al usuario por id, no por nombre.
        const formData = new FormData();
        formData.append('Id', usuarioActual.usuarioid);
        formData.append('NombreUsuario', usuarioActual.nombreusuario);
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (token) formData.append('__RequestVerificationToken', token);

        try {
            const response = await fetch('/Equipo/Eliminar', {
                method: 'POST',
                body: formData
            });

            const result = await response.json();

            if (response.ok && result.success) {
                bootstrap.Modal.getInstance(document.getElementById('modalEliminarUsuario')).hide();
                toastr.success('Usuario retirado del equipo de soporte');
                tabla1.reload();
            } else {
                toastr.error(result.message || 'No se pudo retirar al usuario.');
            }

        } catch (error) {
            console.error(error);
            toastr.error('Error al comunicarse con el servidor.');
        }
    });
});
