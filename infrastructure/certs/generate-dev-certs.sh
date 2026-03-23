#!/usr/bin/env bash
# ============================================================
# generate-dev-certs.sh — Self-signed TLS certs for local dev
# Run once: bash infra/certs/generate-dev-certs.sh
# Generated files are .gitignored. Never commit them.
# ============================================================
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Generating dev CA..."
openssl genrsa -out "$DIR/ca.key" 4096
openssl req -new -x509 -days 3650 \
  -key "$DIR/ca.key" \
  -out "$DIR/ca.crt" \
  -subj "/CN=NoxDevCA/O=Nox/C=IT"

echo "Generating server certificate (PostgreSQL / shared)..."
openssl genrsa -out "$DIR/server.key" 2048
openssl req -new \
  -key "$DIR/server.key" \
  -out "$DIR/server.csr" \
  -subj "/CN=localhost/O=Nox/C=IT"
openssl x509 -req -days 3650 \
  -in "$DIR/server.csr" \
  -CA "$DIR/ca.crt" \
  -CAkey "$DIR/ca.key" \
  -CAcreateserial \
  -out "$DIR/server.crt"

echo "Generating Redis TLS certificate..."
openssl genrsa -out "$DIR/redis.key" 2048
openssl req -new \
  -key "$DIR/redis.key" \
  -out "$DIR/redis.csr" \
  -subj "/CN=nox-redis/O=Nox/C=IT"
openssl x509 -req -days 3650 \
  -in "$DIR/redis.csr" \
  -CA "$DIR/ca.crt" \
  -CAkey "$DIR/ca.key" \
  -CAcreateserial \
  -out "$DIR/redis.crt"

# PostgreSQL requires specific permissions on key files
chmod 600 "$DIR/server.key" "$DIR/redis.key"

echo ""
echo "✅ Certificates generated in $DIR"
echo ""
echo "Next steps:"
echo "  1. Start services:  docker compose --env-file infra/.env up"
echo "  2. PostgreSQL SSL:  already configured via SslMode=Prefer in connection strings"
echo "  3. Redis TLS:       update docker-compose redis command to use --tls-port"
echo ""
echo "CA cert for trust (add to system store if needed):"
echo "  $DIR/ca.crt"
