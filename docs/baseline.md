# Baseline — zorunlu katmanlar

Bu dosya HiWallet'ın **ne yaptığından bağımsız** olan kısmı anlatır.
Double-entry ledger, saga, idempotency — bunlar sistemin konusu ve `overview.md`'de.
Aşağıdaki 12 katman ise sistem ne olursa olsun aynı şekilde durur: her serviste,
her akışta beklenen production hijyeni.

Her servis bu 12 katmanı içerir. Dominant tema bunun **üstüne** eklenir, yerine değil.

## Zorunlu Katmanlar

### 1. Configuration & Secrets

- `appsettings.json` + environment variable override.
- Secret koda gömülmez; connection string vb. env'den gelir.
- Startup'ta basit config validation: kritik config eksikse uygulama ayağa kalkmadan patlar (fail fast).
- **Kapsam:** Vault/secret manager yok. `.env` + Docker Compose yeterli. (Üretimde böyle yapılmaz: local'de User Secrets, prod'da Vault/secret manager; `.gitignore` + gitleaks ile sızıntı koruması.)

### 2. Structured Logging

- `ILogger<T>` (Microsoft.Extensions.Logging) + OpenTelemetry Logs. **Serilog yok.**
- Log'lar OTLP ile export edilir; trace_id/span_id log record'una otomatik gömülür (korelasyon bedava).
- Doğru log seviyeleri (Information / Warning / Error).
- **Kapsam:** Makine kanalı OTLP → Collector → Loki. İnsan kanalı local'de ayrı `AddSimpleConsole` (terminalde okunur format).

### 3. Observability — Traces & Metrics

- OpenTelemetry: distributed tracing + temel metrikler (request count, latency, error rate — RED).
- Auto-instrumentation (ASP.NET Core, HttpClient, Npgsql).
- Log–trace korelasyonu: OTel logs sayesinde trace_id otomatik eşleşir; Grafana'da log'dan Tempo trace'ine atlanabilir.
- **Kapsam:** OTLP exporter → OTel Collector → Loki/Tempo/Prometheus → Grafana. Mevcut bir gözlemlenebilirlik stack'ine bağlanır.

### 4. Health Checks

- `/health/live` (process ayakta mı) ve `/health/ready` (DB/cache/broker hazır mı) ayrımı.
- Readiness gerçek bağımlılıkları kontrol eder.
- **Kapsam:** Built-in `AddHealthChecks()` + ilgili check paketleri yeterli.

### 5. Error Handling

- Global exception handling middleware.
- Tutarlı error response: RFC 7807 ProblemDetails.
- Beklenen hatalar (validation, not found) ile beklenmeyen (unhandled) ayrımı; internal detay client'a sızmaz.
- **Kapsam:** Birkaç custom exception tipi + tek merkezi handler yeterli.

### 6. Input Validation

- Sınırda doğrulama (FluentValidation).
- Geçersiz girdi domain'e ulaşmadan reddedilir, anlamlı mesaj döner.
- **Kapsam:** Sadece kullanılan endpoint'lerin DTO'ları için.

### 7. Rate Limiting

- ASP.NET Core built-in rate limiting middleware (fixed/sliding window veya token bucket).
- Limit aşımında `429` + `Retry-After`.
- **Kapsam:** In-memory limiter yeterli. Çok instance'ta efektif limit instance başına düşer; dağıtık limiter Redis gerektirir ve bu sistemin konusu değil (`decisions.md` madde 12).

### 8. Data Layer Discipline

- Migration ile şema versiyonlama (EF Core migrations).
- Connection pooling (Npgsql default).
- Net transaction sınırları.
- Concurrency stratejisi açık — burada optimistic lock, çünkü dominant temanın parçası (`decisions.md` madde 2).
- Unbounded query yok; liste dönen endpoint'lerde pagination.
- **Kapsam:** Repository soyutlaması değer katıyorsa eklenir, yoksa doğrudan DbContext.

### 9. API Contract

- Versiyonlama: `/v1` prefix.
- Liste endpoint'lerinde pagination.
- OpenAPI dokümanı `Microsoft.AspNetCore.OpenApi` ile üretilir — framework'ün kendi
  üreteci. Swashbuckle KULLANILMAZ: .NET 9'dan beri şablonlardan da çıkarıldı ve aynı
  işi üçüncü parti bir pakete yaptırmanın karşılığı yok.
- Arayüz Scalar. Üreteç yalnızca JSON dokümanı veriyor, UI ayrı bir bağımlılık.
- İkisi de YALNIZCA Development'ta açılır. Üretimde API yüzeyinin şemasını yayınlamak
  saldırgana harita vermektir; kapı varsayılan olarak kapalı.
- Tutarlı response yapısı (zarf var ya da bilinçli olarak yok — ama tutarlı).
- **Kapsam:** İstemcisi olan iki serviste (`wallet-api`, `withdrawal-orchestrator`)
  açık. `topup-webhook`'ta YOK: sözleşmeyi sağlayıcı dayatıyor, biz belgelemiyoruz.

### 10. Graceful Shutdown

- SIGTERM'de yeni istek alımı durur, in-flight işler biter.
- Background worker'lar (varsa) temiz kapanır (`IHostedService` + `CancellationToken`).
- **Kapsam:** .NET host'un default graceful shutdown davranışı + worker'larda token'a uyum yeterli.

### 11. Resilience (timeout / retry / circuit breaker)

- Dış bağımlılıklar için Polly: timeout, retry (yalnız idempotent çağrılarda), circuit breaker.
- Bir bağımlılığın çökmesi tüm servisi kilitlemez.
- **Kapsam:** `Microsoft.Extensions.Http.Resilience` (Polly v8) ile standart resilience handler yeterli. Dış bağımlılık yoksa bile DB/cache çağrılarında timeout uygulanır.

### 12. Idempotency & Consistency

- Yazma işlemlerinde idempotency key; at-least-once mesajlaşmada dedup; gerektiğinde Outbox.
- Aynı isteğin/aynı mesajın iki kez işlenmesi engellenir.
- **Kapsam:** Idempotency key ilgili tablonun kolonu, ayrı store yok (`decisions.md` madde 4). Outbox sadece async yayın yapanlarda devreye girer.
- **Not.** Bu katman burada baseline'dan çıkıp **ana konu** oluyor: idempotency + inbox/outbox/relay + saga consistency dominant temanın kendisi. Ayrıntı `overview.md` madde 5 ve 6.

## Opsiyonel Katman

### A. Authentication & Authorization (katman olarak)

- Authn (kim) ve authz (ne yapabilir) ayrımı, token validation.
- **Neden opsiyonel:** Dominant tema güvenlik olmadığında endpoint'leri açık bırakıp odakta kalmak daha temiz.
- **Eklenirse:** JWT bearer + policy yeterli; token üretimi için merkezi IdP (Keycloak) kullanılır.
- **Kapsam:** HiWallet'ta yok (`decisions.md` madde 12). Bu katmanın kendisi — JWT/RS256+JWKS, permission-based authz, multi-tenancy, revocation — başlı başına bir konu ve burada baseline'ın da dışında.

## Çıkış kriteri

- Zorunlu katmanların hepsi görünür şekilde var (gerçekten çalışıyor, mock değil).
- Dominant tema en az bir senaryoda gösterilebiliyor.
- `docker compose up` ile ayağa kalkıyor.
- Trace bir uçtan diğerine takip edilebiliyor.
- README'de: ne gösteriyor, nasıl çalıştırılır, hangi senaryo nasıl tetiklenir.
