using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace DeepWiki.Data.Postgres.Health;

/// <summary>
/// Health check for PostgreSQL 17+ with pgvector extension support.
/// Validates database connectivity and verifies pgvector extension is installed.
/// </summary>
public class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public PostgresHealthCheck(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Check PostgreSQL version
            const string versionQuery = "SELECT version()";
            using var command = new NpgsqlCommand(versionQuery, connection);
            var versionInfo = (string?)await command.ExecuteScalarAsync(cancellationToken);

            if (string.IsNullOrEmpty(versionInfo))
            {
                return HealthCheckResult.Unhealthy("Could not retrieve PostgreSQL version");
            }

            // Parse version - PostgreSQL 17+ needed
            if (!versionInfo.Contains("PostgreSQL 17") && !versionInfo.Contains("PostgreSQL 1[8-9]"))
            {
                return HealthCheckResult.Degraded(
                    $"PostgreSQL version may not support pgvector. Version: {versionInfo}");
            }

            // Check pgvector extension availability
            const string extensionQuery = @"
                SELECT EXISTS(
                    SELECT 1 FROM pg_extension WHERE extname = 'vector'
                )";

            using var extCommand = new NpgsqlCommand(extensionQuery, connection);
            var pgvectorExists = (bool?)await extCommand.ExecuteScalarAsync(cancellationToken);

            if (!pgvectorExists.HasValue || !pgvectorExists.Value)
            {
                return HealthCheckResult.Degraded(
                    "pgvector extension not installed or not enabled in current database");
            }

            // Test vector type support with a simple query
            const string vectorTestQuery = @"
                SELECT '(0.1, 0.2, 0.3)'::vector";

            using var vectorCommand = new NpgsqlCommand(vectorTestQuery, connection);
            vectorCommand.CommandTimeout = 5;

            try
            {
                var result = await vectorCommand.ExecuteScalarAsync(cancellationToken);
                if (result == null)
                {
                    return HealthCheckResult.Unhealthy("Vector type test failed");
                }
            }
            catch (NpgsqlException ex)
            {
                return HealthCheckResult.Unhealthy(
                    $"Vector type support not available: {ex.Message}");
            }

            await connection.CloseAsync();

            return HealthCheckResult.Healthy($"PostgreSQL health check passed. Version: {versionInfo}");
        }
        catch (NpgsqlException ex)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL connection failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Unexpected error during health check: {ex.Message}", ex);
        }
    }
}
