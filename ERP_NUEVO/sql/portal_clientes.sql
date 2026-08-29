-- ============================================================================
-- Portal de clientes — credenciales de acceso
--
-- El cliente entra con clave de cliente + correo + contraseña. La identidad real
-- es id_cliente: la clave sola no basta porque cve_cli se repite entre empresas
-- (1,559 casos), y es (cve_cli, empresa_id) lo que sí es único.
--
-- El alta es de autoservicio pero acotada: sólo se puede activar un acceso si ese
-- correo YA está registrado en correos_cliente para ese cliente. Así, conocer una
-- clave de cliente no alcanza para ver facturas ajenas; hay que controlar además
-- un buzón que el ERP ya tenía dado de alta.
--
-- Correr una sola vez, contra el esquema 
-- ============================================================================

SET search_path TO srs;

BEGIN;

CREATE TABLE IF NOT EXISTS portal_clientes_acceso (
    id_acceso         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    cliente_id        integer      NOT NULL REFERENCES catclientes (id_cliente),
    -- Se guarda en minúsculas; el índice único de abajo depende de ello.
    correo            varchar(200) NOT NULL,

    -- BCrypt (BCrypt.Net-Next, ya es dependencia del proyecto). Nulo mientras el
    -- acceso está pendiente de activar: existe la solicitud pero aún no hay clave.
    password_hash     varchar(200),

    activo            boolean      NOT NULL DEFAULT false,

    -- Activación y restablecimiento comparten mecanismo: un token de un solo uso
    -- con caducidad. Se guarda el hash, no el token, para que una fuga de la tabla
    -- no permita activar cuentas.
    token_hash        varchar(200),
    token_expira      timestamp,
    token_proposito   varchar(20),          -- 'activacion' | 'restablecer'

    -- Freno a la fuerza bruta. El bloqueo es por cuenta y temporal.
    intentos_fallidos integer      NOT NULL DEFAULT 0,
    bloqueado_hasta   timestamp,

    ultimo_acceso     timestamp,
    fecha_creacion    timestamp    NOT NULL DEFAULT NOW(),

    CONSTRAINT portal_clientes_acceso_correo_ck
        CHECK (correo = LOWER(correo))
);

-- Un acceso por (cliente, correo): el mismo correo puede atender a varios clientes
-- —pasa en 482 casos, típicamente compradores que llevan varias cuentas— y cada uno
-- necesita su propia credencial.
CREATE UNIQUE INDEX IF NOT EXISTS portal_clientes_acceso_unq
    ON portal_clientes_acceso (cliente_id, correo);

-- El login busca por correo antes de resolver el cliente.
CREATE INDEX IF NOT EXISTS portal_clientes_acceso_correo_idx
    ON portal_clientes_acceso (correo);

-- Bitácora de accesos. Sirve para responder "¿quién descargó esta factura?" y para
-- detectar intentos repetidos contra una misma cuenta.
CREATE TABLE IF NOT EXISTS portal_clientes_bitacora (
    id_bitacora   integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cliente_id    integer,
    correo        varchar(200),
    evento        varchar(40)  NOT NULL,   -- login_ok, login_fallido, activacion, descarga, envio_correo…
    detalle       varchar(500),
    ip            varchar(45),
    fecha         timestamp    NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS portal_clientes_bitacora_fecha_idx
    ON portal_clientes_bitacora (fecha DESC);

CREATE INDEX IF NOT EXISTS portal_clientes_bitacora_cliente_idx
    ON portal_clientes_bitacora (cliente_id, fecha DESC);

COMMIT;

-- ── Verificación ────────────────────────────────────────────────────────────
-- SELECT COUNT(*) AS clientes_que_podrian_activar
-- FROM catclientes c
-- WHERE EXISTS (SELECT 1 FROM correos_cliente cc WHERE cc.cliente_id = c.id_cliente);
