using MySqlConnector;

namespace DispatchService.Repositories;

public interface IDbConnectionFactory
{
    MySqlConnection CreateConnection();
}
