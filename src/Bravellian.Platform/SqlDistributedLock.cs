namespace Bravellian.Platform;


using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

internal sealed partial class SqlDistributedLock : ISqlDistributedLock
{
    private readonly string connectionString;

    public SqlDistributedLock(IOptions<YourApplicationOptions> options)
    {
        this.connectionString = options.Value.ConnectionString;
    }

    public async Task<IAsyncDisposable?> AcquireAsync(
        string resource,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var sanitizedResource = SanitizeResource(resource);
        return await SqlAppLock.AcquireAsync(connectionString, sanitizedResource, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static string SanitizeResource(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource cannot be null or whitespace.", nameof(resource));
        }

        var sanitized = LockRegex().Replace(resource, "");
        return sanitized.ToLowerInvariant();
    }

    private sealed class SqlAppLock : IAsyncDisposable
    {
        private readonly SqlConnection connection;
        private readonly SqlTransaction transaction;
        private bool released;

        private SqlAppLock(SqlConnection conn, SqlTransaction tx)
        {
            this.connection = conn;
            this.transaction = tx;
        }

        public static async Task<SqlAppLock?> AcquireAsync(
            string connectionString,
            string resource,
            TimeSpan timeout,
            CancellationToken ct)
        {
            var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            try
            {
                using var cmd = new SqlCommand("sp_getapplock", conn, tx)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@Resource", resource);
                cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
                cmd.Parameters.AddWithValue("@LockOwner", "Transaction");
                cmd.Parameters.AddWithValue("@LockTimeout", (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds));

                var result = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? -99);
                if (result >= 0)
                {
                    return new SqlAppLock(conn, tx);
                }

                await tx.RollbackAsync(ct).ConfigureAwait(false);
                await conn.CloseAsync().ConfigureAwait(false);
                return null;
            }
            catch
            {
                try { await tx.RollbackAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
                await conn.CloseAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.released)
            {
                return;
            }

            this.released = true;

            try
            {
                await this.transaction.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                try { await this.transaction.RollbackAsync().ConfigureAwait(false); } catch { /* ignore */ }
                throw;
            }
            finally
            {
                await this.connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_-]")]
    private static partial Regex LockRegex();
}

    public class YourApplicationOptions
    {
        public string ConnectionString { get; set; }
    }