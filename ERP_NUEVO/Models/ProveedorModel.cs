using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;

namespace BOS_ERP.Models
{
    public class ProveedorModel
    {
        [Key]
        public int Id_Prov { get; set; }
        public string Suc { get; set; }
        public string Cve_Prov { get; set; }
        public string N_Prov { get; set; }
        public string Dir { get; set; }
        public string Col { get; set; }
        public string Pob { get; set; }
        public string Tel { get; set; }
        public string Tel2 { get; set; }
        public string Nro_Fax { get; set; }
        public string Rfc { get; set; }
        public string Cve_Agcy_Cpr { get; set; }
        public string Cve_Gpo { get; set; }
        public string Cve_Tp_Prov { get; set; }
        public decimal? Lim_Crd { get; set; }
        public int? Pl_Crd { get; set; }
        public string Dto { get; set; }
        public string Dto2 { get; set; }
        public string Coment1 { get; set; }
        public string Coment2 { get; set; }
        public string Cp { get; set; }
        public string Tp_Prov { get; set; }
        public string Opr_Prov { get; set; }
        public string Nacl { get; set; }
        public string Cve_Pais_Prov { get; set; }
        public string N_Cml_Prov { get; set; }
        public string Cve_Pais { get; set; }
        public string Cve_Edo { get; set; }
        public string Cve_Mpio { get; set; }
        public string Lada { get; set; }
        public string Id_Fisc { get; set; }
        public string Bco1_Prov { get; set; }
        public string Cta_Bco1_Prov { get; set; }
        public string Bco2_Prov { get; set; }
        public string Cta_Bco2_Prov { get; set; }
        public string Bco3_Prov { get; set; }
        public string Cta_Bco3_Prov { get; set; }
        public string Uso_Cfdi { get; set; }
        public string Fp { get; set; }   // Forma de pago
        public string Mdp { get; set; }  // Método de pago
        public int? ImpuestoId { get; set; }
        public List<PartidaDocumento> Productos { get; set; }
        public List<Impuesto> Impuestos { get; set; }
    }
}