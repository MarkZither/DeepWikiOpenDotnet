#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(dirname "${BASH_SOURCE[0]}")/..
cd "$ROOT_DIR"

if [ ! -f .env ]; then
  echo "No .env file found. Copy .env.example to .env and fill in secrets."
  exit 1
fi

export $(grep -v '^#' .env | xargs)

# Use podman-compose if available, otherwise fall back to docker compose
COMPOSE_CMD=""
if command -v podman-compose &> /dev/null; then
  COMPOSE_CMD="podman-compose -f docker-compose.yml"
elif command -v docker &> /dev/null && docker compose version &> /dev/null; then
  COMPOSE_CMD="docker compose -f docker-compose.yml"
else
  echo "No podman-compose or docker compose found. Install one to bring up the DBs."
  exit 1
fi

echo "Starting persistent DBs (use 'docker compose -f docker-compose.test.yml' for ephemeral tests)..."
$COMPOSE_CMD up -d postgres sqlserver pgadmin

# Wait for Postgres to be ready
echo "Waiting for Postgres to be ready..."
for i in {1..60}; do
  if psql "postgresql://${POSTGRES_USER}:${POSTGRES_PASSWORD}@localhost:5432/${POSTGRES_DB}" -c 'SELECT 1' &> /dev/null; then
    echo "Postgres is ready"
    break
  fi
  sleep 2
done

# Set design-time and runtime connection env vars for EF tools
export DEEPWIKI_POSTGRES_CONNECTION="Host=localhost;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
export DEEPWIKI_SQLSERVER_CONNECTION="Server=localhost,1433;Database=master;User Id=sa;Password=${MSSQL_SA_PASSWORD};"

# Apply EF migrations using the startup project
echo "Applying Postgres migrations..."
cd src/DeepWiki.Data.Postgres
# Use the startup project as the ApiService (it now includes EF.Design as a package)
dotnet ef database update --startup-project ../..//src/deepwiki-open-dotnet.ApiService --context DeepWiki.Data.Postgres.DbContexts.PostgresVectorDbContext || true

cd "$ROOT_DIR"
echo "Done. Check logs (docker/podman logs) if there were errors."