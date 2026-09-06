-- Compose ile ayağa kalkan Postgres'in ilk kurulumu. Yalnızca veri dizini BOŞKEN
-- çalışır (postgres imajının davranışı); sonraki `docker compose up`'larda atlanır.
--
-- Şema burada kurulmuyor — o migration'ın işi. Burada yalnızca migration'ın ve
-- uygulamaların ihtiyaç duyduğu roller ve veritabanları var.
--
-- Dört veritabanı, dört sınır: wallet-service, topup-webhook,
-- withdrawal-orchestrator ve bank-service birbirinin tablosunu göremiyor
-- (CLAUDE.md "Servis sınırı"). Saga'nın anlamı buna bağlı: orchestrator wallet
-- tablolarına yazabilseydi compensation gereksizleşirdi (decisions.md madde 7).

-- ---------------------------------------------------------------------------
-- wallet-service
-- ---------------------------------------------------------------------------
-- İki rol, çünkü ledger_entries üzerindeki REVOKE yalnızca tablo sahibi OLMAYAN bir
-- role işler; sahiplik yetkisi örtüktür ve revoke edilemez (decisions.md madde 5).
-- Tek rol kullanılsaydı append-only kuralı tamamen süs olurdu.
CREATE ROLE wallet_owner LOGIN PASSWORD :'wallet_owner_password';
CREATE ROLE wallet_app   LOGIN PASSWORD :'wallet_app_password';

CREATE DATABASE hiwallet_wallet OWNER wallet_owner ENCODING 'UTF8';

-- ---------------------------------------------------------------------------
-- topup-webhook
-- ---------------------------------------------------------------------------
-- TEK rol, bilinçli. Wallet tarafındaki ikili kurulumun tek sebebi append-only
-- garantisini zorlamaktı; burada öyle bir tablo yok — topup_inbox satırları
-- yayınlandıkça güncelleniyor. Zorlanacak garanti olmayınca ikinci rol yalnızca
-- tören olurdu.
CREATE ROLE topup_app LOGIN PASSWORD :'topup_app_password';

CREATE DATABASE hiwallet_topup OWNER topup_app ENCODING 'UTF8';

-- ---------------------------------------------------------------------------
-- withdrawal-orchestrator
-- ---------------------------------------------------------------------------
-- TEK rol. Burada da append-only zorlanacak tablo yok: saga satırı her geçişte
-- güncelleniyor, outbox satırı yayınlandıkça.
CREATE ROLE withdrawal_app LOGIN PASSWORD :'withdrawal_app_password';

CREATE DATABASE hiwallet_withdrawal OWNER withdrawal_app ENCODING 'UTF8';

-- ---------------------------------------------------------------------------
-- bank-service (fake)
-- ---------------------------------------------------------------------------
-- Sahte servis ama sınırı gerçek: kendi veritabanı var ve wallet'ı göremiyor.
-- Gerçek bir banka da göremezdi.
CREATE ROLE bank_app LOGIN PASSWORD :'bank_app_password';

CREATE DATABASE hiwallet_bank OWNER bank_app ENCODING 'UTF8';

-- Hiçbir rol postgres veritabanında tablo yaratamasın.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

\connect hiwallet_wallet

GRANT CONNECT ON DATABASE hiwallet_wallet TO wallet_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Tablo bazlı GRANT'ler burada DEĞİL: onlar migration'ın içinde
-- (decisions.md madde 24). Elle kurulum adımı olarak bırakılsalardı her yeni
-- ortamda unutulur ve uygulama "permission denied" ile karşılanırdı.

\connect hiwallet_topup

-- topup_app hem migration'ı koşuyor hem uygulama; sahip olduğu için ek GRANT
-- gerekmiyor. wallet rollerine buraya erişim VERİLMİYOR.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CREATE ON SCHEMA public TO topup_app;

\connect hiwallet_withdrawal

-- topup ile aynı kurulum: tek rol, hem migration hem uygulama.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CREATE ON SCHEMA public TO withdrawal_app;

\connect hiwallet_bank

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CREATE ON SCHEMA public TO bank_app;
