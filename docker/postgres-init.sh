#!/bin/bash
# postgres imajı /docker-entrypoint-initdb.d altındaki .sh VE .sql dosyalarını ilk
# açılışta çalıştırıyor. .sql da o dizinde olsaydı iki kez koşardı — ikincisinde
# parola değişkenleri tanımsız olacağı için hata vererek. O yüzden .sql dizin
# DIŞINDA duruyor ve yalnızca buradan çağrılıyor.
#
# Parolalar dosyaya gömülü değil: ortamdan alınıp psql değişkeni olarak geçiriliyor.
set -euo pipefail

: "${WALLET_OWNER_PASSWORD:?WALLET_OWNER_PASSWORD tanımlı değil}"
: "${WALLET_APP_PASSWORD:?WALLET_APP_PASSWORD tanımlı değil}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v wallet_owner_password="$WALLET_OWNER_PASSWORD" \
     -v wallet_app_password="$WALLET_APP_PASSWORD" \
     -f /opt/hiwallet/postgres-init.sql
