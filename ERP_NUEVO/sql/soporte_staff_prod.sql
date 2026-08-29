-- ============================================================================
-- Soporte / Tickets: catálogo de roles y staff — PRODUCCIÓN (srs_prod)
--
-- Gemelo de sql/soporte_staff.sql; sólo cambia el esquema.
--
-- POR QUÉ ESTE SCRIPT
-- La autorización del módulo (helpers/SoporteAuthz.cs) reconoce como staff a quien
-- cumpla CUALQUIERA de estas dos condiciones:
--
--   1. Tener un rol del ERP (usuarios.rolid -> roles.nombre) de esta lista:
--        'Sistemas', 'Super Administrador', 'Resolvedor Tickets', 'Asignador Tickets'
--   2. Tener fila en tkt_usuario_rol con id_rol_tkt IN (1, 2)
--        (1 = Revisor, 2 = Asignador; 3 = Usuario es el usuario final)
--
-- Estado encontrado al revisar la base:
--
--   srs (desarrollo)  roles ERP -> Sistemas: 0, Super Administrador: 0,
--                                  Resolvedor Tickets: 0, Asignador Tickets: 0
--                     tkt_roles -> 3 filas (Revisor / Asignador / Usuario)
--                     tkt_usuario_rol -> 8 filas, TODAS id_rol_tkt = 3
--
--   srs_prod (prod)   roles ERP -> Sistemas: 4 usuarios (esos sí entran hoy)
--                     tkt_roles -> VACÍA  <- rompe la pestaña Equipo
--                     tkt_usuario_rol -> 47 filas, TODAS id_rol_tkt = 3
--
-- Problemas que resuelve:
--   * En desarrollo nadie puede entrar a Soporte > Administración.
--   * En producción tkt_roles vacía deja la pestaña Equipo sin filas, porque
--     UsuariosTodos hace INNER JOIN tkt_roles.
--   * El combo "Responsable" y la asignación masiva salen vacíos en ambos
--     ambientes: la consulta filtra por tur.id_rol_tkt = 1 y nadie lo tiene.
--   * tkt_usuario_rol no tenía UNIQUE(id_usr), pero EquipoController.Editar hace
--     UPDATE ... WHERE id_usr = @id_usr asumiendo una sola fila por usuario, y la
--     consulta de responsables (INNER JOIN) duplicaría al usuario en el combo.
--
-- NO TOCA LA TABLA usuarios. Los permisos del ERP de cada persona quedan igual.
--
-- CÓMO USARLO
--   Ejecutar el archivo completo contra la base de producción. NO se ha corrido.
--   Es idempotente: se puede volver a correr sin efectos secundarios.
--   El bloque 4 imprime la verificación; debe listar a los 4 usuarios de Sistemas
--   y devolver 0 filas en la consulta de duplicados.
-- ============================================================================

BEGIN;

SET search_path TO public, srs_prod;


-- ----------------------------------------------------------------------------
-- 1) CATÁLOGO DE ROLES DE TICKETS
--
-- id_rol_tkt no es identity ni tiene default, así que el id va explícito. Se usan
-- los mismos ids que en desarrollo porque el código los referencia por número
-- (la consulta de responsables filtra por id_rol_tkt = 1).
-- ----------------------------------------------------------------------------
INSERT INTO tkt_roles (id_rol_tkt, nombre) VALUES
    (1, 'Revisor'),
    (2, 'Asignador'),
    (3, 'Usuario')
ON CONFLICT (id_rol_tkt) DO NOTHING;


-- ----------------------------------------------------------------------------
-- 2) INTEGRIDAD DE tkt_usuario_rol
--
-- UNIQUE(id_usr): un usuario tiene un solo rol de tickets. Verificado que hoy no
-- hay duplicados en srs ni en srs_prod, así que no hace falta limpiar antes.
-- Si en producción hubiera aparecido alguno, este ALTER falla y hay que resolver
-- el duplicado primero (la consulta del bloque 4 los lista).
-- ----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'tkt_usuario_rol'::regclass   -- ::regclass resuelve por search_path
          AND conname = 'uq_tkt_usuario_rol_usr'
    ) THEN
        ALTER TABLE tkt_usuario_rol
            ADD CONSTRAINT uq_tkt_usuario_rol_usr UNIQUE (id_usr);
    END IF;
END $$;

-- FK al catálogo: impide dejar a alguien con un id_rol_tkt que no existe.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'tkt_usuario_rol'::regclass   -- ::regclass resuelve por search_path
          AND conname = 'fk_tkt_usuario_rol_rol'
    ) THEN
        ALTER TABLE tkt_usuario_rol
            ADD CONSTRAINT fk_tkt_usuario_rol_rol
            FOREIGN KEY (id_rol_tkt) REFERENCES tkt_roles(id_rol_tkt);
    END IF;
END $$;


-- ----------------------------------------------------------------------------
-- 3) DESIGNACIÓN DEL STAFF
--
-- Se identifica por nombreusuario para que el script sirva igual en los dos
-- ambientes, donde los usuarioid no coinciden.
--
--   Revisor (1)   = atiende y resuelve tickets
--   Asignador (2) = además reparte tickets entre el equipo
--
-- El UPSERT contempla a quien ya tenga fila con rol 3 (Usuario): se actualiza en
-- vez de insertar, que es justo lo que el UNIQUE del bloque 2 permite resolver.
-- ----------------------------------------------------------------------------

-- Producción: los 4 usuarios con rol ERP 'Sistemas'.
-- En producción 'Gsistemas' no existe: el IN simplemente no lo encuentra.
INSERT INTO tkt_usuario_rol (id_usr, id_rol_tkt)
SELECT u.usuarioid, 2                       -- Asignador: pueden atender y repartir
FROM usuarios u
WHERE u.nombreusuario IN (
        'EdgarM',
        'EMONTOYA',
        'EMONTOYA1',
        'EMONTOYAITF',
        'Gsistemas'          -- sólo existe en desarrollo; en prod no hace nada
      )
ON CONFLICT (id_usr) DO UPDATE
    SET id_rol_tkt = EXCLUDED.id_rol_tkt;


-- ----------------------------------------------------------------------------
-- 4) VERIFICACIÓN
-- ----------------------------------------------------------------------------

-- Debe devolver una fila por cada persona designada.
SELECT u.usuarioid,
       u.nombreusuario,
       r.nombre  AS rol_erp,
       tr.nombre AS rol_tickets
FROM usuarios u
JOIN tkt_usuario_rol tur ON tur.id_usr = u.usuarioid
JOIN tkt_roles tr        ON tr.id_rol_tkt = tur.id_rol_tkt
LEFT JOIN roles r        ON r.rolid = u.rolid
WHERE tur.id_rol_tkt IN (1, 2)
ORDER BY u.nombreusuario;

-- Debe devolver 0 filas (no debe quedar ningún usuario con más de un rol).
SELECT id_usr, COUNT(*)
FROM tkt_usuario_rol
GROUP BY id_usr
HAVING COUNT(*) > 1;

COMMIT;

-- ============================================================================
-- NOTA sobre el combo "Responsable"
-- Las consultas que llenan los selects de responsable filtraban por
-- "tur.id_rol_tkt = 1" (sólo Revisor), así que un Asignador no aparecía aunque
-- tuviera permiso para atender tickets. Ya se ampliaron a "IN (1, 2)", el mismo
-- criterio que usa SoporteAuthz, en:
--     SoporteController.DatosSelect y TicketT (responsableQuery)
--     CategoriaController.Datos
--     InformeController.DatosSelect
-- ============================================================================
