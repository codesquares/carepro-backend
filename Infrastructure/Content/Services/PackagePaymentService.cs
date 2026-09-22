using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    /// <summary>
    /// Admin-initiated Package payment link generation + webhook-driven completion
    /// (Option A). Structurally cloned from <see cref="BookingCommitmentService"/>'s
    /// initiate/complete pair, not from <see cref="PendingPaymentService"/> — that
    /// service carries a large amount of Gig-purchase-only logic (duplicate-order
    /// guards, commitment-fee gate, referral discounts, recurring subscriptions) that
    /// does not apply to a Package sale.
    /// </summary>
    public class PackagePaymentService : IPackagePaymentService
    {
        private readonly CareProDbContext _dbContext;
        private readonly IPackageService _packageService;
        private readonly IPackageRequestService _packageRequestService;
        private readonly FlutterwaveService _flutterwaveService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PackagePaymentService> _logger;

        public PackagePaymentService(
            CareProDbContext dbContext,
            IPackageService packageService,
            IPackageRequestService packageRequestService,
            FlutterwaveService flutterwaveService,
            IConfiguration configuration,
            ILogger<PackagePaymentService> logger)
        {
            _dbContext = dbContext;
            _packageService = packageService;
            _packageRequestService = packageRequestService;
            _flutterwaveService = flutterwaveService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<Result<PackagePaymentResponse>> InitiatePackagePaymentAsync(AdminInitiatePackagePaymentRequest request, string? adminId)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId))
                return Result<PackagePaymentResponse>.Failure(new List<string> { "ClientId is required." });
            if (string.IsNullOrWhiteSpace(request.PackageId))
                return Result<PackagePaymentResponse>.Failure(new List<string> { "PackageId is required." });
            if (request.ExtraDays < 0)
                return Result<PackagePaymentResponse>.Failure(new List<string> { "ExtraDays cannot be negative." });

            var package = await _packageService.GetPackageByIdAsync(request.PackageId);
            if (package == null)
                return Result<PackagePaymentResponse>.Failure(new List<string> { "Package not found." });
            if (!package.IsActive)
                return Result<PackagePaymentResponse>.Failure(new List<string> { "This package is not currently available." });

            if (request.ExtraDays > 0 && package.AdditionalDayPrice == null)
                return Result<PackagePaymentResponse>.Failure(new List<string> { "This package does not support an extra-days add-on." });

            if (!ObjectId.TryParse(request.ClientId, out var clientOid))
                return Result<PackagePaymentResponse>.Failure(new List<string> { "Invalid client id." });

            var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id == clientOid);
            if (client == null)
                return Result<PackagePaymentResponse>.Failure(new List<string> { "Client not found." });
            if (string.IsNullOrWhiteSpace(client.Email))
                return Result<PackagePaymentResponse>.Failure(new List<string> { "This client has no email on file — cannot generate a Flutterwave payment link." });

            decimal basePrice = package.BasePrice;
            decimal additionalDayAmount = (package.AdditionalDayPrice ?? 0m) * request.ExtraDays;
            decimal totalAmount = basePrice + additionalDayAmount;

            if (totalAmount <= 0)
                return Result<PackagePaymentResponse>.Failure(new List<string> { "This package cannot be paid for because the total amount is zero or negative." });

            // ── Reuse or expire a stale pending link for the same client+package+add-on ──
            var existingPending = await _dbContext.PendingPackagePayments
                .FirstOrDefaultAsync(p => p.ClientId == request.ClientId
                                       && p.PackageId == request.PackageId
                                       && p.ExtraDays == request.ExtraDays
                                       && p.Status == PendingPackagePaymentStatus.Pending);

            if (existingPending != null)
            {
                var age = DateTime.UtcNow - existingPending.CreatedAt;
                if (age.TotalMinutes < 20 && !string.IsNullOrEmpty(existingPending.PaymentLink))
                {
                    _logger.LogInformation(
                        "Returning existing package payment link. TxRef: {TxRef}, Age: {AgeMinutes}m",
                        existingPending.TransactionReference, (int)age.TotalMinutes);

                    return Result<PackagePaymentResponse>.Success(new PackagePaymentResponse
                    {
                        Success = true,
                        Message = "A payment link for this client and package is already in progress. Use the existing link.",
                        TransactionReference = existingPending.TransactionReference,
                        PaymentLink = existingPending.PaymentLink,
                        ClientId = request.ClientId,
                        PackageId = request.PackageId,
                        ExtraDays = existingPending.ExtraDays,
                        BasePrice = existingPending.BasePrice,
                        AdditionalDayAmount = existingPending.AdditionalDayAmount,
                        TotalAmount = existingPending.TotalAmount,
                        Currency = existingPending.Currency
                    });
                }

                existingPending.Status = PendingPackagePaymentStatus.Expired;
                existingPending.ErrorMessage = "Expired: superseded by a new payment link.";
                _logger.LogInformation(
                    "Expired stale package payment link. TxRef: {TxRef}, Age: {AgeHours}h",
                    existingPending.TransactionReference, (int)age.TotalHours);
            }

            string transactionReference = $"CAREPRO-PKG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
            string redirectUrl = BuildRedirectUrl(transactionReference);

            var pendingPayment = new PendingPackagePayment
            {
                Id = ObjectId.GenerateNewId(),
                TransactionReference = transactionReference,
                ClientId = request.ClientId,
                PackageId = request.PackageId,
                ExtraDays = request.ExtraDays,
                BasePrice = basePrice,
                AdditionalDayAmount = additionalDayAmount,
                TotalAmount = totalAmount,
                Currency = "NGN",
                Email = client.Email,
                RedirectUrl = redirectUrl,
                Status = PendingPackagePaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                Notes = request.Notes?.Trim(),
                InitiatedByAdminId = adminId
            };

            try
            {
                var flutterwaveResponse = await _flutterwaveService.InitiatePayment(
                    totalAmount,
                    client.Email,
                    "NGN",
                    transactionReference,
                    redirectUrl
                );

                var paymentLink = ExtractPaymentLink(flutterwaveResponse);
                if (string.IsNullOrEmpty(paymentLink))
                {
                    _logger.LogError("Failed to get payment link from Flutterwave for package payment. Response: {Response}", flutterwaveResponse);
                    return Result<PackagePaymentResponse>.Failure(new List<string> { "Failed to initialize payment with Flutterwave." });
                }

                pendingPayment.PaymentLink = paymentLink;

                _dbContext.PendingPackagePayments.Add(pendingPayment);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Package payment link generated. TxRef: {TxRef}, ClientId: {ClientId}, PackageId: {PackageId}, ExtraDays: {ExtraDays}, Amount: {Amount}",
                    transactionReference, request.ClientId, request.PackageId, request.ExtraDays, totalAmount);

                return Result<PackagePaymentResponse>.Success(new PackagePaymentResponse
                {
                    Success = true,
                    Message = "Payment link generated successfully.",
                    TransactionReference = transactionReference,
                    PaymentLink = paymentLink,
                    ClientId = request.ClientId,
                    PackageId = request.PackageId,
                    ExtraDays = request.ExtraDays,
                    BasePrice = basePrice,
                    AdditionalDayAmount = additionalDayAmount,
                    TotalAmount = totalAmount,
                    Currency = "NGN"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating Flutterwave package payment for ClientId: {ClientId}, PackageId: {PackageId}", request.ClientId, request.PackageId);
                return Result<PackagePaymentResponse>.Failure(new List<string> { "An error occurred while generating the payment link." });
            }
        }

        public async Task<PendingPackagePayment?> GetByTransactionReferenceAsync(string transactionReference)
        {
            return await _dbContext.PendingPackagePayments
                .FirstOrDefaultAsync(p => p.TransactionReference == transactionReference);
        }

        public async Task<Result<PendingPackagePayment>> CompletePackagePaymentAsync(string transactionReference, string flutterwaveTransactionId, decimal paidAmount)
        {
            var pendingPayment = await GetByTransactionReferenceAsync(transactionReference);
            if (pendingPayment == null)
            {
                _logger.LogWarning("Package payment completion attempted for unknown TxRef: {TxRef}", transactionReference);
                return Result<PendingPackagePayment>.Failure(new List<string> { "Payment record not found." });
            }

            // Idempotency guard (webhook replay protection)
            if (pendingPayment.Status == PendingPackagePaymentStatus.Completed)
            {
                _logger.LogWarning(
                    "Duplicate CompletePackagePayment attempt for TxRef: {TxRef}. Already completed at {CompletedAt}.",
                    transactionReference, pendingPayment.CompletedAt);
                return Result<PendingPackagePayment>.Success(pendingPayment);
            }

            if (pendingPayment.Status == PendingPackagePaymentStatus.AmountMismatch)
            {
                _logger.LogWarning(
                    "CompletePackagePayment retry blocked for previously flagged TxRef: {TxRef} (AmountMismatch).",
                    transactionReference);
                return Result<PendingPackagePayment>.Failure(new List<string> { "This payment was previously flagged for amount mismatch." });
            }

            // CRITICAL SECURITY CHECK: verify the paid amount matches what was quoted (tolerance 0.01)
            if (Math.Abs(paidAmount - pendingPayment.TotalAmount) > 0.01m)
            {
                _logger.LogCritical(
                    "PACKAGE PAYMENT AMOUNT MISMATCH! TxRef: {TxRef}, Expected: {Expected}, Paid: {Paid}. Possible tampering attempt.",
                    transactionReference, pendingPayment.TotalAmount, paidAmount);

                pendingPayment.Status = PendingPackagePaymentStatus.AmountMismatch;
                pendingPayment.ErrorMessage = $"Amount mismatch. Expected: {pendingPayment.TotalAmount}, Paid: {paidAmount}";
                await _dbContext.SaveChangesAsync();

                return Result<PendingPackagePayment>.Failure(new List<string> { "Payment amount does not match. This incident has been logged." });
            }

            string notes = BuildPackageRequestNotes(pendingPayment);

            try
            {
                var packageRequest = await _packageRequestService.CreateAsync(pendingPayment.ClientId, new CreatePackageRequestRequest
                {
                    PackageId = pendingPayment.PackageId,
                    Notes = notes
                });

                pendingPayment.Status = PendingPackagePaymentStatus.Completed;
                pendingPayment.FlutterwaveTransactionId = flutterwaveTransactionId;
                pendingPayment.CompletedAt = DateTime.UtcNow;
                pendingPayment.PackageRequestId = packageRequest.Id;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Package payment completed. TxRef: {TxRef}, FlwTxId: {FlwTxId}, PackageRequestId: {PackageRequestId}",
                    transactionReference, flutterwaveTransactionId, packageRequest.Id);

                return Result<PendingPackagePayment>.Success(pendingPayment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to create PackageRequest for TxRef: {TxRef}. Payment was received but no PackageRequest exists — needs manual follow-up.",
                    transactionReference);

                pendingPayment.Status = PendingPackagePaymentStatus.Failed;
                pendingPayment.ErrorMessage = "Payment received but failed to create the package request. Please contact support.";
                await _dbContext.SaveChangesAsync();

                return Result<PendingPackagePayment>.Failure(new List<string> { "Failed to create package request after payment." });
            }
        }

        private string BuildRedirectUrl(string transactionReference)
        {
            var frontendUrl = (_configuration["FrontendUrl"] ?? "https://oncarepro.com").TrimEnd('/');
            return $"{frontendUrl}/app/client/dashboard?ref={transactionReference}";
        }

        private static string? BuildPackageRequestNotes(PendingPackagePayment payment)
        {
            var note = payment.Notes;
            if (payment.ExtraDays > 0)
            {
                var addOnNote = $"Includes {payment.ExtraDays} extra day(s) — ₦{payment.AdditionalDayAmount:N0} add-on paid (TxRef: {payment.TransactionReference}).";
                note = string.IsNullOrWhiteSpace(note) ? addOnNote : $"{note}\n{addOnNote}";
            }
            return note;
        }

        private string? ExtractPaymentLink(string flutterwaveResponse)
        {
            try
            {
                var response = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(flutterwaveResponse);
                if (response.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("link", out var link))
                {
                    return link.GetString();
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to parse Flutterwave payment initiation response in PackagePaymentService. Response: {Response}",
                    flutterwaveResponse);
                return null;
            }
        }
    }
}
