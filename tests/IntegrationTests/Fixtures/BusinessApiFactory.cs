using HiWallet.BusinessApi;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>İşyerinin sistem entegrasyonunun ön API'si.</summary>
public sealed class BusinessApiFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null,
    int? rateLimitBurst = null)
    : EdgeApiFactory<BusinessApiApp>(
        TestTokens.BusinessAudience, walletApi, withdrawalOrchestrator, rateLimitBurst);
