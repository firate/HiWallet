using HiWallet.PersonalMobileApi;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Bireysel mobil uygulamanın ön API'si.</summary>
public sealed class PersonalMobileApiFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null,
    int? rateLimitBurst = null,
    HttpMessageHandler? onboarding = null)
    : EdgeApiFactory<PersonalMobileApiApp>(
        TestTokens.PersonalMobileAudience, walletApi, withdrawalOrchestrator, rateLimitBurst, onboarding);
