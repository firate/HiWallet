using System.Security.Claims;
using HiWallet.Shared.Infrastructure.Authentication;
using HiWallet.StaffAdmin.Domain;

namespace HiWallet.StaffAdmin.Api.Controllers;

internal static class StaffActor
{
    /// <summary>Değişikliği yapan çalışan: token'daki kimlik ve okunabilsin diye adı.</summary>
    public static Actor ToActor(this ClaimsPrincipal user) => new(
        user.Subject(),
        user.FindFirst("name")?.Value ?? user.FindFirst("email")?.Value ?? user.FindFirst("preferred_username")?.Value);
}

internal static class Paging
{
    public const int DefaultSize = 20;

    public const int MaxSize = 100;

    public static int Size(int? size) => Math.Clamp(size ?? DefaultSize, 1, MaxSize);

    public static int First(int? first) => Math.Max(first ?? 0, 0);
}
