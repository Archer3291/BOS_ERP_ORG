using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace BOS_ERP.Helpers
{
    /// <summary>
    /// Helper mejorado para gestionar el estado de los procesos de cancelaci�n
    /// con manejo de estados intermedios y validaci�n de documentos
    /// </summary>
    public class ControlProcesoHelper
    {
        private readonly Func<string, Dictionary<string, object>, List<Dictionary<string, object>>> RunQuery;
        private readonly Func<string, Dictionary<string, object>, int> RunUpdate;

        public ControlProcesoHelper(
            Func<string, Dictionary<string, object>, List<Dictionary<string, object>>> runQuery,
            Func<string, Dictionary<string, object>, int> runUpdate)
        {
            RunQuery = runQuery;
            RunUpdate = runUpdate;
        }

        #region Estados del Proceso

        public enum EstadoProceso
        {
            INICIADO,
            CANCELADO_SAT,
            DOCUMENTOS_PREPARADOS,      // Nuevo: documentos clonados pero no validados
            REFACTURACION_INDIVIDUAL,
            FACTURA_GLOBAL,
            COMPLETADO,
            ERROR
        }

        public enum EstadoDocumento
        {
            PENDIENTE,          // Documento a�n no procesado
            CREADO,            // Documento clonado/creado
            VALIDADO,          // Documento verificado como correcto
            TIMBRADO,          // Documento timbrado exitosamente
            FALLIDO,           // Documento que fall�
            ELIMINADO          // Documento que fue borrado
        }

        #endregion

        #region M�todos Principales

        /// <summary>
        /// Inicializa un nuevo proceso de cancelaci�n
        /// </summary>
        public int IniciarProceso(string uuid, int encabezadoId, string rfcEmisor, string usuario)
        {
            var query = @"
                INSERT INTO control_proceso_cancelacion 
                (uuid, encabezado_id, rfc_emisor, estado_proceso, paso_actual, paso_descripcion, usuario_creacion, intentos)
                VALUES (@uuid, @encabezado, @rfc, @estado, @paso, @desc, @usuario, 1)
                ON CONFLICT (uuid, encabezado_id) DO UPDATE 
                SET intentos = control_proceso_cancelacion.intentos + 1,
                    ultimo_intento = CURRENT_TIMESTAMP
                RETURNING id;";

            var parametros = new Dictionary<string, object>
            {
                { "uuid", uuid },
                { "encabezado", encabezadoId },
                { "rfc", rfcEmisor },
                { "estado", EstadoProceso.INICIADO.ToString() },
                { "paso", 1 },
                { "desc", "Proceso iniciado" },
                { "usuario", usuario }
            };

            var resultado = RunQuery(query, parametros);
            return Convert.ToInt32(resultado[0]["id"]);
        }

        /// <summary>
        /// Registra la cancelaci�n exitosa en el SAT
        /// </summary>
        public void RegistrarCancelacionSAT(
            int procesoId,
            string acusePath,
            string motivo,
            string folioSustitucion = "")
        {
            var query = @"
                UPDATE control_proceso_cancelacion 
                SET cancelado_sat = true,
                    fecha_cancelacion_sat = CURRENT_TIMESTAMP,
                    acuse_cancelacion_path = @acuse,
                    motivo_cancelacion = @motivo,
                    folio_sustitucion = @folio,
                    estado_proceso = @estado,
                    paso_actual = 2,
                    paso_descripcion = 'Cancelado en SAT exitosamente'
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "acuse", acusePath },
                { "motivo", motivo },
                { "folio", folioSustitucion ?? "" },
                { "estado", EstadoProceso.CANCELADO_SAT.ToString() }
            };

            RunUpdate(query, parametros);
        }

        /// <summary>
        /// Guarda la informaci�n de documentos seleccionados para el proceso
        /// </summary>
        public void GuardarDocumentos(
            int procesoId,
            List<object> seleccionados,
            List<object> cancelados)
        {
            var query = @"
                UPDATE control_proceso_cancelacion 
                SET documentos_seleccionados = @seleccionados::jsonb,
                    documentos_cancelados = @cancelados::jsonb,
                    paso_actual = 3,
                    paso_descripcion = 'Documentos clasificados'
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "seleccionados", JsonConvert.SerializeObject(seleccionados) },
                { "cancelados", JsonConvert.SerializeObject(cancelados) }
            };

            RunUpdate(query, parametros);
        }

        #endregion

        #region Manejo de Variaciones

        /// <summary>
        /// Registra una variaci�n creada (documento intermedio)
        /// </summary>
        public void RegistrarVariacion(
            int procesoId,
            int idVariacion,
            int idDocumentoOrigen,
            string folioOrigen,
            EstadoDocumento estado = EstadoDocumento.CREADO)
        {
            var querySelect = @"
                SELECT variaciones_creadas 
                FROM control_proceso_cancelacion 
                WHERE id = @id;";

            var resultado = RunQuery(querySelect, new Dictionary<string, object> { { "id", procesoId } });
            var variaciones = new List<Dictionary<string, object>>();

            if (resultado.Any() && resultado[0]["variaciones_creadas"] != null)
            {
                var json = resultado[0]["variaciones_creadas"].ToString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    variaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                }
            }

            // Agregar nueva variaci�n
            variaciones.Add(new Dictionary<string, object>
            {
                { "id_variacion", idVariacion },
                { "id_documento_origen", idDocumentoOrigen },
                { "folio_origen", folioOrigen },
                { "estado", estado.ToString() },
                { "fecha_creacion", DateTime.Now },
                { "intentos", 1 }
            });

            var queryUpdate = @"
                UPDATE control_proceso_cancelacion 
                SET variaciones_creadas = @variaciones::jsonb,
                    estado_proceso = @estado,
                    paso_actual = 3,
                    paso_descripcion = @desc
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "variaciones", JsonConvert.SerializeObject(variaciones) },
                { "estado", EstadoProceso.DOCUMENTOS_PREPARADOS.ToString() },
                { "desc", $"Variaci�n {idVariacion} creada para {folioOrigen}" }
            };

            RunUpdate(queryUpdate, parametros);
        }

        /// <summary>
        /// Actualiza el estado de una variaci�n espec�fica
        /// </summary>
        public void ActualizarEstadoVariacion(
            int procesoId,
            int idVariacion,
            EstadoDocumento nuevoEstado,
            string mensaje = "")
        {
            var querySelect = @"
                SELECT variaciones_creadas 
                FROM control_proceso_cancelacion 
                WHERE id = @id;";

            var resultado = RunQuery(querySelect, new Dictionary<string, object> { { "id", procesoId } });

            if (!resultado.Any() || resultado[0]["variaciones_creadas"] == null)
                return;

            var json = resultado[0]["variaciones_creadas"].ToString();
            var variaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);

            // Buscar y actualizar la variaci�n espec�fica
            var variacion = variaciones.FirstOrDefault(v =>
                Convert.ToInt32(v["id_variacion"]) == idVariacion);

            if (variacion != null)
            {
                variacion["estado"] = nuevoEstado.ToString();
                variacion["ultimo_cambio"] = DateTime.Now;
                variacion["intentos"] = Convert.ToInt32(variacion["intentos"]) + 1;

                if (!string.IsNullOrWhiteSpace(mensaje))
                {
                    variacion["mensaje"] = mensaje;
                }
            }

            var queryUpdate = @"
                UPDATE control_proceso_cancelacion 
                SET variaciones_creadas = @variaciones::jsonb
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "variaciones", JsonConvert.SerializeObject(variaciones) }
            };

            RunUpdate(queryUpdate, parametros);
        }

        /// <summary>
        /// Marca variaciones como eliminadas (para limpieza)
        /// </summary>
        public void MarcarVariacionesEliminadas(int procesoId, List<int> idsVariaciones)
        {
            foreach (var id in idsVariaciones)
            {
                ActualizarEstadoVariacion(procesoId, id, EstadoDocumento.ELIMINADO, "Documento limpiado por error");
            }
        }

        /// <summary>
        /// Obtiene variaciones activas (no eliminadas ni fallidas)
        /// </summary>
        public List<int> ObtenerVariacionesActivas(int procesoId)
        {
            var estado = ObtenerEstadoProcesoPorId(procesoId);
            if (estado == null || !estado.ContainsKey("variaciones_creadas") || estado["variaciones_creadas"] == null)
                return new List<int>();

            var json = estado["variaciones_creadas"].ToString();
            var variaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);

            // TIMBRADO también queda fuera: un documento ya timbrado NO está pendiente.
            // Contarlo como activo tenía dos consecuencias graves:
            //   · al emitir la global de reemplazo, el proceso creía que había algo que
            //     reanudar y tomaba las variaciones de la nominativa del cliente;
            //   · una reanudación podía volver a timbrar un documento ya timbrado y
            //     generar un CFDI duplicado.
            return variaciones
                .Where(v =>
                {
                    var estadoVar = v["estado"].ToString();
                    return estadoVar != EstadoDocumento.ELIMINADO.ToString() &&
                           estadoVar != EstadoDocumento.FALLIDO.ToString() &&
                           estadoVar != EstadoDocumento.TIMBRADO.ToString();
                })
                .Select(v => Convert.ToInt32(v["id_variacion"]))
                .ToList();
        }

        /// <summary>
        /// Obtiene todas las variaciones independientemente de su estado
        /// </summary>
        public List<Dictionary<string, object>> ObtenerTodasVariaciones(int procesoId)
        {
            var estado = ObtenerEstadoProcesoPorId(procesoId);
            if (estado == null || !estado.ContainsKey("variaciones_creadas") || estado["variaciones_creadas"] == null)
                return new List<Dictionary<string, object>>();

            var json = estado["variaciones_creadas"].ToString();
            return JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
        }

        #endregion

        #region Refacturaci�n Individual

        /// <summary>
        /// Registra el resultado de una refacturaci�n individual
        /// </summary>
        public void RegistrarRefacturacionIndividual(
            int procesoId,
            string folio,
            string uuid,
            bool exitoso,
            string mensaje,
            int? idDocumento = null)
        {
            var querySelect = @"
                SELECT refacturacion_individual 
                FROM control_proceso_cancelacion 
                WHERE id = @id;";

            var resultado = RunQuery(querySelect, new Dictionary<string, object> { { "id", procesoId } });
            var refacturaciones = new List<Dictionary<string, object>>();

            if (resultado.Any() && resultado[0]["refacturacion_individual"] != null)
            {
                var json = resultado[0]["refacturacion_individual"].ToString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    refacturaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                }
            }

            // Agregar el nuevo resultado
            var refacturacion = new Dictionary<string, object>
            {
                { "folio", folio },
                { "uuid", uuid },
                { "estado", exitoso ? "success" : "error" },
                { "mensaje", mensaje },
                { "fecha", DateTime.Now }
            };

            if (idDocumento.HasValue)
            {
                refacturacion["id_documento"] = idDocumento.Value;
            }

            refacturaciones.Add(refacturacion);

            var queryUpdate = @"
                UPDATE control_proceso_cancelacion 
                SET refacturacion_individual = @refacturaciones::jsonb,
                    estado_proceso = @estado,
                    paso_actual = 4,
                    paso_descripcion = @desc
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "refacturaciones", JsonConvert.SerializeObject(refacturaciones) },
                { "estado", EstadoProceso.REFACTURACION_INDIVIDUAL.ToString() },
                { "desc", $"Procesados {refacturaciones.Count} documentos individualmente" }
            };

            RunUpdate(queryUpdate, parametros);
        }

        /// <summary>
        /// Obtiene los documentos que faltan por refacturar
        /// </summary>
        public List<Dictionary<string, object>> ObtenerDocumentosPendientes(int procesoId)
        {
            var estado = ObtenerEstadoProcesoPorId(procesoId);
            if (estado == null) return new List<Dictionary<string, object>>();

            var documentosCancelados = new List<Dictionary<string, object>>();
            if (estado.ContainsKey("documentos_cancelados") && estado["documentos_cancelados"] != null)
            {
                var json = estado["documentos_cancelados"].ToString();
                documentosCancelados = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            }

            var refacturaciones = new List<Dictionary<string, object>>();
            if (estado.ContainsKey("refacturacion_individual") && estado["refacturacion_individual"] != null)
            {
                var json = estado["refacturacion_individual"].ToString();
                refacturaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            }

            // Retornar solo los que no se han procesado exitosamente
            var foliosProcesados = refacturaciones
                .Where(r => r["estado"].ToString() == "success")
                .Select(r => r["folio"].ToString())
                .ToList();

            return documentosCancelados
                .Where(d => !foliosProcesados.Contains($"{d["gen"]}-{d["nat"]}-{d["folio"]}"))
                .ToList();
        }

        #endregion

        #region Factura Global

        /// <summary>
        /// Registra la factura global generada
        /// </summary>
        public void RegistrarFacturaGlobal(int procesoId, int idFactura, string uuid)
        {
            var query = @"
                UPDATE control_proceso_cancelacion 
                SET id_factura_global = @idFactura,
                    uuid_factura_global = @uuid,
                    estado_proceso = @estado,
                    paso_actual = 6,
                    paso_descripcion = @desc
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "idFactura", idFactura },
                { "uuid", uuid },
                { "estado", EstadoProceso.FACTURA_GLOBAL.ToString() },
                { "desc", $"Factura global generada con UUID: {uuid}" }
            };

            RunUpdate(query, parametros);
        }

        #endregion

        #region M�todos de Consulta

        /// <summary>
        /// Obtiene el estado actual de un proceso
        /// </summary>
        public Dictionary<string, object> ObtenerEstadoProceso(string uuid, int encabezadoId)
        {
            var query = @"
                SELECT * FROM control_proceso_cancelacion 
                WHERE uuid = @uuid AND encabezado_id = @encabezado 
                ORDER BY fecha_creacion DESC 
                LIMIT 1;";

            var parametros = new Dictionary<string, object>
            {
                { "uuid", uuid },
                { "encabezado", encabezadoId }
            };

            var resultado = RunQuery(query, parametros);
            return resultado.FirstOrDefault();
        }

        private Dictionary<string, object> ObtenerEstadoProcesoPorId(int procesoId)
        {
            var query = "SELECT * FROM control_proceso_cancelacion WHERE id = @id;";
            var resultado = RunQuery(query, new Dictionary<string, object> { { "id", procesoId } });
            return resultado.FirstOrDefault();
        }

        /// <summary>
        /// Verifica si ya se cancel� en el SAT
        /// </summary>
        public bool YaCanceladoEnSAT(string uuid, int encabezadoId)
        {
            var estado = ObtenerEstadoProceso(uuid, encabezadoId);
            if (estado == null) return false;

            return estado.ContainsKey("cancelado_sat") &&
                   Convert.ToBoolean(estado["cancelado_sat"]);
        }

        public bool ProcesoCompletado(int procesoId)
        {
            var estado = ObtenerEstadoProcesoPorId(procesoId);
            if (estado == null) return false;

            var estadoProceso = estado["estado_proceso"].ToString();
            return estadoProceso == "COMPLETADO";
        }

        #endregion

        #region Control de Estado

        /// <summary>
        /// Actualiza el estado del proceso
        /// </summary>
        public void ActualizarEstado(
            int procesoId,
            EstadoProceso nuevoEstado,
            int paso,
            string descripcion,
            Dictionary<string, object> datosAdicionales = null)
        {
            var query = @"
                UPDATE control_proceso_cancelacion 
                SET estado_proceso = @estado,
                    paso_actual = @paso,
                    paso_descripcion = @desc,
                    datos_adicionales = @datos::jsonb,
                    ultimo_intento = CURRENT_TIMESTAMP
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "estado", nuevoEstado.ToString() },
                { "paso", paso },
                { "desc", descripcion },
                { "datos", datosAdicionales != null ? JsonConvert.SerializeObject(datosAdicionales) : "{}" }
            };

            RunUpdate(query, parametros);
        }

        /// <summary>
        /// Marca el proceso como completado
        /// </summary>
        public void CompletarProceso(int procesoId)
        {
            var query = @"
                UPDATE control_proceso_cancelacion 
                SET estado_proceso = @estado,
                    paso_actual = 7,
                    paso_descripcion = 'Proceso completado exitosamente',
                    fecha_completado = CURRENT_TIMESTAMP
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "estado", EstadoProceso.COMPLETADO.ToString() }
            };

            RunUpdate(query, parametros);
        }

        /// <summary>
        /// Registra un error en el proceso
        /// </summary>
        public void RegistrarError(int procesoId, string mensajeError, int pasoFallido)
        {
            var query = @"
                UPDATE control_proceso_cancelacion 
                SET estado_proceso = @estado,
                    mensaje_error = @mensaje,
                    paso_descripcion = @desc,
                    ultimo_intento = CURRENT_TIMESTAMP
                WHERE id = @id;";

            var parametros = new Dictionary<string, object>
            {
                { "id", procesoId },
                { "estado", EstadoProceso.ERROR.ToString() },
                { "mensaje", mensajeError },
                { "desc", $"Error en paso {pasoFallido}: {mensajeError}" }
            };

            RunUpdate(query, parametros);
        }

        /// <summary>
        /// Verifica si un proceso puede ser reanudado
        /// </summary>
        public (bool PuedeReanudar, string Mensaje, int PasoReanudar) VerificarEstadoReanudacion(int procesoId)
        {
            var estado = ObtenerEstadoProcesoPorId(procesoId);
            if (estado == null)
                return (false, "Proceso no encontrado", 0);

            var estadoProceso = estado["estado_proceso"].ToString();
            var pasoActual = Convert.ToInt32(estado["paso_actual"]);
            var canceladoSAT = estado.ContainsKey("cancelado_sat") && Convert.ToBoolean(estado["cancelado_sat"]);

            //VALIDACI�N CR�TICA: Si est� completado, NO se puede reanudar bajo NINGUNA circunstancia
            if (estadoProceso == "COMPLETADO")
            {
                return (false, "Este proceso ya fue completado exitosamente y no puede ser modificado.", pasoActual);
            }

            // Si no se ha cancelado en SAT, debe empezar desde el paso 1
            if (!canceladoSAT)
            {
                return (true, "Debe iniciar el proceso de cancelaci�n en el SAT", 1);
            }

            // Determinar desde qu� paso puede reanudar
            switch (estadoProceso)
            {
                case "CANCELADO_SAT":
                    if (pasoActual < 3)
                        return (true, "Puede continuar con la selecci�n de documentos", 2);
                    return (true, "Puede continuar con la preparaci�n de documentos", pasoActual);

                case "DOCUMENTOS_PREPARADOS":
                    return (true, "Puede continuar con la refacturaci�n individual", 4);

                case "REFACTURACION_INDIVIDUAL":
                    return (true, "Puede continuar con la factura global", 5);

                case "FACTURA_GLOBAL":
                    return (true, "Puede completar el proceso", 6);

                case "ERROR":
                    // Determinar el �ltimo paso exitoso
                    if (!canceladoSAT)
                        return (true, "Debe reintentar la cancelaci�n en el SAT", 1);
                    if (pasoActual >= 4)
                        return (true, "Puede reintentar desde la refacturaci�n", 4);
                    return (true, "Puede reintentar desde la preparaci�n de documentos", 3);

                default:
                    return (false, "Estado de proceso desconocido", pasoActual);
            }
        }

        /// <summary>
        /// Obtiene un resumen legible del estado actual
        /// </summary>
        public Dictionary<string, object> ObtenerResumenProceso(int procesoId)
        {
            var estado = ObtenerEstadoProcesoPorId(procesoId);
            if (estado == null)
                return null;

            var resumen = new Dictionary<string, object>
    {
        { "procesoId", procesoId },
        { "uuid", estado["uuid"] },
        { "estadoProceso", estado["estado_proceso"] },
        { "pasoActual", estado["paso_actual"] },
        { "pasoDescripcion", estado["paso_descripcion"] },
        { "canceladoSAT", estado.ContainsKey("cancelado_sat") && Convert.ToBoolean(estado["cancelado_sat"]) },
        { "fechaCreacion", estado["fecha_creacion"] },
        { "ultimoIntento", estado["ultimo_intento"] },
        { "intentos", estado["intentos"] },
        { "mensajeError", estado["mensaje_error"] }
    };

            // Agregar informaci�n de variaciones si existen
            if (estado.ContainsKey("variaciones_creadas") && estado["variaciones_creadas"] != null)
            {
                var json = estado["variaciones_creadas"].ToString();
                var variaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);

                resumen["totalVariaciones"] = variaciones.Count;
                resumen["variacionesActivas"] = variaciones.Count(v =>
                    v["estado"].ToString() != "ELIMINADO" && v["estado"].ToString() != "FALLIDO");
            }

            // Agregar informaci�n de refacturaci�n
            if (estado.ContainsKey("refacturacion_individual") && estado["refacturacion_individual"] != null)
            {
                var json = estado["refacturacion_individual"].ToString();
                var refacturaciones = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);

                resumen["totalRefacturaciones"] = refacturaciones.Count;
                resumen["refacturacionesExitosas"] = refacturaciones.Count(r => r["estado"].ToString() == "success");
            }

            // Agregar informaci�n de factura global
            if (estado.ContainsKey("id_factura_global") && estado["id_factura_global"] != null)
            {
                resumen["facturaGlobalId"] = estado["id_factura_global"];
                resumen["facturaGlobalUUID"] = estado["uuid_factura_global"];
            }

            return resumen;
        }
        #endregion
    }
}