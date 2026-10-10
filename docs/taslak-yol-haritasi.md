# Taslak — projeye ne eklenebilir

Bu dosya **taslaktır**: buradaki hiçbir madde karar değil, tartışma girdisidir.
Bir madde yapılmaya karar verilirse gerekçesi `decisions.md`'ye yazılır ve buradan
çıkarılır. Yapılmayacağına karar verilirse de buradan çıkarılır — dosyada yalnızca
hâlâ açık olan başlıklar durur.

Kapsam dışı olduğu `CLAUDE.md` ve `decisions.md` madde 12'de kayıtlı olanlar burada
YOK: Vault, Kubernetes, gerçek ödeme sağlayıcısı, multi-tenancy, caching, dağıtık
rate limiting, karta iade yolu (madde 36).

---

## B. API yüzeyi

### B1. Hareket listesinde filtre

Bugün: `GET /v1/wallets/{id}/movements` yalnızca `after` ve `size` alıyor; cüzdanın
bütün geçmişi tek akış halinde dönüyor.

Eklenecek: tarih aralığı, işlem tipi, kova. Cursor mantığı aynı kalır — filtre
`WHERE`'e eklenir, sayfalama değişmez. Her filtre kendi index'ini isteyip istemediği
ile birlikte değerlendirilmeli; bugünkü `ix_ledger_entries_movements` yalnızca
`(ledger_account_id, id DESC)` taşıyor.

### B2. Hesap bazında ekstre

Bugün: hareket listesi cüzdan bazında. Bir hesabın birden fazla cüzdanı olabildiği
için (madde 20) hesabın tamamının dökümü tek çağrıyla alınamıyor.

Eklenecek: `GET /v1/accounts/{id}/movements`. Günlük limitin hesap bazında uygulanması
ile aynı gerekçe — müşterinin gördüğü birim hesap.

---

## C. Operasyon

### C1. CI yok

Bugün: `.github` dizini yok. Build ve testler yalnızca elle koşuyor.

Eklenecek: PR'da `dotnet build`, iki test projesi ve iki web uygulamasının testleri.
Integration testler Postgres istiyor — job içinde servis konteyneri olarak kaldırılır.
Bu, "her commit derlenmesin" kararıyla çelişmiyor: kural commit başına değil, PR başına.

### C2. Migration'ın canlı veriyle uyumu

Bugün: migration'lar veritabanlarına uygulanıyor, ama uygulanmış şemanın koddaki model
ile aynı olduğunu doğrulayan bir adım yok. 2026-09-23'te test ortamında tam bu ayrıştı:
image yeni koddu, `__EFMigrationsHistory` squash öncesindeki id'leri taşıyordu,
uygulamalar `500` dönüyordu.

Eklenecek: başlangıçta bekleyen migration varsa uygulamanın açılmaması. Bugün açılıyor
ve ilk isteğe kadar sessiz kalıyor. `decisions.md` madde 5'teki "fail fast" ile aynı
çizgi.

---

## D. Öneri sırası

| Sıra | Madde | Neden burada |
|---|---|---|
| 1 | C1 — CI | Diğer her maddenin altyapısı; en ucuzu |
| 2 | C2 — bekleyen migration'da açılmama | Yaşanmış bir arıza, tek kurulum satırı |
| 3 | B1 — hareket filtreleri | Var olan endpoint'in üstüne, yeni şema istemiyor |
| 4 | B2 — ekstre | Hareket listesinin cursor kalıbının tekrarı |

Sıra tartışmaya açık.
