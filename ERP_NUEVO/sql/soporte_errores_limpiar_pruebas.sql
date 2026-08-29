-- ============================================================================
-- Soporte / Captura de errores: limpieza de los datos de prueba
--
-- PARA QUÉ
-- Deja las tablas de captura en cero antes de arrancar la semana de observación,
-- para que los números de esa semana signifiquen algo. Durante las pruebas de
-- las fases 1 a 5 quedaron en srs varias huellas y tickets sintéticos, más las
-- huellas huérfanas de cuando cambió el cálculo del origen.
--
-- ESTO BORRA TICKETS. Lee las guardas antes de correrlo.
--
-- ALCANCE
-- Sólo toca tickets de la categoría 'Errores del sistema'. Un ticket que haya
-- levantado una persona NO puede caer aquí ni por accidente: el DELETE está
-- acotado por id_cat en todas las sentencias.
--
-- CÓMO USARLO
--   Este archivo es el de DESARROLLO (srs). En producción no hace falta: allá
--   las tablas existen pero el código todavía no está desplegado, así que están
--   vacías. Si algún día se necesita, cambiar srs -> srs_prod y revisar el tope.
-- ============================================================================

BEGIN;

SET search_path TO public, srs;


-- ----------------------------------------------------------------------------
-- 0) PREVIA — qué se va a borrar
--
-- Correr el script completo muestra esto antes de tocar nada. Si el número no
-- se parece a lo esperado, cancelar la transacción (ROLLBACK) en vez de seguir.
-- ----------------------------------------------------------------------------
SELECT 'huellas'          AS que, COUNT(*) AS cuantas FROM srs.tkt_error_huella
UNION ALL
SELECT 'ocurrencias',     COUNT(*) FROM srs.tkt_error_detalle
UNION ALL
SELECT 'tickets automáticos', COUNT(*)
  FROM srs.tkts t
  JOIN srs.tkts_categorias c ON c.id_cat = t.id_cat
 WHERE c.n_cat = 'Errores del sistema';


-- ----------------------------------------------------------------------------
-- 1) GUARDA DE VOLUMEN
--
-- Este script se escribió para borrar un puñado de tickets de prueba. Si se
-- corriera meses después, cuando la captura lleve cientos de tickets legítimos,
-- borraría el histórico entero del módulo. El tope obliga a que eso sea una
-- decisión consciente y no un descuido.
-- ----------------------------------------------------------------------------
DO $$
DECLARE
    v_tickets integer;
    v_tope    integer := 50;   -- súbelo A MANO si de verdad quieres borrar más
BEGIN
    SELECT COUNT(*) INTO v_tickets
    FROM srs.tkts t
    JOIN srs.tkts_categorias c ON c.id_cat = t.id_cat
    WHERE c.n_cat = 'Errores del sistema';

    IF v_tickets > v_tope THEN
        RAISE EXCEPTION
            'ABORTADO: hay % tickets automáticos y el tope de seguridad es %. '
            'Si de verdad quieres borrarlos todos, sube v_tope en este script. '
            'Si no, probablemente ya no estés limpiando pruebas.', v_tickets, v_tope;
    END IF;

    RAISE NOTICE 'Se borrarán % tickets automáticos.', v_tickets;
END $$;


-- ----------------------------------------------------------------------------
-- 2) TELEMETRÍA
--
-- El detalle se va solo por el ON DELETE CASCADE de tkt_error_detalle, pero se
-- borra explícito para que el orden quede a la vista y no dependa de recordar
-- cómo está declarada la FK.
-- ----------------------------------------------------------------------------
DELETE FROM srs.tkt_error_detalle;
DELETE FROM srs.tkt_error_huella;


-- ----------------------------------------------------------------------------
-- 3) TICKETS AUTOMÁTICOS Y SUS DEPENDIENTES
--
-- Las hijas van primero. No se asume que ninguna FK tenga CASCADE: si alguna lo
-- tiene, borrar antes no hace daño; si no lo tiene y no se borra, el DELETE de
-- tkts falla por violación de llave foránea.
--
-- tkt_error_huella.id_tkt es ON DELETE SET NULL, pero la tabla ya quedó vacía
-- en el paso anterior.
-- ----------------------------------------------------------------------------
CREATE TEMP TABLE tmp_tkts_auto ON COMMIT DROP AS
SELECT t.id_tkts
FROM srs.tkts t
JOIN srs.tkts_categorias c ON c.id_cat = t.id_cat
WHERE c.n_cat = 'Errores del sistema';

DELETE FROM srs.tkts_adj  WHERE id_tkts IN (SELECT id_tkts FROM tmp_tkts_auto);
DELETE FROM srs.seg_tkts  WHERE id_tkt  IN (SELECT id_tkts FROM tmp_tkts_auto);
DELETE FROM srs.hst_est   WHERE id_tkts IN (SELECT id_tkts FROM tmp_tkts_auto);
DELETE FROM srs.tkt_asig  WHERE id_tkt  IN (SELECT id_tkts FROM tmp_tkts_auto);
DELETE FROM srs.tkts      WHERE id_tkts IN (SELECT id_tkts FROM tmp_tkts_auto);


-- ----------------------------------------------------------------------------
-- 4) VERIFICACIÓN — todo debe dar 0
--
-- La CATEGORÍA no se borra: la necesita ErrorTicketService para el próximo
-- error, y volver a crearla obligaría a correr otra vez el script de instalación.
-- ----------------------------------------------------------------------------
SELECT 'huellas'          AS que, COUNT(*) AS quedan FROM srs.tkt_error_huella
UNION ALL
SELECT 'ocurrencias',     COUNT(*) FROM srs.tkt_error_detalle
UNION ALL
SELECT 'tickets automáticos', COUNT(*)
  FROM srs.tkts t
  JOIN srs.tkts_categorias c ON c.id_cat = t.id_cat
 WHERE c.n_cat = 'Errores del sistema';

-- La categoría sigue ahí, que es lo correcto.
SELECT id_cat, n_cat, responsable, id_prio, activo
FROM srs.tkts_categorias
WHERE n_cat = 'Errores del sistema';

COMMIT;
