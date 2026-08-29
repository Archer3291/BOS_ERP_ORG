-- ============================================================================
--  Punto de Venta · Cuentas bancarias por forma de pago — PRODUCCIÓN (srs_prod)
--
--  Equivale al estado FINAL de sql/pos_formas_pago_bancos.sql, no a su historia.
--  Aquel archivo nació con la PK como `serial` y trae el bloque que la convierte a
--  INT4 IDENTITY porque en `srs` ya había filas capturadas. Aquí no hace falta:
--  en producción no existe nada todavía, así que la columna nace directamente
--  IDENTITY y este script sólo CREA.
--
--  QUÉ INSTALA
--    pos_formas_pago_bancos          qué cuentas de banco puede usar cada forma de
--                                    pago del punto de venta
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
--  QUÉ NO HACE FALTA DESPLEGAR ANTES
--  El código convive con esta migración sin correrla: detecta la tabla y la
--  columna con Utilities.ExisteColumna / information_schema. Mientras no se
--  aplique, el POS se comporta exactamente como antes.
--
--  TODO VA CALIFICADO CON srs_prod.
--  A propósito, y no por estilo: en el despliegue a `srs` de otro módulo un ALTER
--  TABLE quedó fuera porque se ejecutaron fragmentos sueltos sin arrastrar el SET
--  de la primera línea, y la tabla terminó a medio migrar. Con el esquema escrito
--  en cada objeto, ejecutar de a pedazos deja de ser peligroso.
--
--  CÓMO CORRERLO
--  Va todo en una transacción y TERMINA EN ROLLBACK a propósito. Córrelo una vez,
--  revisa el bloque de verificación del final y, si cuadra, cambia el ROLLBACK por
--  COMMIT y vuelve a correrlo. Es idempotente: pasar dos veces no rompe ni duplica.
--
--  DESPUÉS DE APLICARLO
--  La tabla nace vacía y eso es seguro: sin configuración el POS ofrece el
--  catálogo completo, igual que hoy. Hay que entrar a la pantalla y asignar las
--  cuentas de cada forma de pago para que el filtro empiece a aplicar.
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
    IF to_regclass('srs_prod.catsucursales')        IS NULL THEN v_faltan := v_faltan || ' catsucursales';        END IF;
    IF to_regclass('srs_prod.cat_f_pago')           IS NULL THEN v_faltan := v_faltan || ' cat_f_pago';           END IF;
    IF to_regclass('srs_prod.catbancos')            IS NULL THEN v_faltan := v_faltan || ' catbancos';            END IF;
    IF to_regclass('srs_prod.cuentas_finanzas')     IS NULL THEN v_faltan := v_faltan || ' cuentas_finanzas';     END IF;
    IF to_regclass('srs_prod.factura_formas_pagos') IS NULL THEN v_faltan := v_faltan || ' factura_formas_pagos'; END IF;

    IF v_faltan <> '' THEN
        RAISE EXCEPTION
            'ABORTADO: faltan tablas en srs_prod:%. ¿Es el esquema correcto?', v_faltan;
    END IF;
END $$;

-- Un CREATE TABLE sin calificar aterriza en `public`, que va PRIMERO en el
-- search_path de la aplicación y taparía a la de srs_prod. Si eso ya pasó, mejor
-- detenerse y decirlo que crear una segunda y quedarse leyendo la equivocada.
DO $$
BEGIN
    IF to_regclass('public.pos_formas_pago_bancos') IS NOT NULL THEN
        RAISE EXCEPTION
            'ABORTADO: existe public.pos_formas_pago_bancos y taparía a la de srs_prod. '
            'Muévela con: ALTER TABLE public.pos_formas_pago_bancos SET SCHEMA srs_prod; '
            'y vuelve a correr este script.';
    END IF;
END $$;


-- ── 1. La tabla ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS srs_prod.pos_formas_pago_bancos (
    id_pos_forma_pago_banco integer     GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    empresa_id              integer     NOT NULL,
    -- NULL = la configuración aplica a todas las sucursales de la empresa.
    sucursal_id             integer     NULL REFERENCES srs_prod.catsucursales (id_sucursal) ON DELETE CASCADE,
    f_pago_id               integer     NOT NULL REFERENCES srs_prod.cat_f_pago (id_f_pago),
    banco_id                integer     NOT NULL REFERENCES srs_prod.catbancos (id_catbanco) ON DELETE CASCADE,
    -- La que el POS preselecciona cuando hay varias cuentas para la misma forma de pago.
    predeterminada          boolean     NOT NULL DEFAULT false,
    creada_por              integer     NULL,
    fecha_creacion          timestamp   NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE srs_prod.pos_formas_pago_bancos IS
    'Cuentas de banco (catbancos) que puede usar cada forma de pago del punto de venta. '
    'Se administra en Ventas/FormasPagoBancos y la consume el POS al cobrar y al facturar.';

COMMENT ON COLUMN srs_prod.pos_formas_pago_bancos.sucursal_id IS
    'NULL = aplica a todas las sucursales. Una fila con sucursal concreta gana sobre la de NULL.';


-- ── 2. Restricciones e índices ──────────────────────────────────────────────
-- Una cuenta no se puede asignar dos veces a la misma forma de pago en el mismo
-- alcance. Se indexa sobre COALESCE porque en Postgres dos NULL no chocan en un
-- UNIQUE normal y se podrían duplicar las filas de "todas las sucursales".
CREATE UNIQUE INDEX IF NOT EXISTS ux_pos_fpb_asignacion
    ON srs_prod.pos_formas_pago_bancos (empresa_id, COALESCE(sucursal_id, 0), f_pago_id, banco_id);

-- A lo mucho una predeterminada por forma de pago y alcance.
CREATE UNIQUE INDEX IF NOT EXISTS ux_pos_fpb_predeterminada
    ON srs_prod.pos_formas_pago_bancos (empresa_id, COALESCE(sucursal_id, 0), f_pago_id)
    WHERE predeterminada;

-- Lectura del POS: siempre por empresa + sucursal.
CREATE INDEX IF NOT EXISTS ix_pos_fpb_lectura
    ON srs_prod.pos_formas_pago_bancos (empresa_id, sucursal_id, f_pago_id);


-- ── 3. Cuenta elegida en el cobro ───────────────────────────────────────────
-- El desglose de formas de pago de la venta guarda a qué cuenta entró cada
-- importe. Con esto el modal de facturación puede preseleccionar la cuenta que el
-- cajero ya había elegido al cobrar, y el corte sabe en qué banco quedó cada peso.
--
-- Es NULLable y sin default: las ventas ya registradas se quedan con NULL, que el
-- código interpreta como "no se eligió cuenta al cobrar" y resuelve al facturar.
--
-- Sobre el bloqueo: agregar una columna NULL sin default no reescribe la tabla,
-- pero la llave foránea sí obliga a un ACCESS EXCLUSIVE mientras Postgres valida.
-- Como todas las filas existentes quedan en NULL la validación pasa de largo; aun
-- así, si factura_formas_pagos resultara enorme, córrelo fuera del horario de caja.
ALTER TABLE srs_prod.factura_formas_pagos
    ADD COLUMN IF NOT EXISTS banco_id integer NULL REFERENCES srs_prod.catbancos (id_catbanco);

COMMENT ON COLUMN srs_prod.factura_formas_pagos.banco_id IS
    'Cuenta de banco (catbancos) elegida en el POS para este cobro, según la '
    'configuración de pos_formas_pago_bancos.';


-- ── 4. Verificación ─────────────────────────────────────────────────────────
-- Se espera:
--   identidad     data_type = integer, is_identity = YES, generation = ALWAYS
--   columna cobro banco_id presente en factura_formas_pagos
--   formas POS    las 6 claves que el punto de venta sabe cobrar, presentes en
--                 cat_f_pago; si falta alguna, esa forma de pago no aparecerá en
--                 la pantalla de configuración

SELECT 'identidad' AS chequeo,
       data_type,
       is_identity,
       identity_generation,
       column_default
FROM   information_schema.columns
WHERE  table_schema = 'srs_prod'
  AND  table_name   = 'pos_formas_pago_bancos'
  AND  column_name  = 'id_pos_forma_pago_banco';

SELECT 'columna del cobro' AS chequeo,
       COUNT(*)            AS existe
FROM   information_schema.columns
WHERE  table_schema = 'srs_prod'
  AND  table_name   = 'factura_formas_pagos'
  AND  column_name  = 'banco_id';

-- Las 6 claves SAT que el POS sabe cobrar (espejo de FormasPagoPos en
-- PVAdminFormasPagoBancos.cs y de paymentMethodsData en PuntoDeVenta.cshtml).
SELECT 'formas de pago del POS' AS chequeo,
       c.cve_sat,
       f.id_f_pago,
       COALESCE(f.descripcion, '*** NO EXISTE EN cat_f_pago ***') AS descripcion
FROM   (VALUES ('01'), ('02'), ('03'), ('04'), ('28'), ('99')) AS c(cve_sat)
LEFT   JOIN srs_prod.cat_f_pago f ON f.cve_sat::text = c.cve_sat
ORDER  BY c.cve_sat;

-- Cambia por COMMIT cuando la verificación cuadre.
ROLLBACK;
