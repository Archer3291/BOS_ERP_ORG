using System;

namespace BOS_ERP.Models
{
    public class EmailSenderModel
    {
        public string Folio { get; set; }
        public DateTime Date { get; set; }
        public string SenderName { get; set; }
        public string SenderEmail { get; set; }
        public decimal? Total { get; set; }
        public string Description { get; set; }
        public string RecipientName { get; set; }
        public string RecipientEmail { get; set; }
        public string URL { get; set; }
        public EmailSenderModel() { }
        public EmailSenderModel(string folio, DateTime date, string senderName, string senderEmail, decimal total, string description, string recipientName, string recipientEmail, string uRL)
        {
            Folio = folio;
            Date = date;
            SenderName = senderName;
            SenderEmail = senderEmail;
            Total = total;
            Description = description;
            RecipientName = recipientName;
            RecipientEmail = recipientEmail;
            URL = uRL;
        }
    }
}