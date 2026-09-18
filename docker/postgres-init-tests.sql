-- INTEGRATION TESTLERİN veritabanı. Uygulamanın kurulumundan (postgres-init.sql)
-- AYRI dosya çünkü ayrı bir amaca hizmet ediyor: canlıya giden kurulum, yalnızca
-- testler için var olan bir veritabanını açmamalı.
--
-- Bu dosya compose'da mount edildiği için koşuyor. Mount edilmezse init betiği
-- onu sessizce atlıyor — canlıda beklenen davranış bu.
--
-- Her test koşusu burada kendi schema'sını açıyor, migration'ları uyguluyor ve
-- sonunda düşürüyor (ConnectionStrings__IntegrationTests). Uygulama
-- veritabanlarından ayrı durması, yarıda kalan bir koşunun gerçek verinin yanına
-- çöp bırakmamasını sağlıyor.
--
-- postgres-init.sql'den SONRA çalışıyor; wallet rolleri orada kuruluyor.

-- Sahibi wallet_owner: testler bu rolle bağlanıp schema açıyor. Sahibi postgres
-- olsaydı CREATE SCHEMA yetki hatası verirdi.
CREATE DATABASE hiwallet_schema_check OWNER wallet_owner ENCODING 'UTF8';

REVOKE CONNECT ON DATABASE hiwallet_schema_check FROM PUBLIC;

-- Append-only testleri uygulamanın GERÇEK rolüyle koşuyor: ledger_entries
-- üzerindeki REVOKE sahibe işlemiyor, dolayısıyla wallet_owner ile koşan bir test
-- o kuralı hiç sınamazdı.
GRANT CONNECT ON DATABASE hiwallet_schema_check TO wallet_app;
