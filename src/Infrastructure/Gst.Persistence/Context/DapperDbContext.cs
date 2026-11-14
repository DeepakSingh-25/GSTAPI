using Gst.Application.Interfaces.IContext;
using Gst.Domain.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;

namespace Gst.Persistence.Context
{
    public class DapperDbContext(IOptions<ConnectionStrings> connectionStrings) : IDapperDbContext
    {
        private readonly string _connectionString = connectionStrings.Value.DefaultConnection ?? string.Empty;

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}
