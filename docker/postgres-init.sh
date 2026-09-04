#!/bin/bash
# postgres imajı /docker-entrypoint-initdb.d altındaki .sh ve .sql dosyalarını
# ilk açılışta çalıştırıyor. .sql doğrudan verilseydi parolalar dosyaya gömülü
# olurdu; bu sarmalayıcı onları ortamdan alıp psql değişkeni olarak geçiriyor.
set -euo pipefail

: "${WALLET_OWNER_PASSWORD:?WALLET_OWNER_PASSWORD tanımlı değil}"
: "${WALLET_APP_PASSWORD:?WALLET_APP_PASSWORD tanımlı değil}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v wallet_owner_password="$WALLET_OWNER_PASSWORD" \
     -v wallet_app_password="$WALLET_APP_PASSWORD" \
     -f /docker-entrypoint-initdb.d/postgres-init.sql
