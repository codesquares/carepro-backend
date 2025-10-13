using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    
    public class DailyEarningService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DailyEarningService> _logger;

        public DailyEarningService(IServiceScopeFactory scopeFactory, ILogger<DailyEarningService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
               // var nextRun = DateTime.Today.AddDays(1);
                var nextRun = DateTime.Now.AddMinutes(10);
                var delay = nextRun - now;

                _logger.LogInformation("DailyEarningService will run in {Delay}", delay);

                await Task.Delay(delay, stoppingToken);

                try
                {
                    await ProcessEarningsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing earnings.");
                }
            }
        }


        private async Task ProcessEarningsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareProDbContext>();

            var cutoffDate = DateTime.UtcNow.AddDays(-7);

            var eligibleOrders = await dbContext.ClientOrders
                .Where(o => o.ClientOrderStatus == "Completed" &&
                            (o.IsOrderStatusApproved || o.OrderUpdatedOn <= cutoffDate))
                .ToListAsync();

            foreach (var order in eligibleOrders)
            {
                var exists = await dbContext.Earnings.AnyAsync(e => e.ClientOrderId == order.Id.ToString());
                if (exists) continue;

                dbContext.Earnings.Add(new Earnings
                {
                    Id = ObjectId.GenerateNewId(),
                    ClientOrderId = order.Id.ToString(),
                    ClientOrderStatus = order.ClientOrderStatus,
                    CaregiverId = order.CaregiverId,
                    Amount = order.Amount,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync();
        }
    }


}
