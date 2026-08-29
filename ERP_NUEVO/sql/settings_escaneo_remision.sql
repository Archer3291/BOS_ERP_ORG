-- ============================================================================
-- Setting: verificación por escáner antes de aceptar una remisión.
--
--   true  → al generar la remisión se abre el modal de escaneo y NO se guarda
--           hasta que todas las partidas estén escaneadas (lo revalida
--           VNRemision.Guardar, no solo el front).
--   false → la remisión se guarda sin verificación; el modal sigue disponible
--           desde el botón "Verificar con escáner" para quien lo quiera usar.
--
-- Se administra desde /Settings/Settings como el resto (setting_type checkbox).
-- Idempotente: se puede correr varias veces sin duplicar el renglón.
-- ============================================================================

INSERT INTO srs.settings (display_name, setting_name, setting_value, setting_type)
SELECT 'Escaneo obligatorio en Remisiones', 'escaneo_remision_obligatorio', 'false', 'checkbox'
WHERE NOT EXISTS (
    SELECT 1 FROM srs.settings WHERE setting_name = 'escaneo_remision_obligatorio'
);

-- Para activarlo por SQL (o desde la pantalla de Settings):
-- UPDATE srs.settings SET setting_value = 'true' WHERE setting_name = 'escaneo_remision_obligatorio';
