using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Data
{
    public class CareProDbContext : DbContext
    {
        public CareProDbContext(DbContextOptions<CareProDbContext> options) : base(options)
        {

        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            base.OnConfiguring(optionsBuilder);
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;

            modelBuilder.Entity<Caregiver>().ToCollection("CareGivers");
            modelBuilder.Entity<AppUser>().ToCollection("AppUsers");
            modelBuilder.Entity<Client>().ToCollection("Clients");
            modelBuilder.Entity<Gig>().ToCollection("Gigs");
            modelBuilder.Entity<ClientOrder>().ToCollection("ClientOrders");
            modelBuilder.Entity<Certification>().ToCollection("Certifications");
            modelBuilder.Entity<ChatMessage>().ToCollection("ChatMessages");
            modelBuilder.Entity<Verification>().ToCollection("Verifications");
            modelBuilder.Entity<Assessment>().ToCollection("Assessments");
            modelBuilder.Entity<ClientPreference>().ToCollection("ClientPreferences");
            modelBuilder.Entity<Notification>().ToCollection("Notifications");
            modelBuilder.Entity<QuestionBank>().ToCollection("QuestionBank");
            modelBuilder.Entity<Earnings>().ToCollection("Earnings");
            modelBuilder.Entity<WithdrawalRequest>().ToCollection("WithdrawalRequests");
            modelBuilder.Entity<AdminUser>().ToCollection("AdminUsers");
            modelBuilder.Entity<Review>().ToCollection("Reviews");

        }

        
        public DbSet<Caregiver> CareGivers { get; set; }
        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Gig> Gigs { get; set; }
        public DbSet<ClientOrder> ClientOrders { get; set; }
        public DbSet<Certification> Certifications { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Verification> Verifications { get; set; }
        public DbSet<Assessment> Assessments { get; set; }
        public DbSet<ClientPreference> ClientPreferences { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<QuestionBank> QuestionBank { get; set; }
        public DbSet<Earnings> Earnings { get; set; }
        public DbSet<WithdrawalRequest> WithdrawalRequests { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<Review> Reviews { get; set; }
    }
}
