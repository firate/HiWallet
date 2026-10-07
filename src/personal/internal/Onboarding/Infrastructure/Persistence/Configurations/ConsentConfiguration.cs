using HiWallet.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HiWallet.Onboarding.Infrastructure.Persistence.Configurations;

internal sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("consents", t =>
            t.HasCheckConstraint("ck_consents_document", "document IN ('terms','privacy_notice')"));

        builder.HasKey(c => c.Id).HasName("pk_consents");

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Subject).HasColumnName("subject").HasColumnType("text").IsRequired();
        builder.Property(c => c.Document)
            .HasColumnName("document")
            .HasConversion(ValueConverters.ConsentDocument)
            .HasColumnType("text")
            .IsRequired();
        builder.Property(c => c.Version).HasColumnName("version").HasColumnType("text").IsRequired();
        builder.Property(c => c.AcceptedAt).HasColumnName("accepted_at");

        // Aynı sürümün onayı bir kez yazılıyor; tekrar eden istek ON CONFLICT ile geçiyor.
        builder.HasIndex(c => new { c.Subject, c.Document, c.Version })
            .HasDatabaseName("ux_consents_subject_document_version")
            .IsUnique();
    }
}
