namespace HiWallet.WalletService.Domain.Errors;

/// <summary>
/// İş kuralı reddinin makinenin okuyacağı adı. Müşterinin ekranı sebebi bundan kuruyor;
/// istisnanın mesajı hesap kimliği ve iç ayrıntı taşıyor, log ve destek için. wallet-api'nin
/// hata cevabı (<c>rule</c>) ve çekim reddi (<c>WithdrawalDebitRejected.Rule</c>) aynı adları
/// veriyor: iki ayrı eşleme olsaydı aynı ret iki yerde farklı adla görünürdü.
/// </summary>
public static class DomainRules
{
    public static string Of(DomainException exception) => exception switch
    {
        InsufficientFundsException => "insufficient_funds",
        TransferTypeMismatchException => "transfer_type_mismatch",
        LimitExceededException limit => limit.LimitName,
        IncomingLimitExceededException incoming => incoming.LimitName,
        // Müşterinin kendi yüklemesi: hangi limitin dolduğu ayrıntısı mesajda.
        CardTopupLimitExceededException => "card_topup_limit",
        KycLevelNotApplicableException => "kyc_level_not_applicable",
        UnsupportedCurrencyException => "unsupported_currency",
        PromoGrantRejectedException => "promo_grant_rejected",
        AccountRuleException => "account_rule",
        NoWalletInCurrencyException => "no_wallet_in_currency",
        WithdrawalRejectedException => "withdrawal_rejected",
        _ => "business_rule"
    };
}
