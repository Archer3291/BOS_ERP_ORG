-- ============================================================================
--  Punto de Venta · Cuentas bancarias por forma de pago — esquema srs
--
--  CONSOLIDADO. Lleva el esquema a su forma FINAL venga del estado que venga:
--  sirve igual en una base donde no existe nada y en una donde ya se corrió la
--  primera versión de este script (la que creaba la PK como `serial`). En ese
--  segundo caso convierte la columna a INT4 IDENTITY sin tocar las filas que ya
--  estén capturadas.
--
--  Es el gemelo de sql/pos_formas_pago_bancos_prod.sql; sólo cambia el esquema.
--
--  QUÉ INSTALA
--    pos_formas_pago_bancos      qué cuentas de banco puede usar cada forma de
--                                pago del punto de venta
--    factura_formas_pagos.banco_id   la cuenta que el cajero eligió al cobrar
--
--  QUÉ RESUELVE
--  El select "Cuenta Bancaria" del modal de facturación del POS estaba escrito a
--  mano en PuntoDeVenta.cshtml (una lista fija de códigos contables 1-1-02-*) y el
--  cierre de caja pedía la cuenta contra el catálogo contable completo. Nada
--  amarraba una forma de pago a la cuenta donde de verdad entra ese dinero: el
--  cajero podía elegir cualquiera. Ahora se configura en
--  Ventas > Punto de Venta Administrativo > Formas de Pago y Bancos, y el POS sólo
--  ofrece lo configurado.
--
--  RESOLUCIÓN POR SUCURSAL (la implementa PVAdminFormasPagoBancos.cs)
--    1. Las filas de la sucursal de la sesión.
--    2. Si esa forma de pago no tiene ninguna, las filas con sucursal_id NULL
--       (configuración "Todas las sucursales").
--    3. Si tampoco hay, el POS cae al catálogo completo de bancos para no frenar
--       la caja mientras se termina de configurar.
--
--  TODO VA CALIFICADO CON 
--  A propósito, y no por estilo: la aplicación conecta con `Search Path=public,srs`,
--  así que un CREATE TABLE sin calificar aterriza en `public` y TAPA al de `srs`.
--  Además, ejecutar fragmentos sueltos con el esquema escrito en cada sentencia
--  deja de ser peligroso: cada una sabe sola dónde vive.
--
--  CÓMO CORRERLO
--  Va todo en una transacción y TERMINA EN ROLLBACK a propósito. Córrelo una vez,
--  revisa los NOTICE y el bloque de verificación del final y, si cuadra, cambia el
--  ROLLBACK por COMMIT y vuelve a correrlo. Es idempotente: pasar dos veces por lo
--  ya aplicado no rompe ni duplica nada.
-- ============================================================================

BEGIN;

-- ── 0. Red de seguridad ─────────────────────────────────────────────────────
-- Sin estas tablas el script fallaría a media creación, con unos objetos puestos
-- y otros no. Vale más abortar antes de tocar nada y con un mensaje que diga qué
-- falta, en vez de un error de llave foránea a mitad del camino.
DO $$
DECLARE
    v_faltan text := '';
BEGIN
    IF to_regclass('catsucursales')       IS NULL THEN v_faltan := v_faltan || ' catsucursales';       END IF;
    IF to_regclass('cat_f_pago')          IS NULL THEN v_faltan := v_faltan || ' cat_f_pago';          END IF;
    IF to_regclass('catbancos')           IS NULL THEN v_faltan := v_faltan || ' catbancos';           END IF;
    IF to_regclass('cuentas_finanzas')    IS NULL THEN v_faltan := v_faltan || ' cuentas_finanzas';    END IF;
    IF to_regclass('factura_formas_pagos') IS NULL THEN v_faltan := v_faltan || ' factura_formas_pagos'; END IF;

    IF v_faltan <> '' THEN
        RAISE EXCEPTION
            'ABORTADO: faltan tablas en srs:%. ¿Es el esquema correcto?', v_faltan;
    END IF;
END $$;

-- Si una corrida anterior dejó la tabla en `public` (por haberla creado sin
-- calificar), la aplicación estaría leyendo ESA y no la de `srs`, porque public va
-- primero en el search_path. Mejor detenerse y decirlo que crear una segunda.
DO $$
BEGIN
    IF to_regclass('public.pos_formas_pago_bancos') IS NOT NULL THEN
        RAISE EXCEPTION
            'ABORTADO: existe public.pos_formas_pago_bancos y taparía a la de  '
            'Muévela con: ALTER TABLE public.pos_formas_pago_bancos SET SCHEMA srs; '
            'y vuelve a correr este script.';
    END IF;
END $$;


-- ── 1. La tabla ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pos_formas_pago_bancos (
    id_pos_forma_pago_banco integer     GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    empresa_id              integer     NOT NULL,
    -- NULL = la configuración aplica a todas las sucursales de la empresa.
    sucursal_id             integer     NULL REFERENCES catsucursales (id_sucursal) ON DELETE CASCADE,
    f_pago_id               integer     NOT NULL REFERENCES cat_f_pago (id_f_pago),
    banco_id                integer     NOT NULL REFERENCES catbancos (id_catbanco) ON DELETE CASCADE,
    -- La que el POS preselecciona cuando hay varias cuentas para la misma forma de pago.
    predeterminada          boolean     NOT NULL DEFAULT false,
    creada_por              integer     NULL,
    fecha_creacion          timestamp   NOT NULL DEFAULT NOW()
);


-- ── 2. serial → INT4 IDENTITY ───────────────────────────────────────────────
-- La primera versión de este script declaraba la PK como `serial`. Aquí se
-- convierte a IDENTITY conservando las filas ya capturadas y su id.
--
-- El detalle que importa: si la secuencia del serial sobrevive a la conversión,
-- queda huérfana con dependencia 'a' sobre la columna, y a partir de ahí
-- pg_get_serial_sequence devuelve ESA y no la que la identity usa de verdad
-- (dependencia 'i'). Ya nos costó una tanda de llaves duplicadas en
-- corte_piezas — ver sql/corte_piezas_resync_identity.sql. Por eso se borra.
DO $$
DECLARE
    v_seq  regclass;
    v_next bigint;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM   information_schema.columns
        WHERE  table_schema = 'srs'
          AND  table_name   = 'pos_formas_pago_bancos'
          AND  column_name  = 'id_pos_forma_pago_banco'
          AND  is_identity  = 'NO'
    ) THEN
        RAISE NOTICE 'pos_formas_pago_bancos.id_pos_forma_pago_banco ya es IDENTITY: nada que convertir.';
        RETURN;
    END IF;

    -- Se captura ANTES de quitar el default, mientras la dependencia del serial
    -- sigue siendo la única.
    v_seq := pg_get_serial_sequence('pos_formas_pago_bancos', 'id_pos_forma_pago_banco');

    SELECT COALESCE(MAX(id_pos_forma_pago_banco), 0) + 1
      INTO v_next
      FROM pos_formas_pago_bancos;

    ALTER TABLE pos_formas_pago_bancos
        ALTER COLUMN id_pos_forma_pago_banco DROP DEFAULT;

    IF v_seq IS NOT NULL THEN
        EXECUTE format('ALTER SEQUENCE %s OWNED BY NONE', v_seq);
        EXECUTE format('DROP SEQUENCE %s', v_seq);
    END IF;

    ALTER TABLE pos_formas_pago_bancos
        ALTER COLUMN id_pos_forma_pago_banco ADD GENERATED ALWAYS AS IDENTITY;

    -- Los registros que ya existían conservan su id; la identity arranca después
    -- del mayor, no en 1.
    EXECUTE format(
        'ALTER TABLE pos_formas_pago_bancos ALTER COLUMN id_pos_forma_pago_banco RESTART WITH %s',
        v_next);

    RAISE NOTICE 'Convertida a IDENTITY (secuencia vieja % eliminada); siguiente id = %', v_seq, v_next;
END $$;


-- ── 3. Restricciones e índices ──────────────────────────────────────────────
-- Una cuenta no se puede asignar dos veces a la misma forma de pago en el mismo
-- alcance. Se indexa sobre COALESCE porque en Postgres dos NULL no chocan en un
-- UNIQUE normal y se podrían duplicar las filas de "todas las sucursales".
CREATE UNIQUE INDEX IF NOT EXISTS ux_pos_fpb_asignacion
    ON pos_formas_pago_bancos (empresa_id, COALESCE(sucursal_id, 0), f_pago_id, banco_id);

-- A lo mucho una predeterminada por forma de pago y alcance.
CREATE UNIQUE INDEX IF NOT EXISTS ux_pos_fpb_predeterminada
    ON pos_formas_pago_bancos (empresa_id, COALESCE(sucursal_id, 0), f_pago_id)
    WHERE predeterminada;

-- Lectura del POS: siempre por empresa + sucursal.
CREATE INDEX IF NOT EXISTS ix_pos_fpb_lectura
    ON pos_formas_pago_bancos (empresa_id, sucursal_id, f_pago_id);

COMMENT ON TABLE pos_formas_pago_bancos IS
    'Cuentas de banco (catbancos) que puede usar cada forma de pago del punto de venta. '
    'Se administra en Ventas/FormasPagoBancos y la consume el POS al cobrar y al facturar.';

COMMENT ON COLUMN pos_formas_pago_bancos.sucursal_id IS
    'NULL = aplica a todas las sucursales. Una fila con sucursal concreta gana sobre la de NULL.';


-- ── 4. Cuenta elegida en el cobro ───────────────────────────────────────────
-- El desglose de formas de pago de la venta guarda a qué cuenta entró cada
-- importe. Con esto el modal de facturación puede preseleccionar la cuenta que el
-- cajero ya había elegido al cobrar, y el corte sabe en qué banco quedó cada peso.
--
-- La columna es opcional: mientras no se corra este script, el POS sigue cobrando
-- y facturando igual que antes (el código la detecta con Utilities.ExisteColumna).
ALTER TABLE factura_formas_pagos
    ADD COLUMN IF NOT EXISTS banco_id integer NULL REFERENCES catbancos (id_catbanco);

COMMENT ON COLUMN factura_formas_pagos.banco_id IS
    'Cuenta de banco (catbancos) elegida en el POS para este cobro, según la '
    'configuración de pos_formas_pago_bancos.';


-- ── 5. Verificación ─────────────────────────────────────────────────────────
-- Se espera:
--   identidad     data_type = integer, is_identity = YES, generation = ALWAYS
--   secuencias    exactamente 1 (la de la identity, deptype 'i')
--   columna cobro banco_id presente en factura_formas_pagos
--   filas         las que ya tuvieras capturadas, intactas

SELECT 'identidad' AS chequeo,
       data_type,
       is_identity,
       identity_generation,
       column_default
FROM   information_schema.columns
WHERE  table_schema = 'srs'
  AND  table_name   = 'pos_formas_pago_bancos'
  AND  column_name  = 'id_pos_forma_pago_banco';

SELECT 'secuencias ligadas' AS chequeo,
       COUNT(*)             AS total,
       string_agg(s.oid::regclass::text || ' (deptype ' || d.deptype || ')', ', ') AS detalle
FROM   pg_depend d
JOIN   pg_class s     ON s.oid = d.objid AND s.relkind = 'S'
JOIN   pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
WHERE  d.refobjid = 'pos_formas_pago_bancos'::regclass
  AND  a.attname  = 'id_pos_forma_pago_banco';

SELECT 'columna del cobro' AS chequeo,
       COUNT(*)            AS existe
FROM   information_schema.columns
WHERE  table_schema = 'srs'
  AND  table_name   = 'factura_formas_pagos'
  AND  column_name  = 'banco_id';

SELECT 'configuración actual' AS chequeo,
       f.cve_sat,
       f.descripcion          AS forma_pago,
       COALESCE(s.cve_sucursal, 'TODAS') AS sucursal,
       b.nombre               AS cuenta,
       b.cuenta_contable,
       p.predeterminada
FROM   pos_formas_pago_bancos p
JOIN   cat_f_pago f     ON f.id_f_pago   = p.f_pago_id
JOIN   catbancos  b     ON b.id_catbanco = p.banco_id
LEFT   JOIN catsucursales s ON s.id_sucursal = p.sucursal_id
ORDER  BY f.cve_sat, sucursal, b.nombre;

-- Cambia por COMMIT cuando la verificación cuadre.
ROLLBACK;
