namespace HiWallet.StaffAdmin.Domain;

/// <summary>Çalışanın bir rolü. Rol alındığında satır siliniyor.</summary>
public sealed class StaffRoleAssignment
{
    private StaffRoleAssignment()
    {
        // EF Core materialization.
    }

    public Guid StaffId { get; private set; }

    public Guid RoleId { get; private set; }

    public static StaffRoleAssignment For(Guid staffId, Guid roleId) => new() { StaffId = staffId, RoleId = roleId };
}
