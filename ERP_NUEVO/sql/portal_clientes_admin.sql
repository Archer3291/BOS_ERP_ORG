-- ============================================================================
-- Portal de clientes — administración desde Sistemas
--
-- Separa dos cosas que el diseño original había mezclado:
--
--   correos_cliente        → a dónde se ENVÍAN las facturas (distribución fiscal)
--   portal_clientes_acceso → quién puede ENTRAR a consultarlas
--
-- Usar la primera como llave de la segunda obligaba a un trato malo: para darle
-- portal a la contadora del cliente había que meterla en la lista de correos de
-- las facturas, y entonces empezaba a recibir todos los CFDI.
--
-- El autoservicio sigue exigiendo un correo ya registrado —ahí no interviene
-- ningún humano, así que la puerta debe ser estrecha—. Este módulo es la
-- excepción atendida: un administrador verifica la solicitud y da el acceso con
-- el correo que el cliente pida, sin tocar la distribución de facturas.
--
-- Correr después de sql/portal_clientes.sql.
-- ============================================================================

SET search_path TO srs;

BEGIN;

-- ── 1. Quién creó cada acceso ───────────────────────────────────────────────
-- Un acceso dado de alta a mano necesita responsable: es una excepción al
-- control automático y debe poder auditarse.
ALTER TABLE portal_clientes_acceso
    ADD COLUMN IF NOT EXISTS creado_por     integer,
    ADD COLUMN IF NOT EXISTS origen_alta    varchar(20) NOT NULL DEFAULT 'autoservicio',
    ADD COLUMN IF NOT EXISTS notas          varchar(300);

COMMENT ON COLUMN portal_clientes_acceso.origen_alta IS
    'autoservicio = el cliente se activó solo con un correo ya registrado; administrador = alta manual desde Sistemas.';

-- ── 2. Permiso del módulo ───────────────────────────────────────────────────
-- DoesUserHasRight ya concede todo a quien tenga 'sistemas' o 'super_admin',
-- así que este permiso sirve para delegarlo sin dar Sistemas completo.
INSERT INTO permisos (nombre, descripcion, es_modulo, modulo_padre)
SELECT 'portal_clientes_admin', 'Portal de clientes: administrar accesos', false,
       (SELECT id_permiso FROM permisos WHERE nombre = 'sistemas')
WHERE NOT EXISTS (SELECT 1 FROM permisos WHERE nombre = 'portal_clientes_admin');

COMMIT;

-- ── Verificación ────────────────────────────────────────────────────────────
-- SELECT nombre, descripcion, modulo_padre FROM srs.permisos WHERE nombre = 'portal_clientes_admin';
-- SELECT column_name FROM information_schema.columns
--  WHERE table_schema='srs' AND table_name='portal_clientes_acceso' ORDER BY ordinal_position;
