using BOS_ERP.Controllers;
using Npgsql;

namespace BOS_ERP.Helpers
{
    public class BuildDblinkConnection : Utilities
    {
        public string BuildDblink(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var dblinkConn = $"host={builder.Host} port={builder.Port} dbname={builder.Database} user={builder.Username} password={builder.Password} options='-c search_path={builder.SearchPath}'";
            var safeConn = dblinkConn.Replace("'", "''");

            return safeConn.ToString();
        }
    }
}