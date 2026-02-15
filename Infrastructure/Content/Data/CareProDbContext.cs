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
            modelBuilder.Entity<Caregiver>().HasKey(c => c.Id);
            modelBuilder.Entity<Caregiver>().Property(c => c.Id).HasElementName("_id");
            modelBuilder.Entity<Caregiver>().HasIndex(c => c.Email).IsUnique();
            
            modelBuilder.Entity<AppUser>().ToCollection("AppUsers");
            modelBuilder.Entity<AppUser>().HasKey(a => a.Id);
            modelBuilder.Entity<AppUser>().Property(a => a.Id).HasElementName("_id");
            modelBuilder.Entity<AppUser>().HasIndex(a => a.Email).IsUnique();
            
            modelBuilder.Entity<Client>().ToCollection("Clients");
            modelBuilder.Entity<Client>().HasKey(cl => cl.Id);
            modelBuilder.Entity<Client>().Property(cl => cl.Id).HasElementName("_id");
            modelBuilder.Entity<Client>().HasIndex(cl => cl.Email).IsUnique();
            modelBuilder.Entity<AdminUser>().ToCollection("AdminUsers");
            modelBuilder.Entity<AdminUser>().HasKey(au => au.Id);
            modelBuilder.Entity<AdminUser>().Property(au => au.Id).HasElementName("_id");
            modelBuilder.Entity<Gig>().ToCollection("Gigs");
            modelBuilder.Entity<ClientOrder>().ToCollection("ClientOrders");
            modelBuilder.Entity<Certification>().ToCollection("Certifications");
            modelBuilder.Entity<ChatMessage>().ToCollection("ChatMessages");
            modelBuilder.Entity<ChatMessage>().HasKey(c => c.MessageId);
            modelBuilder.Entity<ChatMessage>().Property(c => c.MessageId).HasElementName("_id");
            modelBuilder.Entity<Verification>().ToCollection("Verifications");
            modelBuilder.Entity<Assessment>().ToCollection("Assessments");
            modelBuilder.Entity<ClientPreference>().ToCollection("ClientPreferences");
            modelBuilder.Entity<ClientPreference>().HasKey(cp => cp.Id);
            modelBuilder.Entity<ClientPreference>().Property(cp => cp.Id).HasElementName("_id");
            modelBuilder.Entity<ClientRecommendation>().ToCollection("ClientRecommendations");
            modelBuilder.Entity<ClientRecommendation>().HasKey(cr => cr.Id);
            modelBuilder.Entity<ClientRecommendation>().Property(cr => cr.Id).HasElementName("_id");
            modelBuilder.Entity<Notification>().ToCollection("Notifications");
            modelBuilder.Entity<QuestionBank>().ToCollection("QuestionBank");
            modelBuilder.Entity<Earnings>().ToCollection("Earnings");
            modelBuilder.Entity<WithdrawalRequest>().ToCollection("WithdrawalRequests");
            modelBuilder.Entity<AdminUser>().ToCollection("AdminUsers");
            modelBuilder.Entity<Review>().ToCollection("Reviews");
            modelBuilder.Entity<Location>().ToCollection("Locations");
            modelBuilder.Entity<Contract>().ToCollection("Contracts");
            modelBuilder.Entity<Contract>().HasKey(c => c.Id);
            modelBuilder.Entity<Contract>().Property(c => c.Id).HasElementName("_id");
            modelBuilder.Entity<ContractNegotiationHistory>().ToCollection("ContractNegotiationHistory");
            modelBuilder.Entity<ContractNegotiationHistory>().HasKey(cnh => cnh.Id);
            modelBuilder.Entity<ContractNegotiationHistory>().Property(cnh => cnh.Id).HasElementName("_id");
            modelBuilder.Entity<OrderTasks>().ToCollection("OrderTasks");
            modelBuilder.Entity<TrainingMaterial>().ToCollection("TrainingMaterials");
            modelBuilder.Entity<TrainingMaterial>().HasKey(tm => tm.Id);
            modelBuilder.Entity<TrainingMaterial>().Property(tm => tm.Id).HasElementName("_id");
            modelBuilder.Entity<EmailNotificationLog>().ToCollection("EmailNotificationLogs");
            modelBuilder.Entity<EmailNotificationLog>().HasKey(enl => enl.Id);
            modelBuilder.Entity<EmailNotificationLog>().Property(enl => enl.Id).HasElementName("_id");
            modelBuilder.Entity<RefreshToken>().ToCollection("RefreshTokens");
            modelBuilder.Entity<RefreshToken>().HasKey(rt => rt.Id);
            modelBuilder.Entity<RefreshToken>().Property(rt => rt.Id).HasElementName("_id");
            modelBuilder.Entity<WebhookLog>().ToCollection("WebhookLogs");
            modelBuilder.Entity<WebhookLog>().HasKey(wl => wl.Id);
            modelBuilder.Entity<WebhookLog>().Property(wl => wl.Id).HasElementName("_id");
            modelBuilder.Entity<PendingPayment>().ToCollection("PendingPayments");
            modelBuilder.Entity<PendingPayment>().HasKey(pp => pp.Id);
            modelBuilder.Entity<PendingPayment>().Property(pp => pp.Id).HasElementName("_id");
            modelBuilder.Entity<CareRequest>().ToCollection("CareRequests");
            modelBuilder.Entity<CareRequest>().HasKey(cr => cr.Id);
            modelBuilder.Entity<CareRequest>().Property(cr => cr.Id).HasElementName("_id");
            modelBuilder.Entity<Subscription>().ToCollection("Subscriptions");
            modelBuilder.Entity<Subscription>().HasKey(s => s.Id);
            modelBuilder.Entity<Subscription>().Property(s => s.Id).HasElementName("_id");

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
        public DbSet<ClientRecommendation> ClientRecommendations { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<QuestionBank> QuestionBank { get; set; }
        public DbSet<Earnings> Earnings { get; set; }
        public DbSet<WithdrawalRequest> WithdrawalRequests { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Location> Locations { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<ContractNegotiationHistory> ContractNegotiationHistory { get; set; }
        public DbSet<OrderTasks> OrderTasks { get; set; }
        public DbSet<TrainingMaterial> TrainingMaterials { get; set; }
        public DbSet<EmailNotificationLog> EmailNotificationLogs { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<WebhookLog> WebhookLogs { get; set; }
        public DbSet<PendingPayment> PendingPayments { get; set; }
        public DbSet<CareRequest> CareRequests { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
    }
}
