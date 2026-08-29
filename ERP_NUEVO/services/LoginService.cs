using BOS_ERP.Models;
using Npgsql;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace BOS_ERP.Services
{
    public class LoginService
    {
        private readonly IConfiguration _configuration;

        public LoginService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public UserLoginElement ValidateCredentials(string username, string password)
        {
            var successLoginElement = new UserLoginElement();
            successLoginElement.successLogin = false;
            try
            {
                var result = new List<Dictionary<object, object>>();
                string connectionString = _configuration.GetConnectionString("ERP_SRS");
                string query = "SELECT usuarioid, nombre || ' ' || apellido AS nombre, nombreusuario, contrasena FROM usuarios WHERE nombreusuario = @userName";
                using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
                {
                    using (NpgsqlCommand command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("username", username);
                        connection.Open();
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.HasRows)
                            {
                                while (reader.Read())
                                {
                                    string userHash = reader.GetString(reader.GetOrdinal("contrasena"));
                                    string userName = reader.GetString(reader.GetOrdinal("nombreusuario"));
                                    //EncryptString(username, userHash);
                                    string pass = DecryptString(username, userHash);
                                    if (pass == password)
                                    {
                                        successLoginElement.successLogin = true;
                                        successLoginElement.Name = username;
                                        successLoginElement.GivenName = reader.GetString(reader.GetOrdinal("nombre"));
                                    }
                                    else
                                    {
                                        successLoginElement.errorMessage = "Contraseña equivocada";
                                    }
                                }
                            }
                            else
                            {
                                successLoginElement.errorMessage = "Usuario no encontrado";
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                successLoginElement.successLogin = false;
                successLoginElement.errorMessage = e.Message;
            }
            return successLoginElement;
        }

        public string ValidateCredentials(string username, string password, bool sendString)
        {
            try
            {
                var result = new List<Dictionary<object, object>>();
                string connectionString = _configuration.GetConnectionString("ERP_SRS");
                string query = "SELECT contrasena FROM usuarios WHERE nombreusuario = @userName";
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("username", username);
                        connection.Open();
                        var qresult = command.ExecuteScalar();
                        if (qresult != null)
                        {
                            string pass = DecryptString(username, qresult.ToString());
                            if (pass == password)
                                return "";
                            else
                                return "Contraseña equivocada";
                        }
                        else
                        {
                            return "Usuario no encontrado";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }


        public string EncryptString(string key, string plainText)
        {
            var data = Encoding.Default.GetBytes(plainText);
            var pwd = !string.IsNullOrEmpty(key) ? Encoding.Default.GetBytes(key) : Array.Empty<byte>();
#pragma warning disable CA1416 // Validate platform compatibility
            var cipher = ProtectedData.Protect(data, pwd, DataProtectionScope.LocalMachine);
#pragma warning restore CA1416 // Validate platform compatibility
            return Convert.ToBase64String(cipher);
        }

        public string DecryptString(string key, string cipherText)
        {
            if (key == "" && cipherText == "")
                return "";
            if (key == "" && cipherText == "")
                return "";
            var cipher = Convert.FromBase64String(cipherText);
            var pwd = !string.IsNullOrEmpty(key) ? Encoding.Default.GetBytes(key) : Array.Empty<byte>();
#pragma warning disable CA1416 // Validate platform compatibility
            var data = ProtectedData.Unprotect(cipher, pwd, DataProtectionScope.LocalMachine);
#pragma warning restore CA1416 // Validate platform compatibility
            return Encoding.Default.GetString(data);
        }
    }

}