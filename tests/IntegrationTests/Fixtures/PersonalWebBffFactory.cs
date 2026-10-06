using HiWallet.PersonalWebBff;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Bireysel web uygulamasının BFF'i: müşterinin realm'i.</summary>
public sealed class PersonalWebBffFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null,
    HttpMessageHandler? onboarding = null,
    HttpMessageHandler? cardTopup = null)
    : BffFactory<PersonalWebBffApp>(
        TestTokens.Issuer, "personal-web", staff: false, walletApi, withdrawalOrchestrator, onboarding,
        cardTopup: cardTopup);
