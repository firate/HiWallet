# API örnekleri

Her endpoint için request ve **beklenen** response. Elle denemek ve bir şeyin bozulduğunu
anlamak için; sözleşmenin kaynağı kod, bu dosya ona uyar.

Response'lar compose'da koşan sistemden alındı (`localhost:8091-8096`, `8109`). Kimlikler
her koşuda değişir.

Adresler ve `docker compose` komutları stack'in koştuğu makineye ait; hepsi orada
çalışır.

```bash
set -a; . ./.env; set +a          # webhook secret'ları kabuğa gelsin
```

**Token.** `wallet-api`'nin, orchestrator'ın ve ön API'lerin uçları token istiyor;
aşağıdaki komutlar `$TOKEN`'ı kullanıyor. Kullanıcı açmak ve token almak
`verify-compose.md` "Kimlik" bölümünde. Hesabı açan kimlik hesabın kullanıcısı oluyor;
başka bir kimliğin token'ıyla aynı hesaba giden istek `404` alıyor. Token'sız istek `401`.

**Durum kodları neden bu şekilde:** `201` yaratıldı, `202` kalıcı olarak alındı ama
henüz işlenmedi, `400` girdi bozuk, `404` kayıt yok, `409` eşzamanlılık çakışması,
`422` request geçerli ama iş kuralı reddetti. `409` ile `422` karıştırılmaz — birincisi
"tekrar dene", ikincisi "tekrar denemenin faydası yok".

---

## wallet-api — `:8091`

### Hesap aç

```bash
curl -i -X POST localhost:8091/v1/accounts -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"type":"Business"}'
```

```
HTTP/1.1 201 Created
Location: http://localhost:8091/v1/accounts/d5e9df14-cd62-4ad5-b628-08df56e06daa
```
```json
{
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "accountNumber": "4817305925",
  "type": "Business",
  "kycLevel": null,
  "createdAt": "2026-09-06T12:41:03.117421+00:00"
}
```

`accountNumber` insanın kullandığı numara: on hane, son hanesi Luhn kontrol hanesi,
rastgele ve değişmiyor. Bireysel hesabı kayıt açıyor (`POST /v1/person-accounts`); onun
cevabında da numara var.

`type`: `Person` | `Business`. Sayı değil isim gönderilir — sayı olsaydı enum'a yeni
değer eklemek mevcut istemcilerin anlamını kaydırırdı.

Hesap para tutmaz. Bir hesabın aynı para biriminde birden fazla cüzdanı olabilir
(`decisions.md` madde 20); günlük limit bu yüzden cüzdan değil **hesap** bazında
uygulanır.

<details><summary>Geçersiz tip → <code>400</code></summary>

```bash
curl -i -X POST localhost:8091/v1/accounts -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"type":"Robot"}'
```
```
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
```
</details>

### Cüzdan aç

```bash
curl -i -X POST localhost:8091/v1/accounts/$ACCOUNT/wallets -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Birikim","currency":"TRY"}'
```

```
HTTP/1.1 201 Created
Location: http://localhost:8091/v1/wallets/23948ca4-7c69-4b0a-b8a1-eccdc87876a4
```
```json
{
  "walletId": "23948ca4-7c69-4b0a-b8a1-eccdc87876a4",
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "name": "Birikim",
  "currency": "TRY",
  "balance": 0.0000,
  "withdrawable": 0.0000,
  "balances": [
    { "fundType": "cash",  "balance": 0.0000 },
    { "fundType": "card",  "balance": 0.0000 },
    { "fundType": "promo", "balance": 0.0000 }
  ]
}
```

Açılışta üç kova da sıfır. Response'un şekli sorguyla aynı; kovalar açılışta
gizlenip sonra ortaya çıkmıyor.

`name` zorunlu: aynı hesabın aynı para birimindeki cüzdanları başka türlü ayırt
edilemiyor. Bakiye her zaman `0` başlar — para yalnızca ledger üzerinden girer.

<details><summary>Olmayan hesap → <code>404</code></summary>

```json
{
  "type": "https://hiwallet.dev/problems/account-not-found",
  "title": "Hesap bulunamadı",
  "status": 404,
  "detail": "Hesap bulunamadı: 00000000-...",
  "traceId": "..."
}
```
</details>

<details><summary>Sistem hesabı olmayan para birimi → <code>422</code></summary>

```bash
curl -i -X POST localhost:8091/v1/accounts/$ACCOUNT/wallets -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"name":"Dolar","currency":"USD"}'
```
```json
{
  "type": "https://hiwallet.dev/problems/business-rule",
  "title": "İşlem iş kuralı gereği reddedildi",
  "status": 422,
  "detail": "USD için sistem hesapları açılmamış; ... Desteklenen: TRY.",
  "rule": "unsupported_currency"
}
```

`400` değil `422`: `USD` geçerli bir ISO 4217 kodu, request kusursuz. Reddin sebebi
o para biriminde `clearing` ve `revenue` hesaplarının seed edilmemiş olması — bu
ledger'ın bilgisi, sınırdaki doğrulayıcı bilemez.
</details>

<details><summary>Boş ad → <code>400</code></summary>

```json
{
  "status": 400,
  "errors": { "Name": ["Cüzdan adı zorunlu."] }
}
```
</details>

### Cüzdanı sorgula

```bash
curl -s localhost:8091/v1/wallets/$WALLET -H "Authorization: Bearer $TOKEN"
```
```json
{
  "walletId": "23948ca4-7c69-4b0a-b8a1-eccdc87876a4",
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "name": "Birikim",
  "currency": "TRY",
  "balance": 500.0000,
  "withdrawable": 300.0000,
  "balances": [
    { "fundType": "cash",  "balance": 300.0000 },
    { "fundType": "card",  "balance": 150.0000 },
    { "fundType": "promo", "balance":  50.0000 }
  ]
}
```

Bakiye `ledger_balances` projeksiyonundan okunur, `ledger_entries` toplanarak değil.

**`balance` kovaların toplamı, `withdrawable` IBAN'a çıkabilen kısım**
(`decisions.md` madde 36). Yukarıdaki cüzdanda 500 TRY var ama çekime açık olan
300; kalan 150 kart ile yüklendiği, 50 de hediye bakiye olduğu için nakde
çevrilemiyor. İkisi ayrı dönüyor, yoksa müşteri çekimin neden reddedildiğini
göremezdi.

Kovalar sıfır olsalar da listede duruyor — yeni açılmış bir cüzdanda üçü de
`0` döner.

**Sistem hesabı bu endpoint'ten görünmez.** `revenue` ya da `clearing` kimliğiyle sorarsan
`404` dönerler — aynı tabloda duruyorlar ama iç muhasebe, public API'nin
cevaplayacağı soru değil.

### Cüzdanın hareketleri

```bash
curl -s "localhost:8091/v1/wallets/$WALLET/movements?size=2" -H "Authorization: Bearer $TOKEN"
```
```json
{
  "items": [
    {
      "movementId": 918274,
      "transactionId": "6b1f0c2e-...",
      "type": "withdrawal",
      "amount": -206.0000,
      "currency": "TRY",
      "fundType": "cash",
      "createdAt": "2026-09-22T14:10:55.201Z"
    },
    {
      "movementId": 918270,
      "transactionId": "a4c81d55-...",
      "type": "p2p",
      "amount": -102.0000,
      "currency": "TRY",
      "fundType": "cash",
      "createdAt": "2026-09-22T13:02:11.884Z"
    }
  ],
  "size": 2,
  "nextCursor": 918270
}
```

Kaynak `ledger_entries` — bakiye projeksiyonu değil, hareketin kendisi. `amount`
yön taşır: cüzdana giren `+`, çıkan `-`. Transferde gönderen tek satır görür ve o
satır komisyon dahil toplamı gösterir (yukarıda 100 + 2).

**Sayfalama cursor ile.** Bir sonraki sayfa `nextCursor`'ı `after` olarak
göndererek alınır:

```bash
curl -s "localhost:8091/v1/wallets/$WALLET/movements?size=2&after=918270" -H "Authorization: Bearer $TOKEN"
```

Son sayfada `nextCursor` `null` döner; istemci listenin bittiğini buradan anlar ve
ayrıca bir toplam sayı sorgusu koşulmaz.

Offset yerine cursor seçildi çünkü ledger append-only ve yeni satırlar listenin
**başına** giriyor. `OFFSET` ile iki sayfa arasında gelen bir top-up sayfayı
kaydırır, müşteri aynı kaydı iki kez görürdü. Ayrıca derin sayfada `OFFSET`
Postgres'e okunup atılacak satır saydırıyor; cursor `ix_ledger_entries_movements`
üzerinde tek arama yapıyor.

`size` tavanı 100. Daha büyüğü isteyen request reddedilmiyor, tavana çekiliyor ve
response'taki `size` gerçekte uygulanan değeri söylüyor.

Sistem hesapları bu endpoint'ten de görünmez: `revenue` kimliğiyle sorarsan `404`.

### Hesabı ve cüzdanlarını sorgula

```bash
curl -s localhost:8091/v1/accounts/$ACCOUNT -H "Authorization: Bearer $TOKEN"
```
```json
{
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "accountNumber": "4817305925",
  "type": "Person",
  "createdAt": "2026-09-06T12:41:03.117421+00:00",
  "wallets": [
    {
      "walletId": "23948ca4-...",
      "name": "Birikim",
      "currency": "TRY",
      "balance": 398.0000,
      "withdrawable": 198.0000,
      "balances": [
        { "fundType": "cash",  "balance": 198.0000 },
        { "fundType": "card",  "balance": 150.0000 },
        { "fundType": "promo", "balance":  50.0000 }
      ],
      "isDefault": true
    },
    {
      "walletId": "7c1e0b22-...",
      "name": "Harcama",
      "currency": "TRY",
      "balance": 0.0000,
      "withdrawable": 0.0000,
      "balances": [
        { "fundType": "cash",  "balance": 0.0000 },
        { "fundType": "card",  "balance": 0.0000 },
        { "fundType": "promo", "balance": 0.0000 }
      ],
      "isDefault": false
    }
  ]
}
```

Kırılım liste görünümünde de var: "neden çekemiyorum" sorusunun cevabı tek cüzdana
girmeden görünüyor. `isDefault`: hesap numarasına gelen TRY bu cüzdana düşüyor.

### Hesabı numarasıyla bul

```bash
curl -s localhost:8091/v1/accounts/by-number/4817305925 -H "Authorization: Bearer $TOKEN"
```

Cevap kimlikle sorgulamanın aynısı. Gruplama boşlukları kabul ediliyor
(`481%20730%205925`). Kontrol hanesi tutmayan numara `400`, olmayan numara ve
başkasının hesabı `404`. Çalışan `customer.view` izniyle her hesabı buluyor.

### Seviye limitleri

```bash
curl -s "localhost:8091/v1/accounts/$ACCOUNT/limits?currency=TRY" -H "Authorization: Bearer $TOKEN"
```
```json
{
  "accountId": "8f7c...",
  "kycLevel": "Unverified",
  "currency": "TRY",
  "periodStart": "2026-10-01T00:00:00+00:00",
  "movements": [
    { "movement": "IncomingTransfer", "limit": 5500, "used": 250, "remaining": 5250 },
    { "movement": "OutgoingTransfer", "limit": 0, "used": 0, "remaining": 0 },
    { "movement": "Payment", "limit": 5500, "used": 120, "remaining": 5380 },
    { "movement": "Withdrawal", "limit": 0, "used": 0, "remaining": 0 },
    { "movement": "Deposit", "limit": 5500, "used": 500, "remaining": 5000 },
    { "movement": "IncomingTotal", "limit": 5500, "used": 750, "remaining": 4750 }
  ],
  "balanceCap": 5500,
  "balance": 630
}
```

Seviyenin aylık limitleri ve bu ay kullanılanı; kullanım limit kontrolünün saydığıyla aynı.
`limit: 0` hareketin bu seviyede kapalı olduğu demek. Giden harekette kullanım cüzdandan
düşen, komisyon dahil; çekimde iade edilenler düşülmüş. `balanceCap` yalnızca kimliği
tespit edilmemiş seviyede. İşyeri hesabı `422` (`kyc_level_not_applicable`), başkasının
hesabı `404`; çalışan `customer.view` izniyle görüyor. Ön API'lerde aynı yol.

### Varsayılan cüzdanı değiştir

```bash
curl -i -X PUT localhost:8091/v1/accounts/$ACCOUNT/default-wallets/TRY \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "{\"walletId\":\"$SAVINGS\"}"
```
```
HTTP/1.1 204 No Content
```

Hesap numarasına gelen TRY bundan sonra bu cüzdana. Her para biriminde tam bir varsayılan
var; hesabın o para birimindeki ilk cüzdanı kendiliğinden varsayılan. Başka bir hesabın
cüzdanı `404`, başka para biriminin cüzdanı `422`. Müşterinin tercihi: çalışan `403`.

### Havaleyle yükleme bilgisi

```bash
curl -s localhost:8091/v1/accounts/$ACCOUNT/deposit-instructions -H "Authorization: Bearer $TOKEN"
```
```json
{
  "iban": "TR280009900000000000123456",
  "accountHolder": "Hive Elektronik Para A.Ş.",
  "reference": "4817305925",
  "currency": "TRY"
}
```

Toplama hesabı: bütün müşteriler aynı IBAN'a gönderiyor, açıklamaya yazılan `reference`
(hesap numarası) paranın kime ait olduğunu söylüyor. Gelen para varsayılan cüzdana düşüyor;
yalnızca müşterinin kendi adına kayıtlı hesabından gelen havale cüzdana geçiyor. İşyeri
hesabı `422`; başkasının hesabı `404`; çalışan `403`. IBAN ve alıcı adı `Deposits`
ayarında.

### Askıdaki havaleler (çalışan)

```bash
curl -s "localhost:8091/v1/suspended-deposits?size=20" -H "Authorization: Bearer $STAFF_TOKEN"
```
```json
{
  "items": [
    {
      "id": "…",
      "provider": "bank-fake",
      "bankReference": "GLN7C1E2A9F04B3D58E",
      "amount": 250.0000,
      "currency": "TRY",
      "reason": "sender_not_holder",
      "accountId": "…",
      "accountNumber": "4817305925",
      "receivedAt": "…",
      "createdAt": "…"
    }
  ],
  "size": 20,
  "nextCursor": null
}
```

Cüzdana geçirilemeyen havaleler, yeniden eskiye; `deposit.view` izni. `reason`:
`no_account_number`, `ambiguous_account_number`, `unknown_account`, `business_account`,
`no_wallet_in_currency`, `unknown_sender`, `sender_not_holder`, `limit_exceeded`.
`accountId` açıklamadaki numaranın hesabı, bulunduysa. Gönderenin adı, IBAN'ı ve kimlik
numarası wallet'ta olmadığı için burada da yok.

### Transfer

```bash
curl -i -X POST localhost:8091/v1/transfers -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: transfer-1' -H 'Content-Type: application/json' \
  -d "{\"fromWalletId\":\"$FROM\",\"toWalletId\":\"$TO\",\"amount\":200,\"currency\":\"TRY\",\"type\":\"Payment\"}"
```
```
HTTP/1.1 201 Created
```
```json
{ "transactionId": "...", "replayed": false }
```

Alıcı hesap numarasıyla da verilebiliyor; para alıcının o para birimindeki varsayılan
cüzdanına düşüyor:

```bash
curl -i -X POST localhost:8091/v1/transfers -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: transfer-2' -H 'Content-Type: application/json' \
  -d "{\"fromWalletId\":\"$FROM\",\"toAccountNumber\":\"4817305925\",\"amount\":50,\"currency\":\"TRY\",\"type\":\"P2P\"}"
```

`toWalletId` ya da `toAccountNumber`, ikisi birden değil (`400`). Olmayan numara `404`;
alıcının o para biriminde cüzdanı yoksa `422` ve `rule` `no_wallet_in_currency`.

`type`: `P2P` | `Payment` | `P2B` | `B2P` | `B2B`. Komisyon istenen tutara **ek**
olarak gönderenden düşülür: `Payment` %2 ise gönderen `-204`, alan `+200`,
`revenue` `+4`. Ledger'a üç satır düşer, toplamı sıfır.

`type` tarafların hesap tipiyle uyuşmak zorunda: `P2P` kişiden kişiye, `P2B` kişiden
işletmeye, `B2P` işletmeden kişiye, `B2B` işletmeden işletmeye, `Payment` işletmeye
(gönderen kişi de işletme de olabilir). Uyuşmazsa `422` ve ledger'a hiçbir şey yazılmaz:

```json
{ "status": 422, "rule": "transfer_type_mismatch", "traceId": "..." }
```

**`Idempotency-Key` ZORUNLU**; başlık yoksa `400` ve ledger'a hiçbir şey yazılmaz
(`decisions.md` madde 4). Anahtarsız bir tekrar hiçbir constraint'e takılmaz ve çift
harcama sessizce ledger'a düşerdi; append-only olduğu için de geri alınamaz, yalnızca
ters kayıtla düzeltilir.

Aynı anahtarla ikinci request yeni transfer yapmaz:

```json
{ "transactionId": "aynı-kimlik", "replayed": true }
```

<details><summary>Yetersiz bakiye → <code>422</code></summary>

```json
{ "status": 422, "rule": "insufficient_funds", "traceId": "..." }
```

Limit aşımında `rule` limitin adını taşır (`per_transaction`, `daily`).
Eşzamanlılık çakışmasında ise `409` döner ve `rule` yoktur — o bir iş kuralı reddi
değil, "tekrar dene" demek.
</details>

`Payment`'ta gönderenin alıcı işyerinde geçerli promo partileri önce harcanır ve
yalnızca tutarı karşılar; komisyon `card` ve `cash`'ten düşer. İşyerine promo payı
dahil tutarın tamamı `cash` olarak geçer (`decisions.md` madde 37). Gönderende 40 TL
geçerli promo varken 100 TL'lik `Payment` (%2 komisyon): gönderen `promo -40`,
`cash -62`; işyeri `cash +100`; `revenue +2`.

### Promo ver (işyeri)

İşyeri kendi müşterisine promo veriyor. İşyerinin `cash` kovası düşer, müşterinin
`promo` kovası artar; parti yalnızca bu işyerinde geçerli.

```bash
curl -i -X POST localhost:8091/v1/promos -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: promo-1' -H 'Content-Type: application/json' \
  -d "{\"funderWalletId\":\"$SHOP_WALLET\",\"walletId\":\"$WALLET\",\"amount\":50,\"currency\":\"TRY\",\"expiresAt\":\"2026-12-31T23:59:59+03:00\"}"
```
```
HTTP/1.1 201 Created
Location: http://localhost:8091/v1/wallets/<walletId>/promos
```
```json
{ "grantId": "...", "replayed": false }
```

`expiresAt` opsiyonel; verilmezse parti süresiz. Süresi dolan partinin kalanı
wallet-consumer'daki süre sonu işiyle işyerinin `cash` kovasına döner.

`Idempotency-Key` ZORUNLU; aynı anahtarla ikinci request yeni parti açmaz,
`replayed: true` döner.

<details><summary>Fonlayan işyeri değil → <code>422</code></summary>

```json
{ "status": 422, "rule": "promo_grant_rejected", "detail": "Promo'yu yalnızca işyeri hesabı fonlayabilir." }
```

İşyerinin kendi hesabındaki bir cüzdana promo vermesi de aynı `rule` ile reddedilir.
İşyerinin `cash` kovası yetmiyorsa `rule: insufficient_funds`; `card` kovası promo'yu
fonlamıyor.
</details>

### Cüzdanın promo partileri

```bash
curl -s "localhost:8091/v1/wallets/$WALLET/promos?size=2" -H "Authorization: Bearer $TOKEN"
```
```json
{
  "items": [
    {
      "grantId": "0b6e2f9a-...",
      "amount": 50.0000,
      "remaining": 50.0000,
      "currency": "TRY",
      "funder": "business",
      "scope": "selected_businesses",
      "merchantAccountIds": ["9c1d4e77-..."],
      "expiresAt": "2026-12-31T20:59:59+00:00",
      "expired": false,
      "createdAt": "2026-09-25T15:02:11.482+00:00"
    }
  ],
  "size": 2,
  "nextCursor": null
}
```

Cüzdanın toplam `promo` bakiyesi "bu işyerinde ne kadar kullanabilirim" sorusunu
cevaplamıyor; her partinin kalanı ve geçerli olduğu işyerleri burada. Sıra yeniden
eskiye, sayfalama `after=<grantId>` ile. `expired: true` olan parti ödemeye girmez;
kalanı süre sonu işi kapatana kadar bakiyede görünür.

### Kampanya, personel promo'su, işyerinin promo kabulü (çalışan)

Üçü de çalışanın token'ını ve kendi iznini istiyor: `merchant.promo_acceptance`, `promo.grant`, `campaign.manage`; görüntülemek `campaign.view` (`decisions.md` madde 37).
Çalışan token'ı tarayıcıda girişle alıyor, girişte OTP zorunlu; bu uçlar
`backoffice-bff` üzerinden aynı yollarla çağrılıyor. Müşterinin token'ıyla `403`.

| uç | ne yapıyor |
| --- | --- |
| `PUT /v1/accounts/{id}/accepts-promo` `{"acceptsPromo": true}` | işyeri platform promo'sunu kabul ediyor; `204`, bireysel hesapta `422` |
| `POST /v1/wallets/{id}/promos` + `Idempotency-Key` | personel promo'su: platform fonlu, `scope` `all_businesses` ya da `selected_businesses` + `merchantAccountIds`; `201` |
| `POST /v1/promo-campaigns` | kampanya açar; `201`, kural uyumsuzsa `400` |
| `GET /v1/promo-campaigns`, `GET /v1/promo-campaigns/{id}` | kampanyalar, verilen toplamla (`granted`); her çalışan görüyor |
| `POST /v1/promo-campaigns/{id}/end` | kampanyayı şimdi bitirir; verilmiş partiler etkilenmez |

Personel promo'su para birimi başına tek seferlik tavanla sınırlı
(`Promos:StaffGrant:MaxAmount`, TRY için 500); üstü `422`, büyük tutar kampanyayla verilir.

Kampanya gövdesi, bu işyerine yapılan her ödemede %5, en fazla 25 TL; parti her yerde
geçerli, 30 gün:

```json
{
  "name": "Kahvede yüzde 5",
  "rule": "payment_to_merchant",
  "rewardType": "percentage",
  "rewardRate": 0.05,
  "rewardMax": 25,
  "currency": "TRY",
  "grantScope": "all_businesses",
  "grantValidForDays": 30,
  "budget": 10000,
  "dailyCapPerAccount": 50,
  "totalCapPerAccount": 200,
  "startsAt": "<şimdi ya da sonrası>",
  "triggerMerchantAccountIds": ["<isyeri-hesap-id>"]
}
```

Günlük eşik kuralında `rule` `daily_payment_total`, `thresholdAmount` zorunlu ve ödül
`fixed` (`rewardAmount`). Kural ve alan uyumsuzluğu (örneğin `daily_payment_total`'da
yüzde ödül) `400` alıyor.

Kampanyanın promo'su ödemeden sonra wallet-consumer'daki `PromoCampaignJob` ile düşer;
iş dakikada bir koşuyor. Tabana yalnızca müşterinin `card` ve `cash` ile ödediği tutar
giriyor: promo payı ve komisyon sayılmıyor.

---

### Kartla yüklemenin limit payı

Bu ucu `card-topup` çağırıyor, müşterinin token'ını ileterek; elle çağırmaya gerek yok.
Ödeme açılmadan önce seviye limitinden pay ayırıyor.

```bash
curl -i -X POST localhost:8091/v1/card-topup-holds -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"holdId\":\"$(uuidgen)\",\"walletId\":\"$WALLET\",\"amount\":500,\"currency\":\"TRY\",\"provider\":\"stripe-fake\"}"
```
```
HTTP/1.1 201 Created
```
```json
{ "holdId": "…", "accountId": "…", "walletId": "…", "amount": 500, "currency": "TRY", "replayed": false }
```

Limit yetmiyorsa `422` ve `"rule": "card_topup_limit"`. Aynı `holdId` ile ikinci istek
yeni pay açmaz, `replayed: true` döner ve limiti yeniden sormaz. `Location` yok: payın
okuma ucu yok, durumu kart yüklemesi servisi tutuyor.

---

## topup-webhook — `:8092`

Kart sağlayıcısının ödeme bildirimi. Normalde `stripe-fake` gönderiyor; elle göndermek
için sağlayıcı rolünü sen oynuyorsun: gövdeyi imzalayıp gönderiyorsun. `reference`
kart yüklemesinin kimliği olmalı: bilinmeyen referansın bildirimi card-topup'ta
dead-letter'a gidiyor.

```bash
BODY="{\"eventId\":\"evt_1\",\"type\":\"payment.succeeded\",\"paymentId\":\"pay_1\",\"reference\":\"$CARD_TOPUP\",\"amount\":500.00,\"currency\":\"TRY\",\"occurredAt\":\"2026-09-06T10:00:00+00:00\"}"
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$STRIPE_FAKE_WEBHOOK_SECRET" -hex | awk '{print $2}')

curl -i -X POST localhost:8092/v1/webhooks/topup/stripe-fake \
  -H 'Content-Type: application/json' \
  -H "X-Hive-Signature: sha256=$SIG" \
  --data "$BODY"
```

```
HTTP/1.1 202 Accepted
```
```json
{ "accepted": true, "duplicate": false }
```

**`200` değil `202`, bilerek.** Verilen söz "işledim" değil "kalıcı kaydettim".
Response döndüğünde para henüz cüzdanda yok; hat webhook → inbox → relay → RabbitMQ →
card-topup → outbox → RabbitMQ → wallet-consumer → ledger. Birkaç saniye sonra
yüklemenin durumuna ve bakiyeye bak.

`type`: `payment.succeeded` (kart çekildi) ya da `payment.canceled` (müşteri vazgeçti).
Tutar ve para birimi yüklemeninkiyle aynı olmalı; değilse card-topup çelişki alarmı
üretir ve yükleme değişmez.

İmza **ham gövde baytları** üzerinde HMAC-SHA256. Gövdeyi yeniden serialize edersen
(boşluk, alan sırası) imza tutmaz. Her sağlayıcının kendi secret'ı var.

<details><summary>Tekrar eden event → yine <code>202</code></summary>

Aynı `eventId` ile ikinci request:

```json
{ "accepted": true, "duplicate": true }
```

Sağlayıcı için yeniden gönderim başarılı bir sonuçtur; hata dönmek onu sonsuza
kadar tekrar ettirirdi. Ayrım gövdedeki `duplicate` alanında.
</details>

<details><summary>Geçersiz imza / eksik başlık / tanınmayan sağlayıcı → <code>401</code></summary>

Üçü de `401`. Tanınmayan sağlayıcıya `404` DÖNÜLMEZ — hangi sağlayıcıların tanımlı
olduğunu dışarıya söylemek istemiyoruz.
</details>

---

## withdrawal-orchestrator — `:8093`

### Çekim başlat

```bash
curl -i -X POST localhost:8093/v1/withdrawals -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: cekim-1' -H 'Content-Type: application/json' \
  -d "{\"accountId\":\"$ACCOUNT\",\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"destinationIban\":\"TR330006100519786457841326\"}"
```

```
HTTP/1.1 202 Accepted
Location: http://localhost:8093/v1/withdrawals/cf13827f-470c-43af-a3b7-e3606e48c31d
```
```json
{
  "withdrawalId": "cf13827f-470c-43af-a3b7-e3606e48c31d",
  "state": "initiated",
  "replayed": false
}
```

**`Idempotency-Key` ZORUNLU** — transfer'de olduğu gibi. Burada bahis daha da yüksek:
çekim çok adımlı ve dışarıya para çıkarıyor, anahtarsız bir tekrar ikinci bir banka
transferi başlatırdı. Başlık yoksa `400`.

`202` dönüldüğünde **hiçbir para hareket etmedi**. IBAN boşluklu yazılabilir,
normalize edilir; mod-97 geçmezse `400`.

### Çekimi sorgula

```bash
curl -s localhost:8093/v1/withdrawals/$WD -H "Authorization: Bearer $TOKEN"
```
```json
{
  "withdrawalId": "cf13827f-470c-43af-a3b7-e3606e48c31d",
  "state": "completed",
  "amount": 100.0000,
  "currency": "TRY",
  "destinationIban": "TR33******************1326",
  "totalDebited": 102.0000,
  "failureReason": null,
  "createdAt": "2026-09-06T12:54:21.993862+00:00",
  "updatedAt": "2026-09-06T12:54:23.2473+00:00"
}
```

`state` sırası: `initiated` → `debited` → `bank_transfer_pending` → `settling` →
`completed`. `settling`, para bankadan çıktıktan sonra iç muhasebenin kapanmasını
bekliyor (clearing boşalıp `nostro`'ya yazılıyor) ve saniyeler sürüyor.
Telafi yolunda: `debited` → `compensating` → `failed`. Reddedilmişse `rejected`.

Tutarı inceleme eşiğinin üstündeki çekim (TRY için 10.000) `debited` → `under_review`'da
bir çalışanın kararını bekliyor. Kuyruk ve karar çalışanın uçları; kuyruk `customer.view`, karar `withdrawal.review` izniyle:

| uç | ne yapıyor |
| --- | --- |
| `GET /v1/withdrawals?state=under_review` | inceleme kuyruğu, en eski önce |
| `POST /v1/withdrawals/{id}/release` | serbest bırakır, banka komutu gider; `200`, incelemede değilse `422` |
| `POST /v1/withdrawals/{id}/cancel` `{"reason": "..."}` | iptal eder, para cüzdana döner; `202`, çekim `cancelling` → `cancelled` |

### Cüzdanın çekimleri

```bash
curl -s "localhost:8093/v1/wallets/$WALLET/withdrawals?size=20" -H "Authorization: Bearer $TOKEN"
```

Yeniden eskiye, sayfalama `after` (önceki sayfanın `nextCursor`'ı) ile; her eleman çekimin
sorgusundaki gövdeyle aynı. Orchestrator hesabın kullanıcılarını bilmiyor: müşteri yalnızca
kendi başlattığı çekimleri görüyor, başkasının cüzdanında liste boş. Çalışan
`customer.view` izniyle cüzdanın bütün çekimlerini görüyor. Ön API'lerde aynı yol.

`totalDebited` cüzdandan gerçekte çıkan toplam (tutar + komisyon). Wallet düşmeyi
yapana kadar `null` — `0` yazılmıyor, "komisyonsuz çekildi" ile karışırdı.

IBAN **maskeli** döner: müşteri zaten kendi girdi, tam hali response'ta dolaşınca log'a,
hata izlemeye ve tarayıcı geçmişine de düşer.

<details><summary>Aynı anahtarla tekrar → <code>202</code>, <code>replayed: true</code></summary>

```json
{ "withdrawalId": "aynı-kimlik", "state": "completed", "replayed": true }
```
Yeni çekim AÇILMADI.
</details>

<details><summary>Yetersiz bakiye / limit aşımı → saga <code>rejected</code></summary>

`POST` yine `202` döner — request geçerliydi ve kalıcı olarak alındı. Ret sonradan
ortaya çıkıyor:

```json
{
  "state": "rejected",
  "failureReason": "Cüzdan 2394... 50,00 TRY tutuyor, 102,00 TRY çekilemez.",
  "totalDebited": null
}
```

`failureReason` domain'in mesajı, makine tarafından ayrıştırılacak bir kod değil.
Kod isteyen bir istemci çıkarsa event'e ayrı bir alan eklenir.

Bu durum dead-letter'a GİTMEZ: cevapsız kalan saga müşteriyi sonsuza kadar
"işleniyor"da bırakırdı.
</details>

---

## card-topup — `:8109`

Kartla yükleme. İç servis: müşteri ön API'den geliyor (`personal-web-bff` ve
`personal-mobile-api` aynı uçları iletiyor), burada doğrudan da denenebilir.

### Kartla yükleme başlat

```bash
curl -i -X POST localhost:8109/v1/card-topups -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: kart-1' -H 'Content-Type: application/json' \
  -d "{\"walletId\":\"$WALLET\",\"amount\":500,\"currency\":\"TRY\",\"returnUrl\":\"http://localhost:8102/kart-yukleme\"}"
```
```
HTTP/1.1 202 Accepted
Location: http://localhost:8109/v1/card-topups/6f1c…
```
```json
{
  "cardTopupId": "6f1c…",
  "walletId": "…",
  "state": "pending",
  "amount": 500,
  "currency": "TRY",
  "paymentUrl": "http://localhost:8096/odeme/pay_…",
  "expiresAt": "2026-10-06T14:15:00+00:00",
  "failureReason": null,
  "replayed": false
}
```

Sırayla: yükleme kaydedildi, wallet-api'den limit payı alındı, sağlayıcıda ödeme açıldı.
Müşteri `paymentUrl`'e gidip kartını giriyor; ödeme sayfası onu `returnUrl`'e
`?cardTopupId=…` ile geri yolluyor. `202`: dönüldüğünde hiçbir para hareket etmedi.

- **Limit yetmiyorsa** wallet'ın `422`'si aynen geliyor (`"rule": "card_topup_limit"`) ve
  ödeme hiç açılmıyor. Aynı anahtarla tekrar: `202`, `"state": "rejected"`,
  `"failureReason": "card_topup_limit"`.
- **`Idempotency-Key` ZORUNLU**, yoksa `400`. Aynı anahtarla ikinci istek yeni yükleme
  açmaz, mevcut olanı `replayed: true` ile döner; yarım kalmış adım varsa (wallet-api ya da
  sağlayıcı cevap vermemişti) onu tamamlar.
- wallet-api'ye ya da sağlayıcıya ulaşılamazsa `503`; yükleme kaydı duruyor, tekrar devam
  ettiriyor.
- Çalışanın token'ı `403`: çalışan müşteri yerine para yüklemiyor.

### Yüklemenin durumu

```bash
curl -s localhost:8109/v1/card-topups/$CARD_TOPUP -H "Authorization: Bearer $TOKEN"
```
```json
{
  "cardTopupId": "6f1c…",
  "walletId": "…",
  "state": "paid",
  "amount": 500,
  "currency": "TRY",
  "paymentUrl": null,
  "expiresAt": "2026-10-06T14:15:00+00:00",
  "failureReason": null,
  "createdAt": "2026-10-06T14:00:00+00:00",
  "updatedAt": "2026-10-06T14:01:12+00:00"
}
```

| `state` | anlamı |
| --- | --- |
| `created` | kaydedildi, pay henüz onaylanmadı |
| `pending` | pay ayrıldı, ödeme açık |
| `paid` | kart çekildi; para cüzdana yazılıyor ya da yazıldı |
| `failed` | ödenmedi: `canceled`, `expired`, `payment_not_opened`, `abandoned`, `provider_rejected` |
| `rejected` | wallet payı vermedi; sebep wallet'ın kural adı |

Başkasının yüklemesi `404`. Çalışan `customer.view` izniyle her yüklemeyi görüyor.

---

## bank-fake (BİZİM DEĞİL) — `:8094`

Bankanın API'sinin yerinde duran servis; canlıda yok (`decisions.md` madde 35).
Senaryo endpoint'i gerçek bir bankada bulunmaz — varlık sebebi "banka reddetti" durumunun
denenebilmesi.

**Transfer sonucu artık senkron dönmüyor.** `POST /v1/transfers` `202 pending`
veriyor, kesin sonuç callback ile ya da `GET /v1/transfers/{ref}` ile sonra
öğreniliyor. Bu yüzden çekim saga'sı gerçekten `bank_transfer_pending`'de bekliyor.

### Senaryo kur

```bash
curl -i -X POST localhost:8094/v1/scenarios \
  -H 'Content-Type: application/json' \
  -d "{\"clientReference\":\"$WD\",\"outcome\":\"Failure\"}"
```
```
HTTP/1.1 204 No Content
```

`outcome`: `Success` | `Failure` | `TransientFailure` | `DelayedSuccess`.
`TransientFailure` için `transientFailures` (1-10, varsayılan 1) kaç kez geçici hata
üretileceğini, `DelayedSuccess` için `delayMilliseconds` (≤30000) gecikmeyi belirler.

`TransientFailure` ile `Failure` arasındaki fark kritik: birincisinde banka `503`
dönüyor ve **transfer hiç açılmıyor** (adaptör yeniden deniyor, saga bekliyor),
ikincisinde transfer açılıyor ama sonucu başarısız (saga telafiye giriyor).

Senaryo **çekim başına** kuruluyor ve anahtarı `clientReference` — bizim saga
kimliğimiz. Yani çekimi başlattıktan sonra kurman gerekiyor ve bu bir **yarış**:
zincir seni beklemiyor, banka senaryoyu transfer request'i geldiği anda okuyor.
Çekim request'inin hemen ardından aynı betikte kurarsan genelde yetişirsin; elle
kopyalayıp yapıştırırken geç kalırsın. Garantili yol varsayılanı değiştirmek:

```bash
BANK_DEFAULT_OUTCOME=Failure docker compose up -d --force-recreate --no-deps bank-fake
docker compose exec bank-fake printenv BankFake__DefaultOutcome
```

Geri almak için aynı komutu değişkensiz çalıştır.

**Senaryolar ve transferler bellekte** — sahte bankanın veritabanı yok. Yukarıdaki
gibi container'ı yeniden yaratmak hepsini siler. O anda `bank_transfer_pending`'de
bekleyen bir çekim varsa kapanmaz: mutabakat taraması sorduğunda banka onu artık
tanımıyor (`404`). Önce bekleyen çekimlerin bitmesini bekle.

### Senaryonun durumu

```bash
curl -s localhost:8094/v1/scenarios/$WD
```
```json
{
  "clientReference": "...",
  "outcome": "TransientFailure",
  "remainingTransientFailures": 0,
  "attempts": 2
}
```

`attempts` retry'ın gerçekten çalıştığının kanıtı. Kurulmamış çekim için `404`.

### Transfer endpoint'leri — adaptörün konuştuğu sözleşme

Bunları elle çağırman gerekmiyor; `bank-adapter` çağırıyor. Burada duruyorlar çünkü
**gerçek entegrasyonda bankanın dokümanından yazılacak kısım** tam olarak bu ikisi.

```bash
curl -i -X POST localhost:8094/v1/transfers \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d '{"clientReference":"<sagaId>","amount":100,"currency":"TRY","destinationIban":"TR330006100519786457841326"}'
```
```
HTTP/1.1 202 Accepted
```
```json
{ "bankReference": "BNK4F2A9C1E8B7D6A3", "status": "pending", "replayed": false }
```

**`status` her zaman `pending`.** Banka "aldım" diyor, "gönderdim" demiyor. Sonuç
callback ile ya da durum sorgusuyla sonra geliyor (`decisions.md` madde 35).

```bash
curl -s localhost:8094/v1/transfers/BNK4F2A9C1E8B7D6A3
```
```json
{
  "bankReference": "BNK4F2A9C1E8B7D6A3",
  "clientReference": "...",
  "status": "succeeded",
  "amount": 100.0000,
  "fee": 1.5000,
  "currency": "TRY",
  "failureReason": null,
  "acceptedAt": "..."
}
```

**Mutabakat taramasının okuduğu endpoint bu.** Bankanın böyle bir endpoint'i olmasaydı, callback'i
kaçırılan transferin sonucunu hiçbir şey öğrenemezdi.

`Idempotency-Key` başlıksız request `400`. Aynı anahtarla ikinci request yeni transfer
AÇMAZ: aynı `bankReference` ve `"replayed": true` döner.

`TransientFailure` senaryosunda endpoint `503` veriyor ve **transfer hiç açılmıyor** —
kalıcı hatadan farkı bu. Adaptör bunu yeniden deniyor, saga'ya hiçbir şey
bildirilmiyor.

### Gelen havale

Toplama hesabımıza havale gelmesini tetikliyor. **Gerçek bankada bu endpoint YOK**:
havaleyi müşteri kendi bankasından gönderir. Gönderenin bilgileri gönderen bankanın
mesajla taşıdığı haliyle.

```bash
curl -i -X POST localhost:8094/v1/incoming-transfers \
  -H 'Content-Type: application/json' \
  -d '{"amount":250,"currency":"TRY","description":"481 730 5925","senderName":"Ayşe Yılmaz","senderIban":"TR330006100519786457841326","senderNationalId":"10000000146"}'
```
```
HTTP/1.1 202 Accepted
```
```json
{ "bankReference": "GLN7C1E2A9F04B3D58E" }
```

Banka havaleyi `bank-webhook`'a `"type": "transfer.incoming"` ile bildiriyor. `"notify":
false` bildirimi göndermiyor: havale yalnızca hesap hareketlerinde görünüyor ve onu
`bank-adapter`'ın taraması buluyor.

Para cüzdana ancak açıklamadaki numaranın hesabının sahibi gönderdiyse geçiyor: compose'da
denerken `senderNationalId` müşterinin doğrulamada verdiği kimlik numarası olmalı. Başka
bir numarayla para askıya düşüyor.

### Hesap hareketleri

```bash
curl -s "localhost:8094/v1/incoming-transfers?from=2026-10-01T00:00:00Z&to=2026-10-05T00:00:00Z"
```
```json
{
  "items": [
    {
      "bankReference": "GLN7C1E2A9F04B3D58E",
      "amount": 250,
      "currency": "TRY",
      "description": "481 730 5925",
      "senderName": "Ayşe Yılmaz",
      "senderIban": "TR330006100519786457841326",
      "senderNationalId": "10000000146",
      "receivedAt": "..."
    }
  ]
}
```

**Hesap hareketi taramasının okuduğu endpoint bu.** Bildirimi kaçırılan havaleyi
bulmanın tek yolu.

---

## stripe-fake (BİZİM DEĞİL) — `:8096`

Kart sağlayıcısının yerinde duran servis; canlıda yok. Ödeme API'si, ödeme sayfası ve
sonucun webhook'u — Stripe'tan para çıkmadığı için ne transfer endpoint'i var ne callback
alıcısı. Ödemeler bellekte; yeniden başlatınca siliniyor.

### Ödeme aç

Bunu `card-topup` çağırıyor; elle denemek için:

```bash
curl -i -X POST localhost:8096/v1/payments -H 'Content-Type: application/json' \
  -d "{\"reference\":\"$(uuidgen)\",\"amount\":100,\"currency\":\"TRY\",\"returnUrl\":\"http://localhost:8102/kart-yukleme\",\"expiresAt\":\"2030-01-01T00:00:00+00:00\"}"
```
```
HTTP/1.1 201 Created
```
```json
{
  "id": "pay_…",
  "reference": "…",
  "status": "requires_payment",
  "amount": 100,
  "currency": "TRY",
  "expiresAt": "2030-01-01T00:00:00+00:00",
  "paymentUrl": "http://localhost:8096/odeme/pay_…"
}
```

Aynı `reference` ile ikinci istek yeni ödeme açmaz, ilkini `200` ile döner; farklı tutarla
gelirse `409`.

### Ödemeyi sorgula

```bash
curl -s "localhost:8096/v1/payments?reference=$CARD_TOPUP"
curl -s localhost:8096/v1/payments/pay_…
```

`status`: `requires_payment`, `succeeded`, `canceled`, `expired`. Bu referansla ödeme hiç
açılmadıysa `404`. card-topup'ın taraması oturumu kapanan yüklemeleri bu yoldan soruyor.

### Ödeme sayfası

`paymentUrl` tarayıcıda açılıyor: "Öde" ya da "Vazgeç". Karardan sonra sayfa müşteriyi
`returnUrl`'e yolluyor (`303`) ve sonucu `topup-webhook`'a imzalı webhook'la gönderiyor
(`payment.succeeded` ya da `payment.canceled`). Oturumun süresi dolan ödeme için webhook
GÖNDERMİYOR — gerçek sağlayıcılar gibi.

Banka havalesi bu yoldan gelmiyor: banka hesabımıza gelen parayı kendi bildirimiyle
`bank-webhook`'a bildiriyor (bkz. "bank-fake", "Gelen havale").

---

## bank-webhook — `:8095`

Bankanın transfer sonucunu ve hesabımıza gelen havaleyi bildirdiği endpoint. **Bizim
kodumuz**, canlıda da koşuyor; `bank-adapter`'dan ayrı bir deployable çünkü ingress'i var
(`decisions.md` madde 28). Gövdedeki `type` ikisini ayırıyor: `transfer.status` ya da
`transfer.incoming`; tipi olmayan bildirim transfer sonucu sayılıyor.

Elle çağırman gerekmiyor — `bank-fake` çağırıyor. İmza `topup-webhook`'unkiyle aynı
algoritma ama **ayrı bir sözleşme**: başlık adı `X-Bank-Signature` ve secret
`BANK_CALLBACK_SECRET`. Orada şemayı biz dayatıyoruz, burada bankanınkini uyguluyoruz.

```bash
BODY='{"eventId":"evt-BNK4F2A9C1E8B7D6A3","bankReference":"BNK4F2A9C1E8B7D6A3","clientReference":"...","status":"succeeded","fee":1.50,"currency":"TRY","failureReason":null,"occurredAt":"2026-03-01T10:00:00+00:00"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$BANK_CALLBACK_SECRET" -hex | awk '{print $2}')
curl -i -X POST localhost:8095/v1/webhooks/bank/bank-fake \
  -H 'Content-Type: application/json' -H "X-Bank-Signature: sha256=$SIG" --data "$BODY"
```
```
HTTP/1.1 202 Accepted
```
```json
{ "accepted": true, "duplicate": false }
```

`202`, `200` değil: verilen söz "işledim" değil "kalıcı kaydettim". Bu servis
**işlemiyor** — inbox'a yazıp bırakıyor, transferi kapatmak `bank-adapter`'daki
relay'in işi.

`eventId` tekrar denemelerde aynı kalmak zorunda; ikinci kez gelirse yine `202` ama
`"duplicate": true` ve satır ikinci kez yazılmıyor.

İmza tutmazsa `401` ve inbox'a **hiçbir şey** yazılmaz. Tanınmayan kurum da `401`,
`404` değil — hangi bankalarla çalıştığımız dışarıya sızmamalı. `eventId` yoksa
`400`: kimliksiz bir bildirim deduplike edilemez.

---

## Telafi yolu — asıl görülmesi gereken

Banka kalıcı olarak reddettiğinde para üç bacaklı ters kayıtla geri döner:

```bash
BEFORE=$(curl -s localhost:8091/v1/wallets/$WALLET -H "Authorization: Bearer $TOKEN" | jq -r .balance)

WD=$(curl -s -X POST localhost:8093/v1/withdrawals -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: cekim-red' -H 'Content-Type: application/json' \
  -d "{\"accountId\":\"$ACCOUNT\",\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"destinationIban\":\"TR330006100519786457841326\"}" | jq -r .withdrawalId)

# Hemen ardından: zincir bankaya varmadan senaryo kurulmuş olmalı. Yetişmezse
# çekim başarılı biter — o durumda BANK_DEFAULT_OUTCOME=Failure yolunu kullan
# (yukarıda, "Senaryo kur").
curl -s -X POST localhost:8094/v1/scenarios -H 'Content-Type: application/json' \
  -d "{\"clientReference\":\"$WD\",\"outcome\":\"Failure\"}"

# Banka sonucu ANINDA vermiyor: BANK_SETTLEMENT_DELAY kadar bekliyor, sonra
# callback gönderiyor, sonra adaptörün relay'i cevabı yayınlıyor.
sleep 10
curl -s localhost:8093/v1/withdrawals/$WD -H "Authorization: Bearer $TOKEN"; echo
echo "önce=$BEFORE sonra=$(curl -s localhost:8091/v1/wallets/$WALLET -H "Authorization: Bearer $TOKEN" | jq -r .balance)"
```

Beklenen: saga `failed`, bakiye **başladığı yerde**.

Ledger'da altı satır — `withdrawal` üçlüsü ve tam aynası `refund` üçlüsü:

```bash
docker compose exec postgres psql -U postgres -d hiwallet_wallet -c \
  "SELECT t.type, la.type AS hesap, e.amount FROM ledger_entries e
     JOIN ledger_accounts la ON la.id = e.ledger_account_id
     JOIN ledger_transactions t ON t.id = e.transaction_id
    WHERE t.correlation_id = '$WD' ORDER BY t.created_at, e.amount;"
```

```
    type    |    hesap    |  amount
------------+-------------+-----------
 withdrawal | user_wallet | -102.0000
 withdrawal | revenue     |    2.0000
 withdrawal | clearing    |  100.0000
 refund     | clearing    | -100.0000
 refund     | revenue     |   -2.0000
 refund     | user_wallet |  102.0000
```

`refund` satırındaki **`revenue -2` kritik**. O bacak atlansaydı kayıt yine dengeli
olurdu, trigger susardı, testler geçerdi — ama müşteri gerçekleşmemiş bir işlemin
komisyonunu ödemiş kalırdı. Ters kayıt bu yüzden politikadan yeniden ÜRETİLMİYOR:
orijinal işlemin bacakları okunup negatifleniyor.

---

## Health check endpoint'leri

```bash
curl -s localhost:8091/health/ready
```
```json
{
  "status": "Healthy",
  "durationMs": 0.4953,
  "checks": [
    { "name": "postgres", "status": "Healthy", "durationMs": 0.4189, "error": null }
  ]
}
```

**`checks` listesi uygulamanın bağımlılıklarını sayıyor.** `wallet-api` yalnızca
Postgres'e bağlanıyor. Broker bağlantısı `wallet-consumer`'da; mesajları o çekiyor
ve ledger'a o yazıyor (`decisions.md` madde 28).

Orchestrator ikisini birden sayıyor:

```bash
curl -s localhost:8093/health/ready
```
```json
{
  "status": "Healthy",
  "durationMs": 2.0093,
  "checks": [
    { "name": "postgres", "status": "Healthy", "durationMs": 0.4396, "error": null },
    { "name": "rabbitmq", "status": "Healthy", "durationMs": 1.9646, "error": null }
  ]
}
```

Broker durdurulduğunda bu endpoint `200` dönmeye devam eder, yalnızca `status` alanı
`Degraded` olur. Çekim request'i kabul edilmeye devam ediyor çünkü komut outbox'a
yazılıyor ve broker döndüğünde yayınlanıyor (`decisions.md` madde 26 ve 32).

Kalan endpoint'ler aynı gövdeyi döndürüyor, yalnızca `checks` içerikleri farklı:

```bash
curl -s localhost:8092/health/ready   # topup-webhook  — postgres + rabbitmq
curl -s localhost:8094/health/ready   # bank-fake      — checks BOŞ (canlıda yok)
curl -s localhost:8095/health/ready   # bank-webhook   — postgres
curl -s localhost:8096/health/ready   # stripe-fake    — checks BOŞ (canlıda yok)
curl -s localhost:8109/health/ready   # card-topup     — postgres + rabbitmq
```

Sahte kurumların `checks` listesi boş: ikisinin de veritabanı yok, sağlıklı olmaları
process'in ayakta olduğunu söylüyor.

`wallet-consumer`'ın host'a açılmış portu yok; onun health check'i container'ın
içinden koşuyor ve sonucu `docker compose ps` çıktısında `healthy` olarak görünüyor.
