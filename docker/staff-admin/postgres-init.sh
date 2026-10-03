#!/bin/bash
# Personel yönetiminin kendi Postgres sunucusu: kim kime ne verdi kaydı burada.
# postgres imajı bu betiği yalnızca veri dizini BOŞKEN çalıştırıyor.
#
# Sunucunun süper kullanıcısı ve veritabanının sahibi staff_admin_owner (POSTGRES_USER);
# migration onunla koşuyor. Uygulama staff_admin_app ile bağlanıyor: kayıt üzerindeki
# REVOKE yalnızca tablo sahibi OLMAYAN bir role işliyor, tek rolde süs olurdu.
#
# Parola dosyaya gömülü değil: ortamdan alınıp psql değişkeni olarak geçiriliyor.
set -euo pipefail

: "${STAFF_ADMIN_APP_PASSWORD:?STAFF_ADMIN_APP_PASSWORD tanımlı değil}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v staff_admin_app_password="$STAFF_ADMIN_APP_PASSWORD" <<'SQL'
CREATE ROLE staff_admin_app LOGIN PASSWORD :'staff_admin_app_password';

-- Tablo yetkileri migration'da, şemayla birlikte veriliyor. Burada yalnızca bağlantı.
GRANT CONNECT ON DATABASE hiwallet_staff_admin TO staff_admin_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL
