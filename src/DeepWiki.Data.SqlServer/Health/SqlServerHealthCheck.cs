using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DeepWiki.Data.SqlServer.Health;

/// <summary>
/// Health check for SQL Server 2025+ with vector support.
/// Validates database connectivity and verifies SQL Server version >= 2025.
/// </summary>
public class SqlServerHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public SqlServerHealthCheck(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Check SQL Server version
            const string versionQuery = "SELECT @@VERSION";
            using var command = new SqlCommand(versionQuery, connection);
            var versionInfo = (string?)await command.ExecuteScalarAsync(cancellationToken);

            if (string.IsNullOrEmpty(versionInfo))
            {
                return HealthCheckResult.Unhealthy("Could not retrieve SQL Server version");
            }

            // Parse version - SQL Server 2025 has major version 16
            // Version string format: "Microsoft SQL Server 2025 (RTM) - 16.0.xxx"
            if (!versionInfo.Contains("SQL Server 2025") && !versionInfo.Contains("16."))
            {
                return HealthCheckResult.Degraded(
                    $"SQL Server version may not support vectors. Version: {versionInfo}");
            }

            // Test vector support with a simple query
            const string vectorTestQuery = @"
                SELECT CAST(VECTOR([0.1, 0.2, 0.3], 3) AS VARCHAR(MAX))";

            using var vectorCommand = new SqlCommand(vectorTestQuery, connection);
            vectorCommand.CommandTimeout = 5;

            try
            {
                var result = await vectorCommand.ExecuteScalarAsync(cancellationToken);
                if (result == null)
                {
                    return HealthCheckResult.Unhealthy("Vector support test failed");
                }
            }
            catch (SqlException ex)
            {
                return HealthCheckResult.Unhealthy(
                    $"Vector support not available: {ex.Message}");
            }

            await connection.CloseAsync();

            return HealthCheckResult.Healthy($"SQL Server health check passed. Version: {versionInfo}");
        }
        catch (SqlException ex)
        {
            return HealthCheckResult.Unhealthy($"SQL Server connection failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Unexpected error during health check: {ex.Message}", ex);
        }
    }
}
