using HiWallet.WalletService.Application.Abstractions;
using HiWallet.WalletService.Domain.Accounts;
using HiWallet.WalletService.Domain.Errors;
using HiWallet.WalletService.Domain.Ledger;
using HiWallet.WalletService.Domain.Promos;
using HiWallet.WalletService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiWallet.WalletService.Application.Promos;

/// <summary>
/// Kampanya açma (decisions.md madde 37). Kuralın parçalarının birbiriyle uyumunu
/// <see cref="PromoCampaign.Create"/> kontrol ediyor; uyumsuz tanım <c>400</c>.
/// </summary>
/// <param name="EmployeeSubject">Açan çalışanın <c>sub</c>'ı.</param>
public sealed record CreatePromoCampaignCommand(
    string Name,
    PromoCampaignRule Rule,
    decimal? ThresholdAmount,
    PromoRewardType RewardType,
    decimal? RewardAmount,
    decimal? RewardRate,
    decimal? RewardMax,
    string Currency,
    PromoScope GrantScope,
    TimeSpan? GrantValidFor,
    decimal Budget,
    decimal DailyCapPerAccount,
    decimal TotalCapPerAccount,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyList<Guid> TriggerMerchantAccountIds,
    IReadOnlyList<Guid> ScopeMerchantAccountIds,
    string EmployeeSubject);

/// <summary>Kampanyayı şimdi bitiriyor; verilmiş partiler olduğu gibi kalıyor.</summary>
public sealed record EndPromoCampaignCommand(Guid CampaignId, string EmployeeSubject);

public sealed class PromoCampaignCommandHandler(IDbContextFactory<WalletDbContext> contextFactory, IClock clock)
{
    public async Task<PromoCampaignView> HandleAsync(CreatePromoCampaignCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var currency = Currency.From(command.Currency);

        // Kampanyanın verdiği parti promo_expense'ten çıkıyor; o para biriminde hesap
        // yoksa kampanya hiçbir şey veremez.
        if (!await db.LedgerAccounts.AnyAsync(a => a.Type == LedgerAccountType.PromoExpense && a.Currency == currency, ct))
        {
            throw new PromoGrantRejectedException($"{currency.Code} için promo gider hesabı yok.");
        }

        var merchants = command.TriggerMerchantAccountIds.Concat(command.ScopeMerchantAccountIds).Distinct().ToArray();
        var businesses = await db.Accounts.CountAsync(a => merchants.Contains(a.Id) && a.Type == AccountType.Business, ct);

        if (businesses != merchants.Length)
        {
            throw new AccountRuleException("Kampanyadaki her hesap bir işyeri hesabı olmalı.");
        }

        PromoCampaign campaign;

        try
        {
            campaign = PromoCampaign.Create(
                Guid.NewGuid(),
                command.Name.Trim(),
                command.Rule,
                command.ThresholdAmount,
                command.RewardType,
                command.RewardAmount,
                command.RewardRate,
                command.RewardMax,
                currency,
                command.GrantScope,
                command.GrantValidFor,
                command.Budget,
                command.DailyCapPerAccount,
                command.TotalCapPerAccount,
                command.StartsAt,
                command.EndsAt,
                command.TriggerMerchantAccountIds,
                command.ScopeMerchantAccountIds,
                clock.UtcNow,
                command.EmployeeSubject);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDefinitionException(exception.Message, exception);
        }

        db.PromoCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return PromoCampaignView.From(campaign, granted: 0m);
    }

    public async Task<PromoCampaignView> HandleAsync(EndPromoCampaignCommand command, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var campaign = await db.PromoCampaigns
                           .Include(c => c.Merchants)
                           .FirstOrDefaultAsync(c => c.Id == command.CampaignId, ct)
                       ?? throw new PromoCampaignNotFoundException(command.CampaignId);

        campaign.End(command.EmployeeSubject, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        var granted = await PromoCampaignQueryHandler.GrantedAsync(db, [campaign.Id], ct);

        return PromoCampaignView.From(campaign, granted.GetValueOrDefault(campaign.Id));
    }
}
