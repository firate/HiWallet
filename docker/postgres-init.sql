-- Compose ile ayağa kalkan Postgres'in ilk kurulumu. Yalnızca veri dizini BOŞKEN
-- çalışır (postgres imajının davranışı); sonraki `docker compose up`'larda atlanır.
--
-- Şema burada kurulmuyor — o migration'ın işi. Burada yalnızca migration'ın ve
-- uygulamanın ihtiyaç duyduğu roller ve veritabanı var.

-- İki rol, çünkü ledger_entries üzerindeki REVOKE yalnızca tablo sahibi OLMAYAN bir
-- role işler; sahiplik yetkisi örtüktür ve revoke edilemez (decisions.md madde 5).
-- Tek rol kullanılsaydı append-only kuralı tamamen süs olurdu.
CREATE ROLE wallet_owner LOGIN PASSWORD :'wallet_owner_password';
CREATE ROLE wallet_app   LOGIN PASSWORD :'wallet_app_password';

CREATE DATABASE hiwallet_wallet OWNER wallet_owner ENCODING 'UTF8';

-- wallet_app hiçbir yerde tablo yaratamasın.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

\connect hiwallet_wallet

GRANT CONNECT ON DATABASE hiwallet_wallet TO wallet_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Tablo bazlı GRANT'ler burada DEĞİL: onlar migration'ın içinde
-- (decisions.md madde 24). Elle kurulum adımı olarak bırakılsalardı her yeni
-- ortamda unutulur ve uygulama "permission denied" ile karşılanırdı.
