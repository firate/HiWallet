using HiWallet.BackofficeBff;

namespace HiWallet.IntegrationTests.Fixtures;

/// <summary>Backoffice panelinin BFF'i: çalışanların realm'i.</summary>
public sealed class BackofficeBffFactory(
    HttpMessageHandler? walletApi = null,
    HttpMessageHandler? withdrawalOrchestrator = null,
    HttpMessageHandler? staffAdmin = null,
    HttpMessageHandler? cardTopup = null,
    HttpMessageHandler? onboarding = null)
    : BffFactory<BackofficeBffApp>(
        TestTokens.StaffIssuer, "backoffice", staff: true, walletApi, withdrawalOrchestrator, onboarding, staffAdmin,
        cardTopup);
