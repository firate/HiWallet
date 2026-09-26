using HiWallet.PersonalMobileApi;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Bireysel mobil uygulamanın ön API'si.</summary>
public sealed class PersonalMobileApiFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null,
    int? rateLimitBurst = null)
    : EdgeApiFactory<PersonalMobileApiApp>(
        TestTokens.PersonalMobileAudience, walletApi, withdrawalOrchestrator, rateLimitBurst);
