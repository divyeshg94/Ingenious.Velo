using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Velo.SQL;

namespace Velo.Api.Services;

/// <summary>
/// Sets the tenant scope on a freshly-resolved DbContext so subsequent queries
/// pass through both EF query filters (CurrentOrgId) AND SQL Server row-level
/// security (SESSION_CONTEXT N'org_id').
///
/// Use this from any code path that resolves VeloDbContext outside the normal
/// HTTP request pipeline -- background Task.Run, hosted services, webhooks --
/// because TenantResolutionMiddleware only runs for incoming HTTP requests.
/// </summary>
internal static class TenantContextHelper
{
    public static async Task SetAsync(VeloDbContext db, string orgId, CancellationToken cancellationToken)
    {
        db.CurrentOrgId = orgId;

        // sp_set_session_context is SQL Server-specific; skip for non-relational providers (e.g. InMemory in tests).
        if (!db.Database.IsRelational()) return;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context N'org_id', @OrgId";
        cmd.Parameters.Add(new SqlParameter("@OrgId", orgId));

        // SECURITY: failing to set RLS session context would let this code path
        // read/write rows for ALL organisations. Let the exception propagate.
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
