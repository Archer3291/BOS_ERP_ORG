using System;
using System.Collections.Generic;

namespace BOS_ERP.Models
{
    public class ReservacionSalaEmailModel
    {
        public string Folio { get; set; }
        public string Tipo { get; set; }
        public string Asunto { get; set; }
        public string Comentarios { get; set; }
        public string Sala { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public string Organizador { get; set; }
        public string OrganizadorEmail { get; set; }
        public string Destinatario { get; set; }
        public string Motivo { get; set; }
        public string URL { get; set; }
        public List<string> Participantes { get; set; } = new List<string>();
    }
}
