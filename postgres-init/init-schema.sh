#!/bin/bash
# Standalone-compose equivalent of the schema/role slice this service owns
# in orchestration's cluster init script — same shape, one service only.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
  CREATE SCHEMA IF NOT EXISTS users;
  CREATE ROLE users_role LOGIN PASSWORD '$USERS_DB_PASSWORD';
  ALTER ROLE users_role SET search_path TO users;
  GRANT USAGE, CREATE ON SCHEMA users TO users_role;
  GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA users TO users_role;
  ALTER DEFAULT PRIVILEGES IN SCHEMA users GRANT ALL PRIVILEGES ON TABLES TO users_role;
  ALTER DEFAULT PRIVILEGES IN SCHEMA users GRANT ALL PRIVILEGES ON SEQUENCES TO users_role;
EOSQL
