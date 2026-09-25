using Application.Commands;
using Application.DTOs;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Admin CRUD + wallet-credit trigger for <see cref="Payroll"/> (Phase 9.7). Follows
    /// the same conventions as <see cref="PackageService"/>/<see cref="CaregiverPayRateService"/>:
    /// ArgumentException for bad input, KeyNotFoundException for a missing record.
    /// </summary>
    public class PayrollService : IPayrollService
    {
        private readonly CareProDbContext _context;
        private readonly ITaskSheetService _taskSheetService;
        private readonly ICaregiverWalletService _walletService;
        private readonly IEarningsLedgerService _ledgerService;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly ILogger<PayrollService> _logger;

        public PayrollService(
            CareProDbContext context,
            ITaskSheetService taskSheetService,
            ICaregiverWalletService walletService,
            IEarningsLedgerService ledgerService,
            IMediator mediator,
            IEmailService emailService,
            ILogger<PayrollService> logger)
        {
            _context = context;
            _taskSheetService = taskSheetService;
            _walletService = walletService;
            _ledgerService = ledgerService;
            _mediator = mediator;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<PayrollDTO> CreatePayrollAsync(CreatePayrollRequest request)
        {
            if (request == null) throw new ArgumentException("Request body is required");
            if (!ObjectId.TryParse(request.AssignmentId, out var assignmentOid))
                throw new ArgumentException("Invalid assignment ID format");

            var assignment = await _context.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentOid)
                ?? throw new KeyNotFoundException($"Assignment '{request.AssignmentId}' not found");

            var packageRequest = await _context.PackageRequests
                    .FirstOrDefaultAsync(pr => pr.Id == ObjectId.Parse(assignment.PackageRequestId))
                ?? throw new KeyNotFoundException($"PackageRequest '{assignment.PackageRequestId}' not found for this assignment");

            var package = await _context.Packages.FirstOrDefaultAsync(p => p.Id == ObjectId.Parse(packageRequest.PackageId))
                ?? throw new KeyNotFoundException($"Package '{packageRequest.PackageId}' not found for this assignment");

            if (package.PayCalculationType == null)
                throw new InvalidOperationException(
                    "This package has no PayCalculationType set — set it via the admin Packages endpoint before creating payroll.");

            var payPeriod = new DateTime(request.Year, request.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var duplicate = await _context.Payrolls.AnyAsync(p =>
                p.AssignmentId == request.AssignmentId && p.PayPeriod == payPeriod);
            if (duplicate)
                throw new ArgumentException(
                    $"A payroll record already exists for assignment '{request.AssignmentId}' for {request.Year}-{request.Month:D2}.");

            decimal calculatedAmount;
            double? hoursWorked = null;
            decimal? rateApplied = null;

            if (package.PayCalculationType == PayCalculationType.Fixed)
            {
                calculatedAmount = package.FixedCaregiverPay
                    ?? throw new InvalidOperationException("Package is Fixed but has no FixedCaregiverPay set.");
            }
            else
            {
                var caregiver = await _context.CareGivers.FirstOrDefaultAsync(c => c.Id.ToString() == assignment.CaregiverId)
                    ?? throw new KeyNotFoundException($"Caregiver '{assignment.CaregiverId}' not found");

                if (caregiver.CaregiverType == null || caregiver.ExperienceTier == null)
                    throw new InvalidOperationException(
                        "Caregiver must have both CaregiverType and ExperienceTier set before payroll can be calculated.");

                var payRate = await _context.CaregiverPayRates.FirstOrDefaultAsync(r =>
                        r.CaregiverType == caregiver.CaregiverType.Value
                        && r.ExperienceTier == caregiver.ExperienceTier.Value
                        && r.IsActive)
                    ?? throw new KeyNotFoundException(
                        $"No active pay rate found for {caregiver.CaregiverType}/{caregiver.ExperienceTier}.");

                var hours = await _taskSheetService.GetMonthlyHoursForCaregiverAsync(
                    assignment.CaregiverId, request.Year, request.Month, request.AssignmentId);

                hoursWorked = hours.TotalHours;
                rateApplied = payRate.HourlyRate;
                calculatedAmount = Math.Round((decimal)hoursWorked.Value * rateApplied.Value, 2);
            }

            var now = DateTime.UtcNow;
            var entity = new Payroll
            {
                Id = ObjectId.GenerateNewId(),
                AssignmentId = request.AssignmentId,
                PackageRequestId = assignment.PackageRequestId,
                CaregiverId = assignment.CaregiverId,
                ClientId = assignment.ClientId,
                PayPeriod = payPeriod,
                PayCalculationType = package.PayCalculationType.Value,
                HoursWorked = hoursWorked,
                RateApplied = rateApplied,
                CalculatedAmount = calculatedAmount,
                FinalAmount = calculatedAmount,
                Status = PayrollStatuses.Draft,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Payrolls.Add(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Payroll {Id} created for Assignment {AssignmentId}, period {Year}-{Month:D2}: {Amount} ({CalcType})",
                entity.Id, request.AssignmentId, request.Year, request.Month, calculatedAmount, package.PayCalculationType);

            return MapToDTO(entity);
        }

        public async Task<PayrollDTO?> GetPayrollByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                return null;

            var entity = await _context.Payrolls.FirstOrDefaultAsync(p => p.Id == oid);
            return entity != null ? MapToDTO(entity) : null;
        }

        public async Task<List<PayrollDTO>> GetAllPayrollsAsync(string? caregiverId = null)
        {
            IQueryable<Payroll> query = _context.Payrolls;
            if (!string.IsNullOrEmpty(caregiverId))
                query = query.Where(p => p.CaregiverId == caregiverId);

            var payrolls = await query
                .OrderByDescending(p => p.PayPeriod)
                .ThenByDescending(p => p.CreatedAt)
                .ToListAsync();
            return payrolls.Select(MapToDTO).ToList();
        }

        public async Task<PayrollDTO> ApprovePayrollAsync(string id, ApprovePayrollRequest request, string adminId, string adminEmail)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid payroll ID format");

            var entity = await _context.Payrolls.FirstOrDefaultAsync(p => p.Id == oid)
                ?? throw new KeyNotFoundException($"Payroll with ID '{id}' not found");

            if (entity.Status != PayrollStatuses.Draft)
                throw new InvalidOperationException($"Only Draft payroll records can be approved (current status: {entity.Status}).");

            if (request?.FinalAmount.HasValue == true && request.FinalAmount.Value != entity.CalculatedAmount)
            {
                if (string.IsNullOrWhiteSpace(request.OverrideReason))
                    throw new ArgumentException("A reason is required when FinalAmount differs from CalculatedAmount.");

                var before = new { entity.FinalAmount };

                entity.FinalAmount = request.FinalAmount.Value;
                entity.OverrideReason = request.OverrideReason.Trim();

                var after = new { entity.FinalAmount, entity.OverrideReason };

                _context.AdminAuditLogs.Add(new AdminAuditLog
                {
                    Id = ObjectId.GenerateNewId(),
                    AdminId = adminId,
                    AdminEmail = adminEmail,
                    TargetEntityType = "Payroll",
                    TargetEntityId = entity.Id.ToString(),
                    TargetUserId = entity.CaregiverId,
                    Action = "PayrollFinalAmountOverride",
                    BeforeJson = JsonSerializer.Serialize(before),
                    AfterJson = JsonSerializer.Serialize(after),
                    Reason = request.OverrideReason.Trim(),
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                entity.FinalAmount = entity.CalculatedAmount;
            }

            entity.Status = PayrollStatuses.Approved;
            entity.ApprovedByAdminId = adminId;
            entity.ApprovedByAdminEmail = adminEmail;
            entity.ApprovedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Approval is the finality/release event for payroll — no per-visit release
            // ceremony the way order-based earnings need one — so credit WithdrawableBalance
            // directly, the same mechanism used for recurring subscription payments.
            await _walletService.CreditRecurringPaymentAsync(entity.CaregiverId, entity.FinalAmount);

            // Every other wallet credit in this app (order-received, funds-released,
            // visit-approved, ...) is paired with a ledger entry so the caregiver's
            // transaction history explains where the money came from — payroll was
            // missing this pairing, so the balance moved with no visible line item.
            await _ledgerService.RecordPayrollCreditAsync(
                entity.CaregiverId,
                entity.FinalAmount,
                entity.Id.ToString(),
                entity.PayPeriod,
                $"Payroll approved for {entity.PayPeriod:MMMM yyyy}");

            entity.CreditedToWalletAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Payroll {Id} approved by {AdminEmail} and credited {Amount} to caregiver {CaregiverId}'s wallet",
                entity.Id, adminEmail, entity.FinalAmount, entity.CaregiverId);

            // The caregiver must be told when money lands — same courtesy as order-based
            // earnings (EarningsAdded + SendEarningsNotificationEmailAsync). Always-Send:
            // this is a direct result of the caregiver's own completed work.
            await NotifyCaregiverPaidAsync(
                entity, NotificationTypes.PayrollApproved,
                $"Your payroll for {entity.PayPeriod:MMMM yyyy} has been approved. ₦{entity.FinalAmount:N2} has been added to your wallet and is available to withdraw.");

            return MapToDTO(entity);
        }

        public async Task<bool> MarkPayrollPaidAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid payroll ID format");

            var entity = await _context.Payrolls.FirstOrDefaultAsync(p => p.Id == oid)
                ?? throw new KeyNotFoundException($"Payroll with ID '{id}' not found");

            if (entity.Status != PayrollStatuses.Approved)
                throw new InvalidOperationException($"Only Approved payroll records can be marked Paid (current status: {entity.Status}).");

            entity.Status = PayrollStatuses.Paid;
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyCaregiverPaidAsync(
                entity, NotificationTypes.PayrollPaid,
                $"Your approved payroll for {entity.PayPeriod:MMMM yyyy} (₦{entity.FinalAmount:N2}) has been marked as paid out.");

            return true;
        }

        /// <summary>
        /// Tells the caregiver — in-app + email — that a payroll amount has moved. Best-effort:
        /// a notification hiccup must never roll back a completed wallet credit / status change.
        /// The email uses the policy-#10 "earnings" template (Always-Send).
        /// </summary>
        private async Task NotifyCaregiverPaidAsync(Payroll entity, string notificationType, string content)
        {
            try
            {
                await _mediator.Send(new SendNotificationCommand(
                    RecipientId: entity.CaregiverId,
                    SenderId: "system",
                    Type: notificationType,
                    Content: content,
                    Title: notificationType == NotificationTypes.PayrollPaid ? "Payroll Paid" : "Payroll Approved",
                    RelatedEntityId: entity.Id.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send in-app payroll notification for payroll {PayrollId}", entity.Id);
            }

            try
            {
                var caregiver = ObjectId.TryParse(entity.CaregiverId, out var cgOid)
                    ? await _context.CareGivers.FirstOrDefaultAsync(c => c.Id == cgOid)
                    : null;

                if (caregiver != null && !string.IsNullOrWhiteSpace(caregiver.Email))
                {
                    string clientName = "CarePro";
                    if (ObjectId.TryParse(entity.ClientId, out var clOid))
                    {
                        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == clOid);
                        if (client != null)
                            clientName = $"{client.FirstName} {client.LastName}".Trim();
                    }

                    var serviceType = "Package care";
                    if (ObjectId.TryParse(entity.PackageRequestId, out var prOid))
                    {
                        var pr = await _context.PackageRequests.FirstOrDefaultAsync(p => p.Id == prOid);
                        if (pr != null && !string.IsNullOrWhiteSpace(pr.PackageCategory))
                            serviceType = pr.PackageCategory;
                    }

                    await _emailService.SendEarningsNotificationEmailAsync(
                        caregiver.Email, caregiver.FirstName ?? "there",
                        entity.FinalAmount, clientName, serviceType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send payroll email for payroll {PayrollId}", entity.Id);
            }
        }

        public async Task<bool> DeletePayrollAsync(string id)
        {
            if (!ObjectId.TryParse(id, out var oid))
                throw new ArgumentException("Invalid payroll ID format");

            var entity = await _context.Payrolls.FirstOrDefaultAsync(p => p.Id == oid)
                ?? throw new KeyNotFoundException($"Payroll with ID '{id}' not found");

            if (entity.Status != PayrollStatuses.Draft)
                throw new InvalidOperationException(
                    $"Only Draft payroll records can be deleted — a wallet credit has already been made against status '{entity.Status}'.");

            _context.Payrolls.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        private static PayrollDTO MapToDTO(Payroll p) => new()
        {
            Id = p.Id.ToString(),
            AssignmentId = p.AssignmentId,
            PackageRequestId = p.PackageRequestId,
            CaregiverId = p.CaregiverId,
            ClientId = p.ClientId,
            PayPeriod = p.PayPeriod,
            PayCalculationType = p.PayCalculationType.ToString(),
            HoursWorked = p.HoursWorked,
            RateApplied = p.RateApplied,
            CalculatedAmount = p.CalculatedAmount,
            FinalAmount = p.FinalAmount,
            OverrideReason = p.OverrideReason,
            Status = p.Status,
            CreditedToWalletAt = p.CreditedToWalletAt,
            ApprovedByAdminEmail = p.ApprovedByAdminEmail,
            ApprovedAt = p.ApprovedAt,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        };
    }
}
