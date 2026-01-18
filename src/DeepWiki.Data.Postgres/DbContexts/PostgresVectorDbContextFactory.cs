using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace DeepWiki.Data.Postgres.DbContexts
{
    public class PostgresVectorDbContextFactory : IDesignTimeDbContextFactory<PostgresVectorDbContext>
    {
        public PostgresVectorDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("DEEPWIKI_POSTGRES_CONNECTION")
                ?? throw new InvalidOperationException(
                    "Connection string not configured. Set CONNECTION_STRING or DEEPWIKI_POSTGRES_CONNECTION environment variable.");

            var optionsBuilder = new DbContextOptionsBuilder<PostgresVectorDbContext>();
            
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseVector();
            var dataSource = dataSourceBuilder.Build();

            optionsBuilder.UseNpgsql(dataSource, options =>
            {
                options.UseVector();
                options.EnableRetryOnFailure(maxRetryCount: 3);
            });

            return new PostgresVectorDbContext(optionsBuilder.Options);
        }
    }
}
