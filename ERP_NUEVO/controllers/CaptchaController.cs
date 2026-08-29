using System;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.AspNetCore.Mvc;

public class CaptchaController : Controller
{
    // Genera la imagen captcha y guarda el texto en sesión
    public IActionResult GenerarCaptcha()
    {
        string texto = GenerarTextoAleatorio(5);
        HttpContext.Session.SetString("CaptchaTexto", texto);

        using (var bmp = new Bitmap(120, 40))
        using (var g = Graphics.FromImage(bmp))
        using (var ms = new MemoryStream())
        {
            g.Clear(Color.LightGray);
            using (var font = new Font("Arial", 20, FontStyle.Bold))
            {
                g.DrawString(texto, font, Brushes.Black, new PointF(10, 5));
            }

            // Añade ruido (líneas) para dificultar OCR simple
            var pen = new Pen(Color.Gray);
            var rand = new Random();
            for (int i = 0; i < 5; i++)
            {
                g.DrawLine(pen, rand.Next(bmp.Width), rand.Next(bmp.Height), rand.Next(bmp.Width), rand.Next(bmp.Height));
            }

            bmp.Save(ms, ImageFormat.Png);
            return File(ms.ToArray(), "image/png");
        }
    }

    private string GenerarTextoAleatorio(int longitud)
    {
        var caracteres = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var rand = new Random();
        var texto = new char[longitud];
        for (int i = 0; i < longitud; i++)
            texto[i] = caracteres[rand.Next(caracteres.Length)];
        return new string(texto);
    }
    public static class CaptchaHelper
    {
        public static bool ValidarCaptcha(string respuesta, HttpContext httpContext)
        {
            var texto = httpContext.Session.GetString("CaptchaTexto");
            return !string.IsNullOrEmpty(respuesta)
                && texto != null
                && respuesta.Equals(texto, StringComparison.OrdinalIgnoreCase);
        }
    }

}
