using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("tkt_usuario_rol")]
    public class tkt_usuario_rol
    {
        [Key]
        public int id { get; set; }  // Nueva clave primaria

        public int id_usr { get; set; }

        public int id_rol_tkt { get; set; }

        [ForeignKey("id_usr")]
        public virtual Usuario Usuario { get; set; }

        // Puedes agregar la relación a Rol si existe:
        // [ForeignKey("id_rol_tkt")]
        // public virtual RolTicket RolTicket { get; set; }
    }
}
