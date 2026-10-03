using HiWallet.StaffAdmin.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HiWallet.StaffAdmin.Infrastructure.Persistence.Configurations;

internal sealed class StaffAuditEventConfiguration : IEntityTypeConfiguration<StaffAuditEvent>
{
    public void Configure(EntityTypeBuilder<StaffAuditEvent> builder)
    {
        builder.ToTable("staff_audit_events", t =>
        {
            t.HasCheckConstraint(
                "ck_staff_audit_events_action",
                $"action IN ({Quoted(StaffAuditTexts.Actions.Values)})");
            t.HasCheckConstraint(
                "ck_staff_audit_events_target_type",
                $"target_type IN ({Quoted(StaffAuditTexts.Targets.Values)})");
        });

        builder.HasKey(e => e.Id).HasName("pk_staff_audit_events");

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        builder.Property(e => e.ActorSubject).HasColumnName("actor_subject").HasColumnType("text").IsRequired();
        builder.Property(e => e.ActorName).HasColumnName("actor_name").HasColumnType("text");
        builder.Property(e => e.Action)
            .HasColumnName("action")
            .HasColumnType("text")
            .HasConversion(new ValueConverter<StaffAuditAction, string>(
                action => StaffAuditTexts.Actions[action],
                text => StaffAuditTexts.Actions.Single(pair => pair.Value == text).Key))
            .IsRequired();
        builder.Property(e => e.TargetType)
            .HasColumnName("target_type")
            .HasColumnType("text")
            .HasConversion(new ValueConverter<StaffAuditTarget, string>(
                target => StaffAuditTexts.Targets[target],
                text => StaffAuditTexts.Targets.Single(pair => pair.Value == text).Key))
            .IsRequired();
        builder.Property(e => e.TargetId).HasColumnName("target_id");
        builder.Property(e => e.TargetLabel).HasColumnName("target_label").HasColumnType("text").IsRequired();
        builder.Property(e => e.Details).HasColumnName("details").HasColumnType("jsonb").IsRequired();

        // Bir rolün ya da çalışanın geçmişi.
        builder.HasIndex(e => e.TargetId).HasDatabaseName("ix_staff_audit_events_target_id");
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));
}
