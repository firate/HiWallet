namespace HiWallet.TopupWebhook.Application;

/// <summary>
/// Rotasyon penceresinin gözlemi. Üç webhook endpoint'i de aynı şeyi kaydediyor,
/// bu yüzden tek yerde duruyor.
/// </summary>
internal static class WebhookSecretRotation
{
    /// <summary>
    /// Listenin ilk sırasından sonrasıyla doğrulanan her bildirim, sağlayıcının
    /// geçişi tamamlamadığının kanıtı: bu kayıt kesilmeden eski secret
    /// kaldırılmamalı. Rotasyon uyuşmazlığındaki <c>401</c> ile saldırganın aldığı
    /// <c>401</c> log'da aynı göründüğü için ayrıca yazılıyor.
    /// </summary>
    public static void LogIfOldSecret(ILogger logger, string provider, int match)
    {
        if (match <= 0) return;

        logger.LogWarning(
            "Webhook eski secret ile doğrulandı. Sağlayıcı {Provider}, secret sırası {Index}",
            provider, match);
    }
}
