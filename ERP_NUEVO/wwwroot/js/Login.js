(function () {
    var canvas = document.getElementById('canvas-bg');
    var ctx = canvas.getContext('2d');
    function resize() { canvas.width = window.innerWidth; canvas.height = window.innerHeight; }
    resize();
    window.addEventListener('resize', resize);

    var pts = [];
    for (var i = 0; i < 65; i++) {
        pts.push({
            x: Math.random() * window.innerWidth,
            y: Math.random() * window.innerHeight,
            vx: (Math.random() - 0.5) * 0.32,
            vy: (Math.random() - 0.5) * 0.32,
            r: Math.random() * 1.5 + 0.5,
            teal: Math.random() > 0.45
        });
    }

    function draw() {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        var g = ctx.createRadialGradient(canvas.width * .5, canvas.height * .5, 0, canvas.width * .5, canvas.height * .5, canvas.width * .75);
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
            ctx.fillStyle = p.teal ? 'rgba(29,158,117,0.65)' : 'rgba(47,111,237,0.55)';
            ctx.fill();
        }

        for (var i = 0; i < pts.length; i++) {
            for (var j = i + 1; j < pts.length; j++) {
                var dx = pts[i].x - pts[j].x, dy = pts[i].y - pts[j].y;
                var d = Math.sqrt(dx * dx + dy * dy);
                if (d < 125) {
                    var a = 0.16 * (1 - d / 125);
                    ctx.beginPath();
                    ctx.moveTo(pts[i].x, pts[i].y);
                    ctx.lineTo(pts[j].x, pts[j].y);
                    ctx.strokeStyle = pts[i].teal ? 'rgba(29,158,117,' + a + ')' : 'rgba(47,111,237,' + a + ')';
                    ctx.lineWidth = 0.5;
                    ctx.stroke();
                }
            }
        }
        requestAnimationFrame(draw);
    }
    draw();
})();

(function () {
    var input = document.getElementById('Contrasena');
    var btn = document.getElementById('toggle-pass-btn');
    var icon = document.getElementById('toggle-pass-icon');
    btn.addEventListener('click', function () {
        if (input.type === 'password') {
            input.type = 'text';
            icon.classList.replace('fa-eye', 'fa-eye-slash');
        } else {
            input.type = 'password';
            icon.classList.replace('fa-eye-slash', 'fa-eye');
        }
    });
})();

$(document).ready(function () {
    $('input').on('input focus', function () {
        $(this).closest('.field-group').find('.field-validation-error').text('');
        if ($('.field-validation-error:visible').length === 0) {
            $('.validation-summary-errors').hide();
        }
    });
});