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
    currency      char(3) NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_accounts_owner ON accounts (owner_id) WHERE owner_id IS NOT NULL;
```

| account_type       | owner_type       | Negatife düşebilir | Anlamı                                  |
| ------------------ | ---------------- | ------------------ | --------------------------------------- |
| `user_wallet`      | person / business | Hayır             | Müşteri cüzdanı                         |
| `clearing`         | NULL             | Evet               | Yolda olan / settle olmamış para        |
| `revenue`          | NULL             | Evet               | Müşteriden alınan komisyon (gelir)      |
| `nostro`           | NULL             | Evet               | Kendi banka hesabımızdaki gerçek para   |
| `provider_expense` | NULL             | Evet               | Sağlayıcıya ödenen ücret (gider)        |

`account_type` hesabın ledger'daki rolü, `owner_type` sahibinin kim olduğu. Dik boyutlar,
birleştirilmez: ledger çekirdeği owner_type'a bakmaz, policy katmanı bakar.

`revenue` ve `provider_expense` ayrı tutulur, netleştirilmez. Biri gelir biri gider;
compensation'da `revenue` ters kayıtla iade edilir, `provider_expense` edilmez
(banka işlemi denediyse ücreti kesilmiştir).

Sistem hesapları seed migration ile oluşturulur, currency başına birer tane.
`nostro` ve `provider_expense` sağlayıcı başına ayrı olabilir
(`nostro/garanti`, `provider_expense/stripe`) — birden fazla sağlayıcı varsa mutabakat
ancak böyle ayrıştırılabilir.

## ledger_transactions

```sql
CREATE TABLE ledger_transactions (
    id               uuid PRIMARY KEY,
    type             text NOT NULL,       -- p2p, p2b, b2p, b2b, payment, topup, withdrawal, refund
    account_id       uuid NOT NULL REFERENCES accounts(id),  -- idempotency scope: isteği başlatan hesap
    idempotency_key  text NULL,
    correlation_id   uuid NULL,           -- saga / webhook event ilişkisi
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_ledger_tx_idem
    ON ledger_transactions (account_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;
```

Partial unique index: idempotency key'siz iç işlemler (compensation, settlement) çakışmaz.

## ledger_entries

```sql
CREATE TABLE ledger_entries (
    id              bigserial PRIMARY KEY,
    transaction_id  uuid NOT NULL REFERENCES ledger_transactions(id),
    account_id      uuid NOT NULL REFERENCES accounts(id),
    amount          numeric(19,4) NOT NULL CHECK (amount <> 0),
    currency        char(3) NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_ledger_entries_account ON ledger_entries (account_id, id);
CREATE INDEX ix_ledger_entries_tx ON ledger_entries (transaction_id);
```

`amount` işareti yönü taşır: credit `+`, debit `-`. Ayrı `direction` kolonu yok —
iki kaynak (işaret + direction) tutarsızlaşabilir, tek kaynak bırakıldı.

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
    total numeric(19,4);
BEGIN
    SELECT COALESCE(SUM(amount), 0) INTO total
      FROM ledger_entries
     WHERE transaction_id = NEW.transaction_id;

    IF total <> 0 THEN
        RAISE EXCEPTION 'Ledger transaction % is unbalanced: %', NEW.transaction_id, total;
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

## wallet_balances

```sql
CREATE TABLE wallet_balances (
    account_id  uuid PRIMARY KEY REFERENCES accounts(id),
    balance     numeric(19,4) NOT NULL DEFAULT 0,
    currency    char(3) NOT NULL,
    version     bigint NOT NULL DEFAULT 0,
    updated_at  timestamptz NOT NULL DEFAULT now()
);
```

Ledger'dan türetilmiş projeksiyon. Source of truth `ledger_entries`; bu tablo her zaman
yeniden inşa edilebilir, tersi geçerli değil.

EF Core: `version` üzerinde `IsConcurrencyToken()`. Başka hiçbir entity'de concurrency token yok.

### Doğrulama sorgusu (mutabakat job'ı bunu koşar)

```sql
SELECT b.account_id, b.balance, COALESCE(SUM(e.amount), 0) AS derived
  FROM wallet_balances b
  LEFT JOIN ledger_entries e ON e.account_id = b.account_id
 GROUP BY b.account_id, b.balance
HAVING b.balance <> COALESCE(SUM(e.amount), 0);
```

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
  "stripe-fake": { "FeeSettlement": "Net" },
  "bank-fake":   { "FeeSettlement": "Invoiced" }
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