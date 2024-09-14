using System.IO.Abstractions;
using Microsoft.Data.Sqlite;

namespace Octans.Core;

public interface ISqlConnectionFactory
{
    SqliteConnection GetConnection();
}

public class SqlConnectionFactory(IPath path) : ISqlConnectionFactory
{
    public SqliteConnection GetConnection()
    {
        var dbFolder = path.Join(AppDomain.CurrentDomain.BaseDirectory, "db");

        var db = path.Join(dbFolder, "octans.db");

        return new(db);
    }
}