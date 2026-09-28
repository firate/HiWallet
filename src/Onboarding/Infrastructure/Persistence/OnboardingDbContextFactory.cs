using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.Onboarding.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
/// Bağlantı dizesi yoksa yer tutucu: <c>migrations add</c> ve <c>bundle</c> bağlanmıyor.
/// </summary>
internal sealed class OnboardingDbContextFactory : IDesignTimeDbContextFactory<OnboardingDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_onboarding;Username=onboarding_owner";

    public OnboardingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__OnboardingOwner")
            ?? PlaceholderConnectionString;

        return new OnboardingDbContext(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseNpgsql(connectionString)
            .Options);
    }
}
