namespace HiWallet.StaffAdmin.Application;

public sealed class StaffAdminOptions
{
    public const string SectionName = "StaffAdmin";

    public BootstrapOptions Bootstrap { get; set; } = new();

    /// <summary>
    /// Açılış kurulumu. Panelden yönetilebilmesi için en az bir çalışanın personel
    /// yönetimi izni olmalı; ilk çalışanı kimlik sağlayıcının konsolu değil bu kurulum açıyor.
    /// </summary>
    public sealed class BootstrapOptions
    {
        /// <summary>Yalnızca personel yönetimi iznini içeren rol; yoksa açılıyor.</summary>
        public string AdminRoleName { get; set; } = string.Empty;

        /// <summary>
        /// Kimsede personel yönetimi yoksa bu adrese davet gidiyor ve rol veriliyor. Boşsa
        /// kurulum yalnızca yönetici rolünü açıyor.
        /// </summary>
        public string? AdminEmail { get; set; }
    }
}
