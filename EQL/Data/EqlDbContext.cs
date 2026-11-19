using EQL.Models;
using Microsoft.EntityFrameworkCore;

namespace EQL.Data
{
    public class EqlDbContext : DbContext
    {
        public EqlDbContext(DbContextOptions<EqlDbContext> options) : base(options) { }

        public DbSet<Item> Items => Set<Item>();
        public DbSet<InventoryEntry> InventoryEntries => Set<InventoryEntry>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Item>().HasIndex(i => i.NormalizedName);
            b.Entity<Item>().HasIndex(i => i.Slot);

            b.Entity<InventoryEntry>().HasIndex(e => e.Character);
            b.Entity<InventoryEntry>()
                .HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
