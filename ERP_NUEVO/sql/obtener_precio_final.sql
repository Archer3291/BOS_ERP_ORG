-- ═══════════════════════════════════════════════════════════════════════════
--  obtener_precio_final — motor de precios de ventas
-- ═══════════════════════════════════════════════════════════════════════════
--  Cambios respecto a la versión en producción:
--
--  1. ORDER BY rp.prioridad ASC  (antes DESC)
--     El número menor gana, que es lo que la UI le promete al usuario:
--     ReglasPrecioController.Listar ordena ASC y el motor de conflictos dice
--     literalmente "La de mayor prioridad (número menor) ganará". Con DESC se
--     aplicaba la regla contraria a la configurada cuando dos reglas empataban
--     en especificidad, sin ningún error visible.
--
--  2. Caso 'BASE' → precio_minimo = 0
--     precio_minimo es el PISO permitido, no el valor con el que arranca el
--     input. Devolver el precio de lista como piso dejaba clavado el precio de
--     cualquier producto sin regla. Ahora 0 = "sin piso": precio libre, y la
--     única restricción es que no sea negativo (se valida en el controlador).
--     El valor con el que arranca el input sigue viajando en precio_final.
--
--  Contrato de las columnas (sin cambios en el resto de las ramas):
--     precio_final    → valor inicial del input de precio
--     precio_minimo   → piso permitido; 0 = sin piso
--     descuento_maximo→ tope de descuento; 0 = no se permite descuento
--                       (PRECIO_FIJO); solo NULL significa "sin configurar"
--
--  PENDIENTE (no incluido aquí a propósito):
--     · COALESCE(pvN, pv1) en el CASE de lista de precios — hoy un cliente con
--       cod_ant='05' y pv5 NULL cotiza en 0.
--     · La rama SIN_PRODUCTO devuelve aplicar_automatico = true con precio 0,
--       lo que bloquea el guardado con el mensaje "fija el precio en $0.00".
--     · gpo y tp se guardan como id en reglas_precio pero catproductos guarda
--       cve_grupo / cve_tipo, así que esas reglas nunca hacen match.
-- ═══════════════════════════════════════════════════════════════════════════

CREATE OR REPLACE FUNCTION obtener_precio_final(p_empresa_id integer, p_cliente_id integer, p_cve_prod character varying)
 RETURNS TABLE(precio_final numeric, precio_minimo numeric, descuento_sugerido numeric, descuento_maximo numeric, aplicar_automatico boolean, tipo_regla character varying)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_precio_base  numeric;
    v_cod_ant      varchar;
    v_producto_id  int;
    v_lin_prod     varchar;
    v_gpo          varchar;
    v_tp           varchar;
    r              RECORD;
    v_descuento    numeric := 0;
BEGIN
    SELECT cod_ant INTO v_cod_ant
    FROM catclientes
    WHERE id_cliente = p_cliente_id AND empresa_id = p_empresa_id;

    SELECT id_catproductos,
           CASE v_cod_ant
               WHEN '01' THEN pv1  WHEN '02' THEN pv2  WHEN '03' THEN pv3
               WHEN '04' THEN pv4  WHEN '05' THEN pv5  WHEN '06' THEN pv6
               WHEN '07' THEN pv7  WHEN '08' THEN pv8  ELSE pv1
           END,
           lin_prod, gpo, tp
    INTO v_producto_id, v_precio_base, v_lin_prod, v_gpo, v_tp
    FROM catproductos
    WHERE cve_prod = p_cve_prod AND empresa_id = p_empresa_id;

    IF v_producto_id IS NULL THEN
        RETURN QUERY SELECT 0::numeric,0::numeric,0::numeric,0::numeric,true,'SIN_PRODUCTO'::varchar(20);
        RETURN;
    END IF;

    SELECT * INTO r
    FROM reglas_precio rp
    WHERE rp.empresa_id  = p_empresa_id
      AND rp.activo      = true
      AND (rp.fecha_inicio IS NULL OR rp.fecha_inicio <= NOW())
      AND (rp.fecha_fin    IS NULL OR rp.fecha_fin    >= NOW())
      AND (rp.cliente_id   IS NULL OR rp.cliente_id   = p_cliente_id)
      AND (rp.producto_id  IS NULL OR rp.producto_id  = v_producto_id)
      AND (rp.lin_prod     IS NULL OR rp.lin_prod     = v_lin_prod)
      AND (rp.gpo          IS NULL OR rp.gpo          = v_gpo)
      AND (rp.tp           IS NULL OR rp.tp           = v_tp)
    ORDER BY
        -- 1. Más criterios activos gana (especificidad)
        (CASE WHEN rp.cliente_id  IS NOT NULL THEN 4 ELSE 0 END +
         CASE WHEN rp.producto_id IS NOT NULL THEN 3 ELSE 0 END +
         CASE WHEN rp.lin_prod    IS NOT NULL THEN 2 ELSE 0 END +
         CASE WHEN rp.gpo         IS NOT NULL THEN 2 ELSE 0 END +
         CASE WHEN rp.tp          IS NOT NULL THEN 2 ELSE 0 END) DESC,
        -- 2. Menor número de prioridad gana (1 gana sobre 10), igual que la UI
        rp.prioridad ASC,
        -- 3. Desempate: la más reciente
        rp.id DESC
    LIMIT 1;

    IF FOUND THEN
        IF r.tipo_regla = 'PRECIO_FIJO' THEN
            IF r.aplicar_automatico THEN
                RETURN QUERY SELECT r.valor,r.valor,0::numeric,0::numeric,true,r.tipo_regla;
            ELSE
                RETURN QUERY SELECT v_precio_base,r.valor,0::numeric,0::numeric,false,r.tipo_regla;
            END IF;
            RETURN;
        END IF;

        IF r.tipo_regla = 'DESCUENTO' THEN
            v_descuento := r.valor;
            IF r.max_descuento IS NOT NULL THEN
                v_descuento := LEAST(v_descuento, r.max_descuento);
            END IF;
            IF r.aplicar_automatico THEN
                RETURN QUERY SELECT v_precio_base,v_precio_base,v_descuento,
                    COALESCE(r.max_descuento,v_descuento),true,r.tipo_regla;
            ELSE
                RETURN QUERY SELECT v_precio_base,v_precio_base,0::numeric,
                    COALESCE(r.max_descuento,v_descuento),false,r.tipo_regla;
            END IF;
            RETURN;
        END IF;
    END IF;

    -- Sin regla: el precio de lista es solo el valor inicial, no un piso.
    RETURN QUERY SELECT v_precio_base,0::numeric,0::numeric,100::numeric,false,'BASE'::varchar(20);
END;
$function$
;
