using System.Data;

namespace Gst.Application.Interfaces.IContext
{
    // Dapper (read side)
    public interface IDapperDbContext
    {
        IDbConnection CreateConnection();
    }
}
