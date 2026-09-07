# Ledger şeması

**Bu dosya kaynak değil, açıklamadır.** Şemanın tek kaynağı EF Core migration'ları
(`src/WalletService/Infrastructure/Persistence/`): tablolar Fluent API konfigürasyonlarından,
zero-sum trigger'ı ve `REVOKE` ise migration içindeki `migrationBuilder.Sql(...)`
bloklarından geliyor. Ayrı bir `.sql` dosyası tutulmuyor.

Buradaki DDL, o konfigürasyonun okunabilir karşılığı — neyin neden öyle olduğunu anlatıyor.
Kod ile burası çeliştiğinde kod haklıdır, bu dosya güncellenir (`CLAUDE.md` "Çalışma tarzı").

Gerekçeler: `docs/decisions.md`.

## EF ile ifade EDİLEMEYEN iki şey

Geri kalan her şey — tablo, kolon, PK, composite FK, unique constraint, partial index,
CHECK constraint — Fluent API'de duruyor. İki istisna var:

| şey | neden model'e girmiyor | nerede duruyor |
| --- | --- | --- |
| zero-sum PL/pgSQL trigger'ı | EF'in trigger fonksiyonu karşılığı yok | migration içinde `Sql(...)` |
| `REVOKE UPDATE, DELETE` | şema değil yetki; EF yetki modellemez | migration içinde `Sql(...)` |

`COALESCE(provider,'')` üzerindeki ifade index'i **istisna değil**: `provider_key` diye
STORED generated column eklenip index onun üstüne kuruldu, böylece EF modelinin parçası
kaldı. Kolon türetilmiş olduğu için sapma riski yok.

## İki seviye

```
accounts          müşteri hesabı        (1)
   └── ledger_accounts                  (n)   -- cüzdanlar VE sistem hesapları
```

`ledger_accounts` "bakiye tutabilen her şey"i tutar. Ayrı `wallets` / `system_accounts`
tablolarına bölünemez çünkü `ledger_entries` **tek bir FK hedefine** işaret etmek zorunda —
bir transfer'in bacakları hem cüzdan hem `revenue` olabiliyor. Bölünseydi polimorfik FK
gerekirdi ve referential integrity çökerdi.

**Cüzdan** = `ledger_accounts` içinde `type = 'user_wallet'` olan satır; `account_id`'si
dolu olan tek tip odur.

## accounts

Müşteri hesabı. Müşteri yönetimi tablosu DEĞİL — ad, e-posta, KYC verisi burada durmaz,
onlar bu sistemin kapsamı dışında. İki iş yapar: sahipliğin kimliği olmak ve
kişi/işletme ayrımını tek yerde tutmak (`decisions.md` madde 20).

```sql
CREATE TABLE accounts (
    id          uuid PRIMARY KEY,
    type        text NOT NULL CHECK (type IN ('person','business')),
    created_at  timestamptz NOT NULL DEFAULT now()
);
```

Bir hesabın **aynı para biriminde birden fazla cüzdanı olabilir** —
`ledger_accounts (account_id, currency)` üzerinde tekillik kısıtı bilinçli olarak YOKTUR.
Sonucu: günlük limitler cüzdan bazında değil **hesap bazında** uygulanır, yoksa müşteri
ikinci cüzdan açarak limiti aşar.

## ledger_accounts

```sql
CREATE TABLE ledger_accounts (
    id          uuid PRIMARY KEY,
    type        text NOT NULL CHECK (type IN
                  ('user_wallet','clearing','revenue','nostro','provider_expense')),
    account_id  uuid NULL REFERENCES accounts(id),  -- cüzdanda zorunlu, sistem hesabında NULL
    name        text NULL,          -- cüzdan adı ("Birikim"), sistem hesaplarında NULL
    provider    text NULL,          -- sistem hesaplarında sağlayıcı ayrımı, cüzdanda NULL
    currency    char(3) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),

    -- Kolon başına bir kural: "bu kolon TAM OLARAK şu tipte dolu".
    -- İhlalde Postgres constraint adını söylediği için hangi kuralın bozulduğu belli olur.
    CONSTRAINT ck_ledger_accounts_account
        CHECK ((type = 'user_wallet') = (account_id IS NOT NULL)),
    CONSTRAINT ck_ledger_accounts_name
        CHECK ((type = 'user_wallet') = (name IS NOT NULL)),
    CONSTRAINT ck_ledger_accounts_name_blank
        CHECK (name IS NULL OR btrim(name) <> ''),
    CONSTRAINT ck_ledger_accounts_provider
        CHECK ((type IN ('clearing','nostro','provider_expense')) = (provider IS NOT NULL)),
    CONSTRAINT ck_ledger_accounts_provider_blank
        CHECK (provider IS NULL OR btrim(provider) <> ''),

    -- Tekillik amacı YOK: id zaten PK, currency eklemek hiçbir yeni kısıt getirmiyor.
    -- Tek işi ledger_entries ve ledger_balances'ın composite FK hedefi olabilmek —
    -- Postgres FK'nın referans verdiği kolonların unique olmasını şart koşuyor.
    -- Gerekçe: decisions.md madde 17.
    CONSTRAINT uq_ledger_accounts_id_currency UNIQUE (id, currency)
);

-- Bir hesabın cüzdanlarını listelemek ve günlük limitini toplamak için. Limit hesap
-- bazında uygulandığı için bu index sıcak yolda: her transfer'de çalışıyor.
CREATE INDEX ix_ledger_accounts_account
    ON ledger_accounts (account_id) WHERE account_id IS NOT NULL;

-- (account_id, currency) üzerinde tekillik YOK — bilinçli. Bir hesabın aynı para
-- biriminde birden fazla cüzdanı olabilir (madde 20).

-- Sistem hesabı tekilliği: aynı (tip, sağlayıcı, currency) ikinci kez açılamaz.
-- provider NULL olabildiği için normalize edilir — NULL'lar unique index'te birbirine
-- eşit sayılmaz, ham kolon yetmez. EF ifade index'i modelleyemediğinden normalizasyon
-- STORED generated column ile yapılıyor, böylece index EF modelinde kalıyor:
--   provider_key text GENERATED ALWAYS AS (COALESCE(provider, '')) STORED
CREATE UNIQUE INDEX ux_ledger_accounts_system
    ON ledger_accounts (type, provider_key, currency)
 WHERE account_id IS NULL;
```

`type` (ledger rolü) ile `accounts.type` (person/business) **farklı sorular**: birincisi
bu hesabın ledger'da ne işe yaradığı, ikincisi sahibinin kim olduğu. Dik boyutlar,
birleştirilmez (`decisions.md` madde 6). Ledger çekirdeği yalnızca `ledger_accounts.type`'a
bakar; policy katmanı transfer tipi için `accounts.type`'a, mutabakat `provider`'a.

`accounts.type` **yalnızca `accounts`'ta duruyor**, cüzdana kopyalanmıyor. Tek kaynak
olduğu için sapması mümkün değil ve composite FK'ya gerek kalmıyor; policy katmanı
person/business bilgisini `accounts`'a bakarak alır (PK araması).

| `type`             | `account_id` | `name` | `provider` | Negatife düşebilir | Anlamı                                |
| ------------------ | ------------ | ------ | ---------- | ------------------ | ------------------------------------- |
| `user_wallet`      | **dolu**     | dolu   | NULL       | Hayır              | Müşteri cüzdanı                       |
| `clearing`         | NULL         | NULL   | sağlayıcı  | Evet               | Yolda olan / settle olmamış para      |
| `revenue`          | NULL         | NULL   | NULL       | Evet               | Müşteriden alınan komisyon (gelir)    |
| `nostro`           | NULL         | NULL   | banka      | Evet               | Kendi banka hesabımızdaki gerçek para |
| `provider_expense` | NULL         | NULL   | sağlayıcı  | Evet               | Sağlayıcıya ödenen ücret (gider)      |

`revenue` ve `provider_expense` ayrı tutulur, netleştirilmez. Biri gelir biri gider;
compensation'da `revenue` ters kayıtla iade edilir, `provider_expense` edilmez
(banka işlemi denediyse ücreti kesilmiştir).

Sistem hesapları seed migration ile oluşturulur: `revenue` currency başına bir tane,
`clearing` / `nostro` / `provider_expense` ise **sağlayıcı × currency** başına bir tane.
Birden fazla sağlayıcı varsa mutabakat ancak böyle ayrıştırılabilir.

`ck_ledger_accounts_*` kısıtları, `LedgerAccount.Wallet()` / `LedgerAccount.System()`
factory'lerinin DB tarafındaki eşidir — ikisi aynı kuralı söyler. Hesap yalnızca
uygulamadan açılmıyor: seed migration, düzeltme script'i, ileride bir admin endpoint'i.
Kural tek tarafta kalırsa diğer yoldan geçersiz satır giriyor ve hiçbir yerde hata
görünmüyor (zero-sum bozulmadığı için trigger da susuyor).

### İşaret sezgisi (dikkat)

Konvansiyon credit `+` / debit `-` olduğu için sistem hesaplarının "normal" bakiyesi
sezgiye ters görünür — yükümlülük hesapları artıda, varlık hesapları eksidedir:

| Hesap              | Muhasebe rolü | Para bizdeyken bakiye |
| ------------------ | ------------- | --------------------- |
| `user_wallet`      | yükümlülük    | `+` (müşteriye borç)  |
| `revenue`          | gelir         | `+`                   |
| `nostro`           | varlık        | `−` (bankada para var)|
| `provider_expense` | gider         | `−`                   |
| `clearing`         | duruma göre   | top-up'ta `−` (alacak), withdrawal'da `+` (borç) |

`nostro` bakiyesinin `-97.1` olması "97.1 açık" değil, "bankada 97.1 var" demektir.
Rapor katmanı işareti sunum için çevirir; ledger'da asla çevrilmez.

## ledger_transactions

```sql
CREATE TABLE ledger_transactions (
    id               uuid PRIMARY KEY,
    type             text NOT NULL,       -- p2p, p2b, b2p, b2b, payment, topup, withdrawal,
                                          -- refund, settlement, provider_invoice
    ledger_account_id uuid NOT NULL REFERENCES ledger_accounts(id),  -- idempotency KAPSAMI
    idempotency_key  text NULL,
    correlation_id   uuid NULL,           -- saga / webhook event ilişkisi
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_ledger_tx_idem
    ON ledger_transactions (ledger_account_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;
```

Partial unique index: idempotency key'siz iç işlemler çakışmaz.

`ledger_account_id` "isteği başlatan hesap" değil, **işlemin idempotency kapsamı olan hesap**.
İç işlemlerde de doludur — nullable OLMAZ, çünkü unique index içindeki NULL hiçbir NULL'a
eşit sayılmaz ve aynı fatura iki kez yazılabilir hale gelir (`decisions.md` madde 15):

| `type`             | `ledger_account_id`                          | `idempotency_key`   |
| ------------------ | ------------------------------------- | ------------------- |
| transfer (5 tip)   | gönderen `user_wallet`                | client'ın key'i     |
| `topup`            | alıcı `user_wallet`                   | `provider:event_id` |
| `withdrawal`       | çeken `user_wallet`                   | client'ın key'i     |
| `refund`           | aynı `user_wallet`                    | saga id             |
| `settlement`       | ilgili `clearing` (sağlayıcı bazında) | sağlayıcı batch ref |
| `provider_invoice` | ilgili `provider_expense`             | fatura numarası     |

## ledger_entries

```sql
CREATE TABLE ledger_entries (
    id              bigserial PRIMARY KEY,
    transaction_id  uuid NOT NULL REFERENCES ledger_transactions(id),
    ledger_account_id      uuid NOT NULL,
    amount          numeric(19,4) NOT NULL CHECK (amount <> 0),
    currency        char(3) NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),

    -- Composite FK, tek kolonluğun yerine geçer: hesabın var olduğunu da garantiler,
    -- entry'nin currency'sinin hesabınkiyle aynı olduğunu da. currency FK'ya KASTEN
    -- gereksiz kolon olarak konuyor — soru "hesap var mı" değil, "ikisi aynı satırda
    -- birlikte mi duruyor" (decisions.md madde 17).
    CONSTRAINT fk_ledger_entries_ledger_account
        FOREIGN KEY (ledger_account_id, currency) REFERENCES ledger_accounts (id, currency)
);

CREATE INDEX ix_ledger_entries_ledger_account ON ledger_entries (ledger_account_id, id);
CREATE INDEX ix_ledger_entries_tx ON ledger_entries (transaction_id);
```

`amount` işareti yönü taşır: credit `+`, debit `-`. Ayrı `direction` kolonu yok —
iki kaynak (işaret + direction) tutarsızlaşabilir, tek kaynak bırakıldı.

`currency` ledger hesabında da duruyor, burada da — bilinçli tekrar, ledger sorgularının
para birimini öğrenmek için `ledger_accounts`'a join olmasını engelliyor. Tekrarı güvenli kılan şey
yukarıdaki composite FK; onsuz iki kolon zamanla ayrışırdı.

### Append-only zorlaması

```sql
REVOKE UPDATE, DELETE ON ledger_entries FROM PUBLIC;
-- uygulama rolü için de açıkça:
REVOKE UPDATE, DELETE ON ledger_entries FROM wallet_app;
```

### Zero-sum invariant

```sql
CREATE OR REPLACE FUNCTION assert_ledger_balanced() RETURNS trigger AS $$
DECLARE
    bad_currency char(3);
    bad_total    numeric(19,4);
BEGIN
    -- Toplam para birimi BAŞINA sıfır olmalı. Tek SUM yetmez: +100 TRY ile -100 USD
    -- toplamı sıfır çıkar ve dengesiz bir işlem dengeli sayılırdı.
    SELECT currency, SUM(amount)
      INTO bad_currency, bad_total
      FROM ledger_entries
     WHERE transaction_id = NEW.transaction_id
     GROUP BY currency
    HAVING SUM(amount) <> 0
     LIMIT 1;

    IF FOUND THEN
        RAISE EXCEPTION 'Ledger transaction % is unbalanced in %: %',
            NEW.transaction_id, bad_currency, bad_total;
    END IF;

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE CONSTRAINT TRIGGER trg_ledger_balanced
    AFTER INSERT ON ledger_entries
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION assert_ledger_balanced();
```

`DEFERRABLE INITIALLY DEFERRED` şart: satırlar tek tek insert edilirken ara durumda
toplam sıfır değil, kontrol commit anında çalışmalı.

`FOR EACH ROW` de şart — Postgres `CONSTRAINT TRIGGER`'ı statement seviyesinde deferred
yapamıyor. Bedeli: 3 bacaklı bir transfer'de bu aggregate 3 kez koşuyor, hep aynı sonucu
bularak. `ix_ledger_entries_tx` tam bunun için var; sorgu birkaç satır okuyor. Kabul
edilen maliyet — alternatifi invariant'ı tamamen uygulamaya bırakmak ki `decisions.md`
madde 5 bunu açıkça reddediyor.

## ledger_balances

```sql
CREATE TABLE ledger_balances (
    ledger_account_id  uuid PRIMARY KEY,
    balance     numeric(19,4) NOT NULL DEFAULT 0,
    currency    char(3) NOT NULL,
    version     bigint NOT NULL DEFAULT 0,
    updated_at  timestamptz NOT NULL DEFAULT now(),

    -- ledger_entries ile aynı gerekçe: projeksiyonun para birimi hesabınkinden sapamaz.
    CONSTRAINT fk_ledger_balances_ledger_account
        FOREIGN KEY (ledger_account_id, currency) REFERENCES ledger_accounts (id, currency)
);
```

Ledger'dan türetilmiş projeksiyon. Source of truth `ledger_entries`; bu tablo her zaman
yeniden inşa edilebilir, tersi geçerli değil.

EF Core: `version` üzerinde `IsConcurrencyToken()`. Başka hiçbir entity'de concurrency token yok.

### Doğrulama sorgusu (mutabakat job'ı bunu koşar)

```sql
SELECT b.ledger_account_id, b.currency, b.balance, COALESCE(SUM(e.amount), 0) AS derived
  FROM ledger_balances b
  LEFT JOIN ledger_entries e
    ON e.ledger_account_id = b.ledger_account_id
   AND e.currency   = b.currency
 GROUP BY b.ledger_account_id, b.currency, b.balance
HAVING b.balance <> COALESCE(SUM(e.amount), 0);
```

Join'de `currency` de var: FK ikisinin sapmasını zaten engelliyor, ama sorgu bu
varsayıma yaslanmıyor. Bozuk bir durumda sessizce yanlış bir `derived` üretmek yerine
sapmayı satır olarak gösteriyor — mutabakat job'ının işi bunu yakalamak.

Boş dönmeli. Satır dönerse projeksiyon sapmış — alarm.

## provider_fees

Ledger DEĞİL. Sağlayıcı ücretinin beklenen/gerçekleşen takibi ve fatura eşleştirmesi.
Zero-sum invariant'ına dahil değildir, hiçbir bakiyeyi etkilemez.

```sql
CREATE TABLE provider_fees (
    id                uuid PRIMARY KEY,
    transaction_id    uuid NOT NULL REFERENCES ledger_transactions(id),
    provider          text NOT NULL,
    settlement_model  text NOT NULL CHECK (settlement_model IN ('net','invoiced')),
    fee_type          text NOT NULL DEFAULT 'provider',
    expected_amount   numeric(19,4) NOT NULL,
    actual_amount     numeric(19,4) NULL,
    currency          char(3) NOT NULL,
    provider_ref      text NULL,        -- sağlayıcının işlem referansı
    invoice_ref       text NULL,        -- fatura numarası, eşleşince dolar
    ledger_tx_id      uuid NULL REFERENCES ledger_transactions(id),  -- gider kaydı
    occurred_at       timestamptz NOT NULL,
    note              text NULL
);

CREATE INDEX ix_provider_fees_unbilled
    ON provider_fees (provider, occurred_at) WHERE invoice_ref IS NULL;
CREATE INDEX ix_provider_fees_tx ON provider_fees (transaction_id);
```

- `net` modelde: `actual_amount` settlement anında dolar, `invoice_ref` hiç dolmaz.
- `invoiced` modelde: fatura gelene kadar `actual_amount` NULL, `invoice_ref` boş.
- `expected_amount` bir tahmindir, ledger'a asla yazılmaz.
- Fatura özeti ayrı tablo gerektirmez; `invoice_ref` ile grupla. **İstisna:**
  uyuşmazlıkta bekleyen faturanın hiç satırı yok (ledger'a yazılmadı, ücretler
  işaretlenmedi), o yüzden `provider_invoices` tablosu faturanın kendisini tutuyor —
  `status`, iddia edilen tutar, beklenen tutar ve gerekçe (`decisions.md` madde 11).

`settlement_model` sağlayıcı konfigürasyonundan gelir:

```json
"Providers": {
  "stripe-fake": { "FeeSettlement": "Net",      "FeeOnFailure": "Charged" },
  "bank-fake":   { "FeeSettlement": "Invoiced", "FeeOnFailure": "Waived"  }
}
```

## Settlement kayıtları

### Top-up (net settlement, sağlayıcı 2.9 kesip 97.1 gönderiyor)

Webhook işlendiğinde:

```
user_wallet  +100
clearing     -100        sağlayıcıdan alacak
toplam          0
```

Settlement geldiğinde:

```
clearing         +100    alacak kapanır
provider_expense  -2.9
nostro           -97.1   banka hesabına giren gerçek tutar
toplam              0
```

### Withdrawal (100 çekim, müşteriden 2 komisyon, banka 1.5 alıyor)

Debit anında (`[Debited]`):

```
user_wallet  -102
clearing     +100        ödenecek para, yolda
revenue        +2        komisyon geliri
toplam          0
```

Settlement geldiğinde (net model):

```
clearing         -100
provider_expense  -1.5
nostro          +101.5   banka hesabından çıkan gerçek tutar
toplam              0
```

Net kazanç `2 - 1.5 = 0.5`; hiçbir hesapta yazmaz, iki hesabın farkı olarak raporlanır.

### Invoiced model

Settlement kaydında `provider_expense` bacağı YOKTUR:

```
clearing  -100
nostro    +100
toplam       0
```

İşlem anında `provider_fees`'e bir satır yazılır (`expected_amount = 1.5`, `actual_amount = NULL`).
Fatura geldiğinde tek toplu ledger kaydı:

```
provider_expense  -4200.00
nostro            +4200.00
toplam                   0
```

`type = 'provider_invoice'`, `idempotency_key = <fatura numarası>`. Aynı faturanın iki kez
işlenmesi manuel tetiklenen job'larda gerçek bir risk.

## Transfer akışı (referans)

```
BEGIN;
  -- 1) İdempotency kapısı ÖNCE (decisions.md madde 21). Policy'den sonra olsaydı,
  --    ilk transfer limiti doldurduğunda aynı isteğin tekrarı 422 alırdı.
  SELECT id FROM ledger_transactions
   WHERE ledger_account_id = @from AND idempotency_key = @key;
  -- satır varsa → onu dön, hiçbir kuralı yeniden değerlendirme

  -- 2) Bakiye ve policy
  SELECT balance, version FROM ledger_balances WHERE ledger_account_id = @from;
  -- limit kontrolü (komisyon DAHİL tutara, madde 22), komisyon hesabı
  --   → ihlal varsa 422, hiç yazma

  INSERT INTO ledger_transactions (id, type, ledger_account_id, idempotency_key) VALUES (...);

  INSERT INTO ledger_entries (transaction_id, ledger_account_id, amount, currency) VALUES
    (@tx, @from,    -102, 'TRY'),
    (@tx, @to,      +100, 'TRY'),
    (@tx, @revenue,   +2, 'TRY');

  -- ledger_account_id ARTAN SIRAYLA (deadlock önleme)
  UPDATE ledger_balances SET balance = balance + @delta, version = version + 1,
         updated_at = now()
   WHERE ledger_account_id = @acc AND version = @readVersion;
  -- 0 satır → DbUpdateConcurrencyException → rollback → retry (max 3)
COMMIT;
```

## Beklenen davranış tablosu

| Durum                          | HTTP | Not                                   |
| ------------------------------ | ---- | ------------------------------------- |
| Yetersiz bakiye                | 422  | İş kuralı reddi, hata değil           |
| Limit aşımı                    | 422  | Aynı şekilde                          |
| Optimistic lock çakışması      | 409  | Retry tükendikten sonra               |
| Aynı idempotency key, tamam    | 200  | Orijinal transaction dönülür          |
| Geçersiz DTO                   | 400  | FluentValidation, ProblemDetails      |
| Rate limit                     | 429  | `Retry-After` header'ı ile            |