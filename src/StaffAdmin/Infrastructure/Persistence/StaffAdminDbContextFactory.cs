using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef</c> tasarım zamanında kullanır. Uygulama çalışırken devreye girmez.
/// Bağlantı dizesi yoksa yer tutucu: <c>migrations add</c> ve <c>bundle</c> bağlanmıyor.
/// </summary>
internal sealed class StaffAdminDbContextFactory : IDesignTimeDbContextFactory<StaffAdminDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=connection-string-not-configured;Database=hiwallet_staff_admin;Username=staff_admin_owner";

    public StaffAdminDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__StaffAdminOwner")
            ?? PlaceholderConnectionString;

        return new StaffAdminDbContext(new DbContextOptionsBuilder<StaffAdminDbContext>()
            .UseNpgsql(connectionString)
            .Options);
    }
}
