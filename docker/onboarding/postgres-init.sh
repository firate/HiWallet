#!/bin/bash
# onboarding'in kendi Postgres sunucusu: kişisel veri ledger'la aynı sunucuda durmuyor.
# postgres imajı bu betiği yalnızca veri dizini BOŞKEN çalıştırıyor.
#
# Sunucunun süper kullanıcısı ve veritabanının sahibi onboarding_owner (POSTGRES_USER);
# migration onunla koşuyor. Uygulama onboarding_app ile bağlanıyor: consents üzerindeki
# REVOKE yalnızca tablo sahibi OLMAYAN bir role işliyor, tek rolde süs olurdu.
#
# Parola dosyaya gömülü değil: ortamdan alınıp psql değişkeni olarak geçiriliyor.
set -euo pipefail

: "${ONBOARDING_APP_PASSWORD:?ONBOARDING_APP_PASSWORD tanımlı değil}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v onboarding_app_password="$ONBOARDING_APP_PASSWORD" <<'SQL'
CREATE ROLE onboarding_app LOGIN PASSWORD :'onboarding_app_password';

-- Tablo yetkileri migration'da, şemayla birlikte veriliyor. Burada yalnızca bağlantı.
GRANT CONNECT ON DATABASE hiwallet_onboarding TO onboarding_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL
