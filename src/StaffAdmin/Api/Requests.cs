using FluentValidation;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;

namespace HiWallet.StaffAdmin.Api;

/// <param name="Name">Rolün adı; token'a ve panelin üstüne yazılıyor, sonradan değişmiyor.</param>
/// <param name="Permissions">Rolün içerdiği izinler; en az bir tane.</param>
public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(string? Description, IReadOnlyList<string> Permissions);

/// <param name="RoleIds">Panelin rolleri. Boş olabilir: çalışan rol verilene kadar panelde bir şey görmüyor.</param>
public sealed record InviteStaffRequest(string Email, string? FirstName, string? LastName, IReadOnlyList<Guid> RoleIds);

/// <param name="RoleIds">Çalışanın rollerinin tamamı; listede olmayan roller alınıyor.</param>
public sealed record SetStaffRolesRequest(IReadOnlyList<Guid> RoleIds);

internal static class PermissionRules
{
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> KnownPermissions<T>(
        this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule
            .NotEmpty().WithMessage("Rol en az bir izin içermeli.")
            .Must(permissions => permissions.All(StaffPermissions.All.Contains))
            .WithMessage($"İzinler şunlardan olmalı: {string.Join(", ", StaffPermissions.All)}.");
}

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(r => r.Name)
            .Must(name => name is not null && StaffRoleRules.IsWellFormed(name))
            .WithMessage($"Ad 2-{StaffRoleRules.MaxNameLength} karakter; harf, rakam, boşluk, tire ve alt çizgi.");

        RuleFor(r => r.Description)
            .MaximumLength(StaffRoleRules.MaxDescriptionLength)
            .WithMessage($"Açıklama en fazla {StaffRoleRules.MaxDescriptionLength} karakter.");

        RuleFor(r => r.Permissions).KnownPermissions();
    }
}

public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(r => r.Description)
            .MaximumLength(StaffRoleRules.MaxDescriptionLength)
            .WithMessage($"Açıklama en fazla {StaffRoleRules.MaxDescriptionLength} karakter.");

        RuleFor(r => r.Permissions).KnownPermissions();
    }
}

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    private const int MaxNameLength = 100;

    public InviteStaffRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("E-posta zorunlu.")
            .EmailAddress().WithMessage("E-posta geçersiz.")
            .MaximumLength(254).WithMessage("E-posta en fazla 254 karakter.");

        RuleFor(r => r.FirstName).MaximumLength(MaxNameLength).WithMessage($"Ad en fazla {MaxNameLength} karakter.");
        RuleFor(r => r.LastName).MaximumLength(MaxNameLength).WithMessage($"Soyad en fazla {MaxNameLength} karakter.");
        RuleFor(r => r.RoleIds).NotNull().WithMessage("Rol listesi zorunlu; boş olabilir.");
    }
}

public sealed class SetStaffRolesRequestValidator : AbstractValidator<SetStaffRolesRequest>
{
    public SetStaffRolesRequestValidator()
    {
        RuleFor(r => r.RoleIds).NotNull().WithMessage("Rol listesi zorunlu; boş olabilir.");
    }
}
