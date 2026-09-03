# Ledger şeması

Referans DDL. EF Core migration'ları bu şemayı üretmeli.
Gerekçeler: `docs/decisions.md`.

## accounts

```sql
CREATE TABLE accounts (
    id            uuid PRIMARY KEY,
    account_type  text NOT NULL CHECK (account_type IN
                    ('user_wallet','clearing','revenue','nostro','provider_expense')),
    owner_id      uuid NULL,          -- user_wallet için zorunlu, sistem hesaplarında NULL
    owner_type    text NULL CHECK (owner_type IN ('person','business')),
    provider      text NULL,          -- sistem hesaplarında sağlayıcı ayrımı, user_wallet'ta NULL
    currency      char(3) NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),

    -- Kolon başına bir kural: "bu kolon TAM OLARAK şu tipte dolu".
    -- İhlalde Postgres constraint adını söylediği için hangi kuralın bozulduğu belli olur.
    CONSTRAINT ck_accounts_owner_id
        CHECK ((account_type = 'user_wallet') = (owner_id IS NOT NULL)),
    CONSTRAINT ck_accounts_owner_type
        CHECK ((account_type = 'user_wallet') = (owner_type IS NOT NULL)),
    CONSTRAINT ck_accounts_provider
        CHECK ((account_type IN ('clearing','nostro','provider_expense')) = (provider IS NOT NULL)),
    CONSTRAINT ck_accounts_provider_blank
        CHECK (provider IS NULL OR btrim(provider) <> ''),

    -- Tekillik amacı YOK: id zaten PK, currency eklemek hiçbir yeni kısıt getirmiyor.
    -- Tek işi ledger_entries ve wallet_balances'ın composite FK hedefi olabilmek —
    -- Postgres FK'nın referans verdiği kolonların unique olmasını şart koşuyor.
    -- Gerekçe: decisions.md madde 17.
    CONSTRAINT uq_accounts_id_currency UNIQUE (id, currency)
);

CREATE INDEX ix_accounts_owner ON accounts (owner_id) WHERE owner_id IS NOT NULL;

-- sistem hesabı tekilliği: aynı (tip, sağlayıcı, currency) ikinci kez açılamaz.
-- provider NULL olabildiği için COALESCE ile normalize edilir — NULL'lar unique index'te
-- birbirine eşit sayılmaz, o yüzden ham kolon yetmez.
CREATE UNIQUE INDEX ux_accounts_system
    ON accounts (account_type, COALESCE(provider, ''), currency)
 WHERE owner_id IS NULL;
```

| account_type       | owner_type        | provider     | Negatife düşebilir | Anlamı                                |
| ------------------ | ----------------- | ------------ | ------------------ | ------------------------------------- |
| `user_wallet`      | person / business | NULL         | Hayır              | Müşteri cüzdanı                       |
| `clearing`         | NULL              | sağlayıcı    | Evet               | Yolda olan / settle olmamış para      |
| `revenue`          | NULL              | NULL         | Evet               | Müşteriden alınan komisyon (gelir)    |
| `nostro`           | NULL              | banka        | Evet               | Kendi banka hesabımızdaki gerçek para |
| `provider_expense` | NULL              | sağlayıcı    | Evet               | Sağlayıcıya ödenen ücret (gider)      |

`account_type` hesabın ledger'daki rolü, `owner_type` sahibinin kim olduğu, `provider`
sistem hesabının hangi dış tarafa ait olduğu. Üçü de dik boyut, birleştirilmez:
ledger çekirdeği yalnızca `account_type`'a bakar, policy katmanı `owner_type`'a,
mutabakat `provider`'a. Gerekçe: `decisions.md` madde 14.

`revenue` ve `provider_expense` ayrı tutulur, netleştirilmez. Biri gelir biri gider;
compensation'da `revenue` ters kayıtla iade edilir, `provider_expense` edilmez
(banka işlemi denediyse ücreti kesilmiştir).

Sistem hesapları seed migration ile oluşturulur: `revenue` currency başına bir tane,
`clearing` / `nostro` / `provider_expense` ise **sağlayıcı × currency** başına bir tane.
Birden fazla sağlayıcı varsa mutabakat ancak böyle ayrıştırılabilir.

`ck_accounts_ownership`, `Account.UserWallet()` / `Account.System()` factory'lerinin DB
tarafındaki eşidir — ikisi aynı kuralı söyler. Hesap yalnızca uygulamadan açılmıyor:
seed migration, düzeltme script'i, ileride bir admin endpoint'i. Kural tek tarafta
kalırsa diğer yoldan geçersiz satır giriyor ve hiçbir yerde hata görünmüyor
(zero-sum bozulmadığı için trigger da susuyor).

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
    account_id       uuid NOT NULL REFERENCES accounts(id),  -- idempotency KAPSAMI (aşağıya bak)
    idempotency_key  text NULL,
    correlation_id   uuid NULL,           -- saga / webhook event ilişkisi
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_ledger_tx_idem
    ON ledger_transactions (account_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;
```

Partial unique index: idempotency key'siz iç işlemler çakışmaz.

`account_id` "isteği başlatan hesap" değil, **işlemin idempotency kapsamı olan hesap**.
İç işlemlerde de doludur — nullable OLMAZ, çünkü unique index içindeki NULL hiçbir NULL'a
eşit sayılmaz ve aynı fatura iki kez yazılabilir hale gelir (`decisions.md` madde 15):

| `type`             | `account_id`                          | `idempotency_key`   |
| ------------------ | ------------------------------------- | ------------------- |
| transfer (5 tip)   | gönderen `user_wallet`                | client'ın key'i     |
| `topup`            | alıcı `user_wallet`                   | webhook `event_id`  |
| `withdrawal`       | çeken `user_wallet`                   | client'ın key'i     |
| `refund`           | aynı `user_wallet`                    | saga id             |
| `settlement`       | ilgili `clearing` (sağlayıcı bazında) | sağlayıcı batch ref |
| `provider_invoice` | ilgili `provider_expense`             | fatura numarası     |

## ledger_entries

```sql
CREATE TABLE ledger_entries (
    id              bigserial PRIMARY KEY,
    transaction_id  uuid NOT NULL REFERENCES ledger_transactions(id),
    account_id      uuid NOT NULL,
    amount          numeric(19,4) NOT NULL CHECK (amount <> 0),
    currency        char(3) NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),

    -- Composite FK, tek kolonluğun yerine geçer: hesabın var olduğunu da garantiler,
    -- entry'nin currency'sinin hesabınkiyle aynı olduğunu da. currency FK'ya KASTEN
    -- gereksiz kolon olarak konuyor — soru "hesap var mı" değil, "ikisi aynı satırda
    -- birlikte mi duruyor" (decisions.md madde 17).
    CONSTRAINT fk_ledger_entries_account
        FOREIGN KEY (account_id, currency) REFERENCES accounts (id, currency)
);

CREATE INDEX ix_ledger_entries_account ON ledger_entries (account_id, id);
CREATE INDEX ix_ledger_entries_tx ON ledger_entries (transaction_id);
```

`amount` işareti yönü taşır: credit `+`, debit `-`. Ayrı `direction` kolonu yok —
iki kaynak (işaret + direction) tutarsızlaşabilir, tek kaynak bırakıldı.

`currency` hesapta da duruyor, burada da — bilinçli tekrar, ledger sorgularının para
birimini öğrenmek için `accounts`'a join olmasını engelliyor. Tekrarı güvenli kılan şey
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

## wallet_balances

```sql
CREATE TABLE wallet_balances (
    account_id  uuid PRIMARY KEY,
    balance     numeric(19,4) NOT NULL DEFAULT 0,
    currency    char(3) NOT NULL,
    version     bigint NOT NULL DEFAULT 0,
    updated_at  timestamptz NOT NULL DEFAULT now(),

    -- ledger_entries ile aynı gerekçe: projeksiyonun para birimi hesabınkinden sapamaz.
    CONSTRAINT fk_wallet_balances_account
        FOREIGN KEY (account_id, currency) REFERENCES accounts (id, currency)
);
```

Ledger'dan türetilmiş projeksiyon. Source of truth `ledger_entries`; bu tablo her zaman
yeniden inşa edilebilir, tersi geçerli değil.

EF Core: `version` üzerinde `IsConcurrencyToken()`. Başka hiçbir entity'de concurrency token yok.

### Doğrulama sorgusu (mutabakat job'ı bunu koşar)

```sql
SELECT b.account_id, b.currency, b.balance, COALESCE(SUM(e.amount), 0) AS derived
  FROM wallet_balances b
  LEFT JOIN ledger_entries e
    ON e.account_id = b.account_id
   AND e.currency   = b.currency
 GROUP BY b.account_id, b.currency, b.balance
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
- Fatura özeti ayrı tablo gerektirmez; `invoice_ref` ile grupla.

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
  SELECT balance, version FROM wallet_balances WHERE account_id = @from;
  -- policy: limit kontrolü, komisyon hesabı  → ihlal varsa 422, hiç yazma

  INSERT INTO ledger_transactions (id, type, account_id, idempotency_key) VALUES (...)
    ON CONFLICT (account_id, idempotency_key) DO NOTHING;
  -- 0 satır → mevcut tx'i oku ve dön, yeni transfer YAPMA

  INSERT INTO ledger_entries (transaction_id, account_id, amount, currency) VALUES
    (@tx, @from,    -102, 'TRY'),
    (@tx, @to,      +100, 'TRY'),
    (@tx, @revenue,   +2, 'TRY');

  -- account_id ARTAN SIRAYLA (deadlock önleme)
  UPDATE wallet_balances SET balance = balance + @delta, version = version + 1,
         updated_at = now()
   WHERE account_id = @acc AND version = @readVersion;
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