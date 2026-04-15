using Application.DTOs;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Infrastructure.Content.Services
{
    public class BillingRecordService : IBillingRecordService
    {
        private readonly CareProDbContext _dbContext;
        private readonly ILogger<BillingRecordService> _logger;

        public BillingRecordService(CareProDbContext dbContext, ILogger<BillingRecordService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<BillingRecordDTO> CreateBillingRecordAsync(
            string orderId, string clientId, string caregiverId, string gigId,
            string serviceType, int frequencyPerWeek,
            decimal amountPaid, decimal orderFee, decimal serviceCharge, decimal gatewayFees,
            string paymentTransactionId,
            string? subscriptionId = null, string? contractId = null,
            int billingCycleNumber = 1,
            DateTime? periodStart = null, DateTime? periodEnd = null, DateTime? nextChargeDate = null)
        {
            var now = DateTime.UtcNow;

            var record = new BillingRecord
            {
                Id = ObjectId.GenerateNewId().ToString(),
                OrderId = orderId,
                SubscriptionId = subscriptionId,
                ContractId = contractId,
                CaregiverId = caregiverId,
                ClientId = clientId,
                GigId = gigId,
                BillingCycleNumber = billingCycleNumber,
                ServiceType = serviceType,
                FrequencyPerWeek = frequencyPerWeek,
                PeriodStart = periodStart ?? now,
                PeriodEnd = periodEnd ?? (serviceType == "monthly" ? now.AddDays(30) : (DateTime?)null),
                NextChargeDate = nextChargeDate ?? (serviceType == "monthly" ? now.AddDays(30) : (DateTime?)null),
                AmountPaid = amountPaid,
                OrderFee = orderFee,
                ServiceCharge = serviceCharge,
                GatewayFees = gatewayFees,
                PaymentTransactionId = paymentTransactionId,
                Status = "Paid",
                CreatedAt = now
            };

            _dbContext.BillingRecords.Add(record);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "BillingRecord created: {RecordId} for order {OrderId}, type {ServiceType}, cycle {Cycle}",
                record.Id, orderId, serviceType, billingCycleNumber);

            return MapToDTO(record);
        }

        public async Task<BillingRecordDTO?> GetBillingRecordByIdAsync(string id)
        {
            var record = await _dbContext.BillingRecords
                .FirstOrDefaultAsync(br => br.Id == id);

            return record != null ? MapToDTO(record) : null;
        }

        public async Task<BillingRecordDTO?> GetBillingRecordByOrderIdAsync(string orderId)
        {
            var record = await _dbContext.BillingRecords
                .FirstOrDefaultAsync(br => br.OrderId == orderId);

            return record != null ? MapToDTO(record) : null;
        }

        public async Task<List<BillingRecordDTO>> GetBillingRecordsBySubscriptionIdAsync(string subscriptionId)
        {
            var records = await _dbContext.BillingRecords
                .Where(br => br.SubscriptionId == subscriptionId)
                .OrderByDescending(br => br.CreatedAt)
                .ToListAsync();

            return records.Select(MapToDTO).ToList();
        }

        public async Task<List<BillingRecordDTO>> GetClientBillingRecordsAsync(string clientId)
        {
            var records = await _dbContext.BillingRecords
                .Where(br => br.ClientId == clientId)
                .OrderByDescending(br => br.CreatedAt)
                .ToListAsync();

            return records.Select(MapToDTO).ToList();
        }

        public async Task<List<BillingRecordDTO>> GetCaregiverBillingRecordsAsync(string caregiverId)
        {
            var records = await _dbContext.BillingRecords
                .Where(br => br.CaregiverId == caregiverId)
                .OrderByDescending(br => br.CreatedAt)
                .ToListAsync();

            return records.Select(MapToDTO).ToList();
        }

        public async Task<bool> MarkAsRefundedAsync(string billingRecordId)
        {
            var record = await _dbContext.BillingRecords
                .FirstOrDefaultAsync(br => br.Id == billingRecordId);

            if (record == null) return false;

            record.Status = "Refunded";
            _dbContext.BillingRecords.Update(record);
            await _dbContext.SaveChangesAsync();

            return true;
        }

        public async Task<bool> MarkAsDisputedAsync(string billingRecordId)
        {
            var record = await _dbContext.BillingRecords
                .FirstOrDefaultAsync(br => br.Id == billingRecordId);

            if (record == null) return false;

            record.Status = "Disputed";
            _dbContext.BillingRecords.Update(record);
            await _dbContext.SaveChangesAsync();

            return true;
        }

        private static BillingRecordDTO MapToDTO(BillingRecord record)
        {
            return new BillingRecordDTO
            {
                Id = record.Id,
                OrderId = record.OrderId,
                SubscriptionId = record.SubscriptionId,
                ContractId = record.ContractId,
                CaregiverId = record.CaregiverId,
                ClientId = record.ClientId,
                GigId = record.GigId,
                BillingCycleNumber = record.BillingCycleNumber,
                ServiceType = record.ServiceType,
                FrequencyPerWeek = record.FrequencyPerWeek,
                PeriodStart = record.PeriodStart,
                PeriodEnd = record.PeriodEnd,
                NextChargeDate = record.NextChargeDate,
                AmountPaid = record.AmountPaid,
                OrderFee = record.OrderFee,
                ServiceCharge = record.ServiceCharge,
                GatewayFees = record.GatewayFees,
                PaymentTransactionId = record.PaymentTransactionId,
                Status = record.Status,
                CreatedAt = record.CreatedAt
            };
        }
    }
}
