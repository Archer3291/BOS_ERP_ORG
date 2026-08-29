using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BOS_ERP.Models
{
    [Table("usersessions")]
    public class UserSession
    {

        [Key]
        [Column("sessionid")]
        public int SessionId { get; set; }
        [Column("userid")]
        public int UserId { get; set; }

        [Required]
        [StringLength(128)]
        [Column("deviceidentifier")]
        public string DeviceIdentifier { get; set; }
        [Column("expirytime")]
        public DateTime ExpiryTime { get; set; }
        [Column("isactive")]
        public bool IsActive { get; set; }
        [Column("lastactivity")]
        public DateTime LastActivity { get; set; } //Añadir si no existe
        [Column("createdat")]
        public DateTime CreatedAt { get; set; }

        [ForeignKey("UserId")]
        public virtual Usuario Usuario { get; set; }


    }

    public class UserLoginElement
    {
        public string Name { get; set; }
        public string GivenName { get; set; }
        public bool successLogin { get; set; }
        public string errorMessage { get; set; }
    }
}
