document.getElementById('verTicketForm').addEventListener('submit', async function (e) {
    e.preventDefault();

    const form = e.target;
    const formData = new FormData(form);

    // Opcional: recordar email usando localStorage
    if (document.getElementById('recordarEmail').checked) {
        localStorage.setItem('emailRecordado', form.email.value);
    } else {
        localStorage.removeItem('emailRecordado');
    }

    try {
        const response = await fetch('/BuscarTickets/Buscar', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) throw new Error('Error HTTP: ' + response.status);

        const data = await response.json();

        if (data.success) {
            // Redirigir, mostrar ticket o abrir modal, según tu lógica
            toastr.success('Ticket encontrado.');
            console.log('Detalles del ticket:', data.ticket); // Por ejemplo
            window.location.href = data.redirectUrl;
        } else {
            toastr.error(data.error || 'No se pudo encontrar el ticket.');
        }
    } catch (error) {
        toastr.error('Error de conexión: ' + error.message);
    }
});

// Al cargar, si existe email guardado, rellenar
window.addEventListener('DOMContentLoaded', () => {
    const emailGuardado = localStorage.getItem('emailRecordado');
    if (emailGuardado) {
        document.getElementById('email').value = emailGuardado;
        document.getElementById('recordarEmail').checked = true;
    }
});
