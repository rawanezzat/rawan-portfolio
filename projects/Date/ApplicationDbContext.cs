// Data/ApplicationDbContext.cs
using Credit2.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Credit2.Data
{
    public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Remove this - Identity already has a Users DbSet
        // public DbSet<User> Users { get; set; }

        public DbSet<UserCard> UserCards { get; set; }
        public DbSet<HolderField> HolderField { get; set; }
        public DbSet<AuditorField> AuditorField { get; set; }
        public DbSet<RecordPhase> RecordPhase { get; set; }
        public DbSet<BoardMemberField> BoardMemberField { get; set; }
        public DbSet<Records> Records { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Identity tables to use your desired names
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users"); // This will be your users table
                entity.Property(e => e.Id).HasColumnName("UserId");
            });

            // User → UserCard (1-to-many)
            modelBuilder.Entity<UserCard>()
                .HasOne(c => c.User)
                .WithMany(u => u.Cards)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Records → Holder (User)
            modelBuilder.Entity<Records>()
                .HasOne(r => r.Holder)
                .WithMany()
                .HasForeignKey(r => r.HolderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Records → AccountantUser (User)
            modelBuilder.Entity<Records>()
                .HasOne(r => r.AccountantUser)
                .WithMany()
                .HasForeignKey(r => r.AccountantUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Records → CreatedByUser (User)
            modelBuilder.Entity<Records>()
                .HasOne(r => r.CreatedByUser)
                .WithMany()
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Rest of your configurations remain the same...
            modelBuilder.Entity<HolderField>()
                .HasOne(h => h.Record)
                .WithMany(r => r.HolderFields)
                .HasForeignKey(h => h.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HolderField>()
                .HasOne(h => h.Holder)
                .WithMany()
                .HasForeignKey(h => h.HolderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RecordPhase>()
                .HasOne(rp => rp.Record)
                .WithMany(r => r.RecordPhases)
                .HasForeignKey(rp => rp.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RecordPhase>()
                .HasOne(rp => rp.CurrentUser)
                .WithMany()
                .HasForeignKey(rp => rp.CurrentUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AuditorField>()
                .HasOne(a => a.Record)
                .WithMany(r => r.AuditorApprovals)
                .HasForeignKey(a => a.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AuditorField>()
                .HasOne(a => a.AuditorUser)
                .WithMany()
                .HasForeignKey(a => a.AuditorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BoardMemberField>()
                .HasOne(b => b.Record)
                .WithMany(r => r.BoardMemberApprovals)
                .HasForeignKey(b => b.RecordId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BoardMemberField>()
                .HasOne(b => b.BoardMemberUser)
                .WithMany()
                .HasForeignKey(b => b.BoardMemberUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}