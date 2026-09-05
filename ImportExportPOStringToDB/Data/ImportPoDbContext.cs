using ImportPOStringToDB.Models;
using Microsoft.EntityFrameworkCore;

namespace ImportPOStringToDB.Data;

public sealed class ImportPoDbContext : DbContext
{
    private readonly string _connectionString;

    public ImportPoDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<PoTranslation> PoTranslations => Set<PoTranslation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer(_connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PoTranslation>();

        entity.ToTable("PoTranslations");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.AllText).IsRequired();
        entity.Property(x => x.MsgId).IsRequired();
        entity.Property(x => x.MsgStr).IsRequired();
        entity.Property(x => x.TranslationLocked)
            .HasColumnName("TranslationLocked")
            .HasDefaultValue(false);
        entity.Property(x => x.SourceFilePath).IsRequired();
        entity.Property(x => x.ImportedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.LastUpdated).HasColumnType("datetime2");
        entity.Property(x => x.Rating).HasColumnType("float");
    }
}
