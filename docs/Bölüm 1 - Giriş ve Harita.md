Bir .NET backend developer'ın işte/mülakatta karşılaşacağı konseptleri gösteren referans uygulamalar (reference implementations). Hepsi ortak bir **baseline** üstüne kurulur; her biri bir **dominant teknik temayı** öne çıkarır.

İçindekiler:
- Bölüm 2 — Baseline (ortak çatı)
- Bölüm 3 — Wallet Distributed

Teknik baz: .NET controller-based Web API, PostgreSQL, Redis (gerekirse), RabbitMQ (dağıtık olanlarda), Docker Compose.

**Felsefe — mimari production seviyesi, kapsam sınırlı.** Amaç, mimari ve yaklaşımları gerçekte nasıl yapılıyorsa öyle kurmak: katman ayrımı, transaction sınırları, idempotency, concurrency stratejisi, error handling, double-entry invariant'ı, saga compensation, observability — bunlarda taviz yok, production kalitesinde.

Sınırlı olması yalnızca **kapsamı ve dış bağımlılıkları** kısaltır, mimariyi değil:

- Dış servisler simüle edilir (KYC `true` döner, fraud-check fake, Stripe/banka `provider-fake`). Ama her fake bir **interface arkasında** durur (`IKycService`, `IPaymentProvider`) — yarın gerçek implementasyon takılınca üst akış değişmez. Fake bile production mimarisine uygun (geçici hack değil, interface'li stub).
- Kapsam daraltılır (biletlemede etkinlik yönetimi yok, tek instance yeter).
- Her katman "gösterilebilir en sade hali" ile alınır — ama varlığı ve doğru kurgusu görünür.

---

| Uygulama             | Baseline                   | Dominant tema                                                                                                                                            |
| -------------------- | -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Wallet — Distributed | 1–12                       | In-process consistency + optimistic lock + double-entry ledger.<br>Servisler arası consistency, saga orchestration, webhook delivery, scheduled raporlar |

**Notlar:**

- Webhook delivery ayrı uygulama değil; Wallet — Distributed içindeki top-up akışında yaşıyor.
- Background jobs (mutabakat, business özeti, stuck saga taraması) Wallet — Distributed'a iliştirilmiştir.
- Distributed lock ve CQRS ayrı uygulama değil; Biletleme'de gerçek senaryolarıyla gösteriliyor (seat hold = lock, seat availability = CQRS read modeli).
- Multi-tenancy Auth uygulamasının konusudur. Wallet tek-tenant'tır (bir e-money şirketinin iç sistemi); içindeki person/business ayrımı tenancy değil, hesap tipidir.
- Caching wallet uygulamalarının konusu değildir; bakiye projeksiyonu kalıcı bir read tablosudur, cache değil.