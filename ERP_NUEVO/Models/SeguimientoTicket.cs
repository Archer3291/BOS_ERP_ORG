using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    public class SeguimientoTicket
    {
        public int IdSegTkts { get; set; }      // [id_seg_tkts] - Clave primaria
        public int IdTkt { get; set; }          // [id_tkt] - ID del ticket relacionado
        public int IdUsr { get; set; }          // [id_usr] - ID del usuario que responde
        public string Coment { get; set; }      // [coment] - Comentario de seguimiento
        public DateTime Fch { get; set; }       // [fch] - Fecha del comentario
    }
}
