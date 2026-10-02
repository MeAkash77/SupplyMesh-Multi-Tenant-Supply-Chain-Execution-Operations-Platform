using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
namespace SupplyMesh.Shared;

public interface IDb { Task<IDbConnection> OpenAsync(); }

public sealed class SqlDb(string cs) : IDb
{
    public async Task<IDbConnection> OpenAsync() { var c = new SqlConnection(cs); await c.OpenAsync(); return c; }
}

public static class DbExtensions
{
    // All data access goes through stored procedures; each proc sets SESSION_CONTEXT so RLS is enforced.
    public static async Task<IEnumerable<T>> ProcAsync<T>(this IDb db, string proc, object? p = null, CancellationToken ct = default)
    {
        using var c = await db.OpenAsync();
        return await c.QueryAsync<T>(new CommandDefinition(proc, p, commandType: CommandType.StoredProcedure, commandTimeout: 15, cancellationToken: ct));
    }
}
