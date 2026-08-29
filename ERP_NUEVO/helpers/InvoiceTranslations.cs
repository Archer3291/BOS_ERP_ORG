
using System.Collections.Generic;

namespace BOS_ERP.Helpers
{
    public static class InvoiceTranslations
    {
        // Diccionario principal: clave -> (español, inglés)
        private static readonly Dictionary<string, (string ES, string EN)> _texts =
            new Dictionary<string, (string, string)>
        {
            //  HEADER / TÍTULOS GENERALES 
            { "invoice_title",          ("Factura CFDI 4.0",                    "Invoice CFDI 4.0") },
            { "invoice_advance",        ("Factura de Anticipo",                  "Advance Payment Invoice") },
            { "issuer_data",            ("Datos del Emisor",                     "Issuer Information") },
            { "tax_id",                 ("RFC",                                  "Tax ID (RFC)") },
            { "company_name",           ("Razón Social",                         "Company Name") },
            { "tax_regime",             ("Régimen Fiscal",                       "Tax Regime") },
            { "fiscal_address",         ("Dirección Fiscal",                     "Fiscal Address") },

            //  DATOS DEL COMPROBANTE 
            { "importer_data",          ("Datos de Importador",                  "Importer Data") },
            { "issue_date",             ("Fecha de Emisión",                     "Issue Date") },
            { "voucher_type",           ("Tipo de Comprobante",                  "Voucher Type") },
            { "payment_method_code",    ("Forma de Pago",                        "Payment Method") },
            { "payment_method_text",    ("Método de Pago",                       "Payment Terms") },
            { "currency",               ("Moneda",                               "Currency") },
            { "exchange_rate",          ("Tipo de Cambio",                       "Exchange Rate") },
            { "series",                 ("Serie",                                "Series") },
            { "internal_folio",         ("Folio Interno",                        "Internal Folio") },
            { "folio",                  ("Folio",                                "Folio") },
            { "purchase_order",         ("Orden de Compra",                      "Purchase Order") },
            { "fiscal_folio",           ("Folio Fiscal",                         "Fiscal Folio (UUID)") },
            { "date",                   ("Fecha",                                "Date") },
            { "type",                   ("Tipo",                                 "Type") },

            //  RECEPTOR 
            { "receiver",               ("Receptor",                             "Receiver") },
            { "fiscal_domicile",        ("Domicilio Fiscal",
                                         "Fiscal Domicile (ZIP)") },
            { "cfdi_use",               ("Uso CFDI",                             "CFDI Purpose") },

            //  CONCEPTOS 
            { "concepts",               ("Conceptos",                            "Line Items") },
            { "col_no_id",              ("No. Identificación",                   "Item No.") },
            { "col_description",        ("Descripción",                          "Description") },
            { "col_client_key",         ("Cve. Cliente",                         "Client Code") },
            { "col_qty",                ("Cantidad",                             "Qty") },
            { "col_unit",               ("Unidad",                               "Unit") },
            { "col_unit_price",         ("Precio Unitario",                      "Unit Price") },
            { "col_amount",             ("Importe",                              "Amount") },
            { "prod_serv_key",          ("Clave Prod/Serv",                      "Prod/Serv Key") },
            { "unit_key",               ("Clave Unidad",                         "Unit Key") },
            { "tax_object",             ("Objeto Imp",                           "Tax Object") },
            { "freight_desc",           ("Servicio de flete",                    "Freight Service") },
            { "freight_label",          ("FLETE",                                "FREIGHT") },
            { "service",                ("Servicio",                             "Service") },

            //  COMERCIO EXTERIOR 
            { "ce_title",               ("Complemento de Comercio Exterior v",   "Foreign Trade Complement v") },
            { "ce_transfer_reason",     ("Motivo Traslado",                      "Transfer Reason") },
            { "ce_pedimento_key",       ("Clave Pedimento",                      "Customs Entry Key") },
            { "ce_incoterm",            ("Incoterm",                             "Incoterm") },
            { "ce_origin_cert",         ("Certificado Origen",                   "Certificate of Origin") },
            { "ce_trusted_exporter",    ("Exportador Confiable",                 "Authorized Exporter") },
            { "ce_usd_rate",            ("Tipo Cambio USD",                      "USD Exchange Rate") },
            { "ce_total_usd",           ("Total USD",                            "Total USD") },
            { "ce_issuer_domicile",     ("Cliente/Importador",                   "Customer/Importer") },
            { "ce_receiver_section",    ("Cliente",                              "Customer") },
            { "ce_tax_reg_id",          ("Registro ID Tributario",               "Tax Registration ID") },
            { "ce_goods_title",         ("Detalle de Mercancías",                "Goods Detail") },
            { "ce_col_no_id",           ("No. Ident.",                           "Item No.") },
            { "ce_col_tariff",          ("Fracción Arancelaria",                 "Tariff Code") },
            { "ce_col_qty",             ("Cantidad",                             "Quantity") },
            { "ce_col_unit",            ("Unidad",                               "Unit") },
            { "ce_col_unit_val",        ("Valor Unit.",                          "Unit Value") },
            { "ce_col_usd_val",         ("Valor USD",                            "USD Value") },
            { "ce_street",              ("Calle",                                "Street") },
            { "ce_number",              ("Número",                               "Number") },
            { "ce_colony",              ("Colonia",                              "Colony") },
            { "ce_zip",                 ("C.P.",                                 "ZIP") },
            { "ce_locality",            ("Localidad",                            "City") },
            { "ce_municipality",        ("Municipio",                            "Municipality") },
            { "ce_state",               ("Estado",                               "State") },
            { "ce_country",             ("País",                                 "Country") },

            //  ANTICIPOS RELACIONADOS 
            { "related_cfdis",          ("CFDIs Relacionados (Anticipos Aplicados)", "Related CFDIs (Applied Advances)") },
            { "col_cfdi_uuid",          ("UUID del CFDI",                        "CFDI UUID") },
            { "col_apply_date",         ("Fecha de Aplicación",                  "Application Date") },
            { "col_balance_before",     ("Saldo Antes",                          "Balance Before") },
            { "col_applied_amount",     ("Monto Aplicado",                       "Applied Amount") },
            { "col_balance_after",      ("Saldo Después",                        "Balance After") },
            { "total_advances",         ("TOTAL ANTICIPOS APLICADOS:",           "TOTAL APPLIED ADVANCES:") },

            //  OBSERVACIONES / TOTALES 
            { "observations",           ("Observaciones",                        "Remarks") },
            { "amount_in_words",        ("Total con Letra",                      "Amount in Words") },
            { "before_advances",        ("(antes de anticipos)",                 "(before advances)") },
            { "advances_applied",       ("Anticipos Aplicados:",                 "Advances Applied:") },
            { "total",                  ("TOTAL:",                               "TOTAL:") },

            //  SELLOS 
            { "seals_title",            ("Sellos y Cadena Original",             "Digital Seals & Original Chain") },
            { "sat_cert_no",            ("No. de Serie del Certificado del SAT", "SAT Certificate Serial No.") },
            { "issuer_cert_no",         ("No de Serie del CSD del Emisor",       "Issuer CSD Serial No.") },
            { "cfdi_seal",              ("Sello CFDI",                           "CFDI Seal") },
            { "sat_seal",               ("Sello SAT",                            "SAT Seal") },
            { "original_chain",         ("Cadena Original",                      "Original Chain") },
            { "qr_code",                ("Código QR",                            "QR Code") },
            { "qr_verification",        ("Verificación SAT",                     "SAT Verification") },
            { "printed_rep",            ("Este documento es una representación impresa de un CFDI",
                                         "This document is a printed representation of a CFDI") },
            { "generated_on",           ("Generado el",                          "Generated on") },
        };


        public static string Get(string key, string lang = "es")
        {
            if (_texts.TryGetValue(key, out var pair))
                return lang == "en" ? pair.EN : pair.ES;
            return key; // fallback: devuelve la clave
        }

        /// <summary>
        /// Retorna "ES / EN" para mostrar ambos idiomas en el PDF.
        /// </summary>
        public static string Both(string key)
        {
            if (_texts.TryGetValue(key, out var pair))
                return $"{pair.ES} / {pair.EN}";
            return key;
        }

        //  NÚMERO A LETRAS EN INGLÉS 
        private static readonly string[] _ones =
        {
            "", "one", "two", "three", "four", "five", "six", "seven",
            "eight", "nine", "ten", "eleven", "twelve", "thirteen",
            "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
        };
        private static readonly string[] _tens =
        {
            "", "", "twenty", "thirty", "forty",
            "fifty", "sixty", "seventy", "eighty", "ninety"
        };

        public static string NumberToWordsEN(decimal number)
        {
            if (number == 0) return "zero";

            long intPart = (long)System.Math.Truncate(number);
            int decPart = (int)System.Math.Round((number - System.Math.Truncate(number)) * 100);

            string words = ChunkToWordsEN(intPart);

            if (decPart > 0)
                words += $" and {ChunkToWordsEN(decPart)} cents";

            // Capitalise first letter
            return char.ToUpper(words[0]) + words.Substring(1);
        }

        private static string ChunkToWordsEN(long n)
        {
            if (n == 0) return "";
            if (n < 0) return "minus " + ChunkToWordsEN(-n);

            string words = "";

            if (n >= 1_000_000)
            {
                words += ChunkToWordsEN(n / 1_000_000) + " million ";
                n %= 1_000_000;
            }
            if (n >= 1_000)
            {
                words += ChunkToWordsEN(n / 1_000) + " thousand ";
                n %= 1_000;
            }
            if (n >= 100)
            {
                words += _ones[n / 100] + " hundred ";
                n %= 100;
            }
            if (n >= 20)
            {
                words += _tens[n / 10];
                if (n % 10 > 0) words += "-" + _ones[n % 10];
                words += " ";
            }
            else if (n > 0)
            {
                words += _ones[n] + " ";
            }

            return words.Trim();
        }
    }
}