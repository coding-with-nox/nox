#!/bin/sh
# Inject KEYCLOAK_CLIENT_SECRET into realm JSON before Keycloak imports it (F04 fix).
set -e

TEMPLATE=/opt/keycloak/scripts/nox-realm-template.json
TARGET=/opt/keycloak/data/import/nox-realm.json

mkdir -p /opt/keycloak/data/import

if [ -z "${KEYCLOAK_CLIENT_SECRET}" ]; then
  echo "[entrypoint] ERROR: KEYCLOAK_CLIENT_SECRET is not set. Aborting." >&2
  exit 1
fi

sed "s/__KEYCLOAK_CLIENT_SECRET__/${KEYCLOAK_CLIENT_SECRET}/g" "${TEMPLATE}" > "${TARGET}"
echo "[entrypoint] Realm JSON prepared at ${TARGET}"

exec /opt/keycloak/bin/kc.sh "$@"
