using Domain.Entities;
using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Infrastructure.Content.Data
{
    /// <summary>
    /// One-time real-pricing seed for <see cref="Package"/>, sourced from
    /// "CarePro Subscription Plans.doc". Every row is a normal, admin-editable
    /// Package after seeding — see <see cref="Services.PackageService.SeedPackagesAsync"/>
    /// for the idempotency mechanism (matches by Category+TierLabel, so an
    /// admin's later edits to a seeded row are never overwritten by a re-run).
    ///
    /// "Total Peace of Mind" is one tier in the source document but priced
    /// per caregiver type (Aux/CHEW/Nurse), so it is split into three rows here
    /// — same Care hours/Includes/Reporting/Backup/Coordinator copy, different
    /// RequiredCaregiverType and (for CHEW/Nurse) BasePrice.
    ///
    /// Postpartum Care's RequiredCaregiverType/RequiredSpecialty
    /// (RegisteredNurse / "Midwifery") is an assumption — the source document
    /// doesn't state a caregiver type for that category, only for Elderly Care.
    ///
    /// Live-in Package rows intentionally leave PayCalculationType and
    /// FixedCaregiverPay unset: the source document gives one client-facing
    /// price per tier with no separate caregiver-payout figure, and per
    /// product direction the caregiver pay split for these packages belongs
    /// to the payroll system being seeded separately, not this pass. An admin
    /// (or that future seed) sets PayCalculationType/FixedCaregiverPay via the
    /// existing CRUD screen when that split is known.
    ///
    /// Live-in Package rows also have placeholder Description text — the
    /// source document gives prices for this section but no Includes/Care
    /// hours/Reporting/Backup/Coordinator copy the way every other category
    /// has. Flagged for an admin to write real copy via the CRUD screen.
    /// </summary>
    public static class PackageSeedData
    {
        public static List<Package> GetSeedData()
        {
            var now = DateTime.UtcNow;

            const string totalPeaceOfMindDescription =
                "Care hours: 24/7, 12-hr shifts. Includes: Full ADL support, dementia-aware care, monthly nurse visit. " +
                "Reporting: Daily photo/video report. Backup/replacement: Guaranteed same-day emergency backup. " +
                "Coordinator access: Dedicated care coordinator.";

            const string recoveryIncludes =
                "Nurse visit coordination, full ADL support, safety monitoring.";

            return new List<Package>
            {
                // ── Elderly Care (Adult/Elder Care) — monthly subscription, hourly pay calc ──
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.AdultElderCare,
                    TierLabel = "Essential Companion",
                    RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
                    BasePrice = 50000m,
                    AdditionalDayPrice = 15000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "Care hours: 2 visits/week, 4 hrs each. Includes: Medication reminders, companionship, safety checks. " +
                        "Reporting: WhatsApp update after each visit. Backup/replacement: Replacement within 72 hrs if needed. " +
                        "Coordinator access: WhatsApp support line.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.AdultElderCare,
                    TierLabel = "Standard Daily Care",
                    RequiredCaregiverType = CaregiverType.CHEW,
                    BasePrice = 220000m,
                    AdditionalDayPrice = 15000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "Care hours: 5 visits weekly, 8 hrs/day. Includes: Meals, mobility support, ADLs, medication management. " +
                        "Reporting: Daily photo + written report. Backup/replacement: Emergency backup caregiver on call. " +
                        "Coordinator access: Weekly coordinator check-in call.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.AdultElderCare,
                    TierLabel = "Total Peace of Mind — Auxiliary Nurse",
                    RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
                    BasePrice = 250000m,
                    AdditionalDayPrice = 15000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description = totalPeaceOfMindDescription,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.AdultElderCare,
                    TierLabel = "Total Peace of Mind — CHEW",
                    RequiredCaregiverType = CaregiverType.CHEW,
                    BasePrice = 270000m,
                    AdditionalDayPrice = 15000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description = totalPeaceOfMindDescription,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.AdultElderCare,
                    TierLabel = "Total Peace of Mind — Registered Nurse",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    BasePrice = 270000m,
                    AdditionalDayPrice = 15000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description = totalPeaceOfMindDescription,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },

                // ── Postpartum Care (Post-Partum Care) — fixed-duration, hourly pay calc ──
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.PostPartumCare,
                    TierLabel = "Essential Postpartum",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    RequiredSpecialty = "Midwifery",
                    BasePrice = 350000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "4-week package. Care hours: Daily visits, 6 hrs/day. Includes: Newborn care basics, breastfeeding support, " +
                        "mother's recovery check and support, newborn bathing/care, light household help. " +
                        "Reporting: Daily WhatsApp update. Add-ons: Optional night nurse at extra cost.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.PostPartumCare,
                    TierLabel = "Premium Confinement",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    RequiredSpecialty = "Midwifery",
                    BasePrice = 450000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "4-week package. Care hours: 24/7 live-in with night nurse rotation. " +
                        "Includes: Full newborn and mother care, meal prep, doctor home-visit coordination. " +
                        "Reporting: Daily photo/video report. Add-ons: Dedicated coordinator throughout.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },

                // ── Post-Surgery Recovery Care (Post Surgery Care) — fixed-duration, hourly pay calc ──
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.PostSurgeryCare,
                    TierLabel = "Essential Recovery",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    BasePrice = 80000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "1-week package, renewable. Care hours: Daily visits, 4 hrs/day. " +
                        "Includes: " + recoveryIncludes + " Reporting: Daily WhatsApp update.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.PostSurgeryCare,
                    TierLabel = "Standard Recovery",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    BasePrice = 150000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "2-week package. Care hours: Daily visits, 4 hrs/day. " +
                        "Includes: " + recoveryIncludes + " Reporting: Daily photo + written report.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.PostSurgeryCare,
                    TierLabel = "Premium Recovery Concierge",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    BasePrice = 300000m,
                    PayCalculationType = PayCalculationType.Hourly,
                    Description =
                        "Up to 4 weeks or until mobility restored. Care hours: Daily visits, 4 hrs/day. " +
                        "Includes: " + recoveryIncludes + " Reporting: Daily photo/video report.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },

                // ── Live-in Package — fixed pay calc (caregiver payout split deferred to the
                //    payroll seed; PayCalculationType/FixedCaregiverPay intentionally left null) ──
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.LiveInPackage,
                    TierLabel = "Aux Package",
                    RequiredCaregiverType = CaregiverType.AuxiliaryNurse,
                    BasePrice = 250000m,
                    Description =
                        "Live-in Auxiliary Nurse package. Full details (care hours, reporting, backup, coordinator access) " +
                        "to be added — not specified in the source pricing document.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.LiveInPackage,
                    TierLabel = "CHEW Package",
                    RequiredCaregiverType = CaregiverType.CHEW,
                    BasePrice = 270000m,
                    Description =
                        "Live-in CHEW (intermediate care) package. Full details (care hours, reporting, backup, coordinator access) " +
                        "to be added — not specified in the source pricing document.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Package
                {
                    Id = ObjectId.GenerateNewId(),
                    Category = PackageCategories.LiveInPackage,
                    TierLabel = "Nurse Package",
                    RequiredCaregiverType = CaregiverType.RegisteredNurse,
                    BasePrice = 300000m,
                    Description =
                        "Live-in Registered Nurse package. Full details (care hours, reporting, backup, coordinator access) " +
                        "to be added — not specified in the source pricing document.",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            };
        }
    }
}
