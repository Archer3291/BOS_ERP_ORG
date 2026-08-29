(function () {
    var canvas = document.getElementById('canvas-bg');
    var ctx = canvas.getContext('2d');

    function resize() {
        canvas.width = window.innerWidth;
        canvas.height = window.innerHeight;
    }
    resize();
    window.addEventListener('resize', resize);

    var pts = [];
    for (var i = 0; i < 60; i++) {
        pts.push({
            x: Math.random() * window.innerWidth,
            y: Math.random() * window.innerHeight,
            vx: (Math.random() - 0.5) * 0.30,
            vy: (Math.random() - 0.5) * 0.30,
            r: Math.random() * 1.5 + 0.5,
            teal: Math.random() > 0.45
        });
    }

    function draw() {
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        var g = ctx.createRadialGradient(
            canvas.width * 0.5, canvas.height * 0.45, 0,
            canvas.width * 0.5, canvas.height * 0.45, canvas.width * 0.75
        );
        g.addColorStop(0, '#0d1829');
        g.addColorStop(1, '#070c17');
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        for (var i = 0; i < pts.length; i++) {
            var p = pts[i];
            p.x += p.vx; p.y += p.vy;
            if (p.x < 0 || p.x > canvas.width) p.vx *= -1;
            if (p.y < 0 || p.y > canvas.height) p.vy *= -1;
            ctx.beginPath();
            ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
            ctx.fillStyle = p.teal
                ? 'rgba(29,158,117,0.62)'
                : 'rgba(47,111,237,0.52)';
            ctx.fill();
        }

        for (var i = 0; i < pts.length; i++) {
            for (var j = i + 1; j < pts.length; j++) {
                var dx = pts[i].x - pts[j].x;
                var dy = pts[i].y - pts[j].y;
                var d = Math.sqrt(dx * dx + dy * dy);
                if (d < 120) {
                    var a = 0.15 * (1 - d / 120);
                    ctx.beginPath();
                    ctx.moveTo(pts[i].x, pts[i].y);
                    ctx.lineTo(pts[j].x, pts[j].y);
                    ctx.strokeStyle = pts[i].teal
                        ? 'rgba(29,158,117,' + a + ')'
                        : 'rgba(47,111,237,' + a + ')';
                    ctx.lineWidth = 0.5;
                    ctx.stroke();
                }
            }
        }

        requestAnimationFrame(draw);
    }
    draw();
})();

/* ── Toggle contraseñas ── */
function bindToggle(btnId, inputId, iconId) {
    var btn = document.getElementById(btnId);
    var input = document.getElementById(inputId);
    var icon = document.getElementById(iconId);
    if (!btn || !input) return;
    btn.addEventListener('click', function () {
        if (input.type === 'password') {
            input.type = 'text';
            icon.classList.replace('fa-eye', 'fa-eye-slash');
        } else {
            input.type = 'password';
            icon.classList.replace('fa-eye-slash', 'fa-eye');
        }
    });
}
bindToggle('toggle-pass1', 'password-input', 'icon-pass1');
bindToggle('toggle-pass2', 'confirm-pass-input', 'icon-pass2');

$(document).ready(function () {

    /* ── Limpiar errores al escribir ── */
    $('input, select').on('input change', function () {
        $(this).closest('.field').find('.field-validation-error, .val-error').text('');
        if ($('.field-validation-error:visible, .val-error:visible').length === 0) {
            $('.validation-summary-errors').hide();
        }
    });

    /* ── AJAX: cargar sucursales al cambiar empresa ── */
    $('#EmpresaId').on('change', function () {
        var empresaId = $(this).val();
        var $suc = $('#Id_sucursal');

        $suc.empty().append('<option value="">Selecciona una opción</option>');
        if (!empresaId) return;

        $suc.prop('disabled', true);

        $.ajax({
            url: ajaxUrl,
            type: 'GET',
            data: { empresaId: empresaId },
            dataType: 'json',
            success: function (data) {
                $.each(data, function (i, s) {
                    $suc.append($('<option>').val(s.value).text(s.text));
                });
                $suc.prop('disabled', false);
            },
            error: function (xhr) {
                if (xhr.responseText && xhr.responseText.indexOf('<!DOCTYPE') > -1) {
                    alert('Sesión expirada. Por favor, recarga la página.');
                } else {
                    alert('Error al cargar las sucursales.');
                }
                $suc.prop('disabled', false);
            }
        });
    });

    /* ── Email del superior: agregar dominio al enviar ── */
    $('form').on('submit', function () {
        var val = $('#superior-email-input').val().trim();
        $('#SuperiorEmail').val(val ? val + '@sellosyretenes.com' : '');
    });


    $('#superior-email-input').on('input', function () {
        $(this).val($(this).val().replace(/@@/g, ''));
    });

    /* Restaurar prefijo si hubo error de validación */
    var hidden = $('#SuperiorEmail').val();
    if (hidden && hidden.indexOf('@sellosyretenes.com') > -1) {
        $('#superior-email-input').val(hidden.replace('@sellosyretenes.com', ''));
    }

});