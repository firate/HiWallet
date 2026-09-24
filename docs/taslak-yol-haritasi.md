# Taslak — projeye ne eklenebilir

Bu dosya **taslaktır**: buradaki hiçbir madde karar değil, tartışma girdisidir.
Bir madde yapılmaya karar verilirse gerekçesi `decisions.md`'ye yazılır ve buradan
çıkarılır. Yapılmayacağına karar verilirse de buradan çıkarılır — dosyada yalnızca
hâlâ açık olan başlıklar durur.

Kapsam dışı olduğu `CLAUDE.md` ve `decisions.md` madde 12'de kayıtlı olanlar burada
YOK: Vault, Kubernetes, gerçek ödeme sağlayıcısı, multi-tenancy, caching, dağıtık
rate limiting, karta iade yolu (madde 36).

---

## A. Kodda yarım duran iki şey

Bu ikisi diğerlerinden farklı: tip ve şema zaten var, yazan yol yok. Yani bugün
çalıştırılamayan kod taşıyoruz.

### A1. `promo` kovasına para giren yol yok

Bugün: `FundType.Promo` enum'da, `ledger_entries.fund_type` CHECK'inde ve her
cüzdanın `ledger_balances` satırlarında var. Yazan tek bir yol yok — sağlayıcı
tarifesi `Cash` ya da `Card` veriyor (`ProviderTerms.FundType`), transfer promo'yu
dağıtıma hiç almıyor, çekim yalnızca `cash` düşüyor. Kova her cüzdanda kalıcı
olarak sıfır.

Sonucu: `FundTypes.SpendOrder`'daki `promo → card → cash` sırasının ilk adımı hiçbir
akışta çalışmıyor. Harcama yolu da yok, yükleme yolu da.

Eklenecek: hediye bakiye yükleyen bir yol. Açık sorular — kim tetikliyor (operasyon
endpoint'i mi, kampanya tüketicisi mi), karşılığında hangi sistem hesabı borçlanıyor
(`promo_expense` gibi yeni bir hesap gerekiyor, `clearing` değil: karşılığında dışarıdan
para girmiyor), ve son kullanma tarihi olacak mı. Süre sonu varsa bakiyenin geri
alınması da ters kayıt demek ve `ledger_entries` append-only olduğu için bu tasarlanacak
bir akış, tek kolon değil.

### A2. `employee` aktörü hiçbir yerde üretilmiyor

Bugün: `ActorType.Employee` enum'da, `Actor.Employee(subject)` fabrikası yazılı,
`CommandActor.Employee` sözleşmede, value converter iki yönde de eşliyor. Üretim
kodunda `Actor.Employee(...)` çağıran tek bir satır yok. Ledger'a bugün yalnızca
`customer` ve `system` düşüyor.

Sonucu: madde 34'ün "kaydı kim başlattı" ayrımının üçte biri kayıtlı ama kullanılmıyor.

Eklenecek: çalışanın başlattığı işlem. En dar hali operasyon düzeltmesi — bir işlemin
ters kaydını yazan endpoint. Bu auth'a bağlı: `Actor.Employee`'nin taşıdığı `subject`
kimlik sağlayıcıdan gelen değer, uydurulamaz. Yani sıralamada auth'tan sonra.

---

## B. API yüzeyi

Bugünkü endpoint'ler:

| Servis | Endpoint |
|---|---|
| wallet-api | `POST /v1/accounts`, `GET /v1/accounts/{id}` |
| wallet-api | `POST /v1/accounts/{id}/wallets`, `GET /v1/wallets/{id}` |
| wallet-api | `GET /v1/wallets/{id}/movements` |
| wallet-api | `POST /v1/transfers`, `GET /v1/transfers/{id}` |
| withdrawal-orchestrator | `POST /v1/withdrawals`, `GET /v1/withdrawals/{id}` |

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

### B3. Çekim listesi

Bugün: `GET /v1/withdrawals/{id}` tek kayıt döner, liste yok. Müşteri geçmiş
çekimlerini göremiyor.

Eklenecek: cursor ile sayfalanan liste. Kaynak `withdrawal_sagas`; orchestrator'ın
kendi veritabanında, wallet'a sormadan.

---

## C. Operasyon

### C1. CI yok

Bugün: `.github` dizini yok. Build ve testler yalnızca elle koşuyor.

Eklenecek: PR'da `dotnet build` ve iki test projesi. Integration testler Postgres
istiyor — job içinde servis konteyneri olarak kaldırılır. Bu, "her commit derlenmesin"
kararıyla çelişmiyor: kural commit başına değil, PR başına.

### C2. Migration'ın canlı veriyle uyumu

Bugün: migration'lar dört veritabanına da uygulanıyor, ama uygulanmış şemanın koddaki
model ile aynı olduğunu doğrulayan bir adım yok. 2026-09-23'te homelab'da tam bu
ayrıştı: image yeni koddu, `__EFMigrationsHistory` squash öncesindeki id'leri
taşıyordu, uygulamalar `500` dönüyordu.

Eklenecek: başlangıçta bekleyen migration varsa uygulamanın açılmaması. Bugün açılıyor
ve ilk isteğe kadar sessiz kalıyor. `decisions.md` madde 5'teki "fail fast" ile aynı
çizgi.

### C3. Auth

Bugün: yok, ve bu `decisions.md` madde 12'de kayıtlı bir kabul.

Bağlı olan şeyler: A2 (employee aktörü), ve bugün `LimitExceededException`'ın `422`
gövdesinde hesabın o güne kadarki harcamasını açması — kimliği doğrulanmamış bir
çağırana verilmemesi gereken bilgi.

---

## D. Öneri sırası

| Sıra | Madde | Neden burada |
|---|---|---|
| 1 | C1 — CI | Diğer her maddenin altyapısı; en ucuzu |
| 2 | C2 — bekleyen migration'da açılmama | Yaşanmış bir arıza, tek kurulum satırı |
| 3 | B1 — hareket filtreleri | Var olan endpoint'in üstüne, yeni şema istemiyor |
| 4 | B2, B3 — ekstre ve çekim listesi | Aynı cursor kalıbının tekrarı |
| 5 | C3 — auth | Başlı başına bir konu, kendi kararını istiyor |
| 6 | A2 — employee aktörü | Auth'tan sonra anlamlı |
| 7 | A1 — promo yükleme | En çok açık soru burada: hangi hesap, süre sonu var mı |

Sıra tartışmaya açık. A1 ve A2'nin sonda olması önem sırası değil; ikisi de kendinden
önce bir karar bekliyor.
