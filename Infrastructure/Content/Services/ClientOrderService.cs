using Application.DTOs;
using Application.Interfaces;
using Application.Interfaces.Content;
using Domain.Entities;
using Infrastructure.Content.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Org.BouncyCastle.Asn1.X509;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class ClientOrderService : IClientOrderService
    {
        private readonly CareProDbContext careProDbContext;
        private readonly IGigServices gigServices;
        private readonly ICareGiverService careGiverService;
        private readonly IClientService clientService;
        private readonly ILogger<GigServices> logger;
        private readonly INotificationService notificationService;
        private readonly IOrderTasksService orderTasksService;
        private readonly IContractService contractService;

        public ClientOrderService(
            CareProDbContext careProDbContext,
            IGigServices gigServices,
            ICareGiverService careGiverService,
            IClientService clientService,
            ILogger<GigServices> logger,
            INotificationService notificationService,
            IOrderTasksService orderTasksService,
            IContractService contractService)
        {
            this.careProDbContext = careProDbContext;
            this.gigServices = gigServices;
            this.careGiverService = careGiverService;
            this.clientService = clientService;
            this.logger = logger;
            this.notificationService = notificationService;
            this.orderTasksService = orderTasksService;
            this.contractService = contractService;
        }

        public async Task<Result<ClientOrderDTO>> CreateClientOrderAsync(AddClientOrderRequest addClientOrderRequest)
        {
            var errors = new List<string>();

            // Add null checks for required parameters
            if (string.IsNullOrEmpty(addClientOrderRequest.ClientId))
            {
                errors.Add("ClientId is required.");
            }

            if (string.IsNullOrEmpty(addClientOrderRequest.GigId))
            {
                errors.Add("GigId is required.");
            }

            if (errors.Any())
            {
                return Result<ClientOrderDTO>.Failure(errors);
            }

            var client = await clientService.GetClientUserAsync(addClientOrderRequest.ClientId!);
            if (client == null)
            {
                errors.Add("The ClientID entered is not a valid ID.");
            }

            var gig = await gigServices.GetGigAsync(addClientOrderRequest.GigId!);
            if (gig == null)
            {
                errors.Add("The GigID entered is not a valid ID.");
            }

            if (errors.Any())
            {
                return Result<ClientOrderDTO>.Failure(errors);
            }

            // Convert DTO to domain object (we've already validated that client and gig are not null)           
            var clientOrder = new ClientOrder
            {
                ClientId = client!.Id ?? throw new InvalidOperationException("Client ID cannot be null"),
                GigId = gig!.Id,
                PaymentOption = addClientOrderRequest.PaymentOption ?? string.Empty,
                Amount = addClientOrderRequest.Amount,
                TransactionId = addClientOrderRequest.TransactionId ?? string.Empty,

                // Assign new ID
                Id = ObjectId.GenerateNewId(),
                CaregiverId = gig.CaregiverId,
                ClientOrderStatus = "In Progress",
                IsOrderStatusApproved = false,
                HasDispute = false,
                OrderCreatedAt = DateTime.Now,
            };

            await careProDbContext.ClientOrders.AddAsync(clientOrder);
            await careProDbContext.SaveChangesAsync();

            // Link OrderTasks to the created ClientOrder
            try
            {
                if (!string.IsNullOrEmpty(addClientOrderRequest.OrderTasksId))
                {
                    var orderTasksLinked = await orderTasksService.LinkToClientOrderAsync(
                        addClientOrderRequest.OrderTasksId,
                        clientOrder.Id.ToString());

                    if (orderTasksLinked)
                    {
                        await orderTasksService.MarkAsPaidAsync(
                            addClientOrderRequest.OrderTasksId,
                            clientOrder.Id.ToString());

                        // Contract generation is now manual via frontend button
                        logger.LogInformation("OrderTasks {OrderTasksId} linked to ClientOrder {ClientOrderId}. Contract generation can be triggered manually.",
                            addClientOrderRequest.OrderTasksId, clientOrder.Id.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error linking OrderTasks or generating contract for ClientOrder {ClientOrderId}",
                    clientOrder.Id.ToString());
                // Don't fail the order creation, but log the error
            }

            // Create notification for the caregiver
            var caregiver = await careGiverService.GetCaregiverUserAsync(clientOrder.CaregiverId);
            if (caregiver != null)
            {
                string notificationContent = $"New order received for your service: {gig!.Title} - Amount: ₦{clientOrder.Amount} from {client!.FirstName} {client.LastName}";

                await notificationService.CreateNotificationAsync(
                    clientOrder.CaregiverId,
                    clientOrder.ClientId ?? string.Empty,
                    "OrderReceived",
                    notificationContent,
                    "New Order Received",
                    clientOrder.Id.ToString()
                );
            }

            var clientOrderDTO = new ClientOrderDTO
            {
                Id = clientOrder.Id.ToString(),
                ClientId = clientOrder.ClientId,
                CaregiverId = clientOrder.CaregiverId,
                GigId = clientOrder.GigId,
                PaymentOption = clientOrder.PaymentOption,
                Amount = clientOrder.Amount,
                TransactionId = clientOrder.TransactionId,
                OrderCreatedAt = clientOrder.OrderCreatedAt,
            };

            return Result<ClientOrderDTO>.Success(clientOrderDTO);
        }

        /// <summary>
        /// Triggers contract generation using OrderTasks data for richer contract content
        /// </summary>
        private async Task TriggerContractGenerationAsync(string orderTasksId, string transactionId)
        {
            try
            {
                // Prepare contract data from OrderTasks
                var contractData = await orderTasksService.PrepareContractDataAsync(orderTasksId, transactionId);

                // Generate the contract using the existing GenerateContractAsync method
                await contractService.GenerateContractAsync(contractData);

                // Mark OrderTasks as contract generated
                await orderTasksService.MarkAsContractGeneratedAsync(orderTasksId);

                logger.LogInformation("Contract generated successfully for OrderTasks {OrderTasksId}", orderTasksId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate contract for OrderTasks {OrderTasksId}", orderTasksId);
                throw; // Re-throw to be handled by caller
            }
        }


        public async Task<CaregiverClientOrdersSummaryResponse> GetAllCaregiverOrderAsync(string caregiverId)
        {

            //var orders = await careProDbContext.ClientOrders
            //   .Where(x => x.CaregiverId == caregiverId && x.ClientOrderStatus == "Completed"  &&( x.IsOrderStatusApproved == true || x.OrderUpdatedOn >= x.OrderUpdatedOn.Ad))
            //   .OrderBy(x => x.OrderCreatedAt)
            //   .ToListAsync();


            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

            var orders = await careProDbContext.ClientOrders
               .Where(x => x.CaregiverId == caregiverId
                       && x.ClientOrderStatus == "Completed"
                       && (x.IsOrderStatusApproved == true || x.OrderUpdatedOn <= sevenDaysAgo))
               .OrderBy(x => x.OrderCreatedAt)
               .ToListAsync();



            var caregiverOrders = new List<ClientOrderResponse>();
            decimal totalEarning = 0;

            foreach (var caregiverOrder in orders)
            {
                var gig = await gigServices.GetGigAsync(caregiverOrder.GigId);
                if (gig == null)
                {
                    throw new KeyNotFoundException("The GigID entered is not a Valid ID");
                }

                var caregiver = await careGiverService.GetCaregiverUserAsync(caregiverId);
                if (caregiver == null)
                {
                    throw new KeyNotFoundException("The UserId entered is not a Valid ID");
                }

                //var client = await careGiverService.GetCaregiverUserAsync(caregiverOrder.ClientId);
                var client = await clientService.GetClientUserAsync(caregiverOrder.ClientId);
                if (client == null)
                {
                    throw new KeyNotFoundException("The ClientId entered is not a Valid ID");
                }

                totalEarning += caregiverOrder.Amount;

                var caregiverOrderDTO = new ClientOrderResponse()
                {
                    Id = caregiverOrder.Id.ToString(),
                    ClientId = caregiverOrder.ClientId,
                    ClientName = client.FirstName + " " + client.LastName,

                    CaregiverId = gig.CaregiverId,
                    CaregiverName = caregiver.FirstName + " " + caregiver.LastName,

                    GigId = caregiverOrder.GigId,
                    GigTitle = gig.Title,
                    GigPackageDetails = gig.PackageDetails,
                    GigImage = gig.Image1,
                    GigStatus = gig.Status,


                    PaymentOption = caregiverOrder.PaymentOption,
                    Amount = caregiverOrder.Amount,
                    TransactionId = caregiverOrder.TransactionId,
                    ClientOrderStatus = caregiverOrder.ClientOrderStatus,
                    OrderCreatedOn = caregiverOrder.OrderCreatedAt,

                };

                caregiverOrders.Add(caregiverOrderDTO);
            }

            return new CaregiverClientOrdersSummaryResponse
            {
                NoOfOrders = caregiverOrders.Count,
                TotalEarning = totalEarning,
                ClientOrders = caregiverOrders,
            };
        }

        public async Task<IEnumerable<ClientOrderResponse>> GetAllClientOrderAsync(string clientUserId)
        {
            var orders = await careProDbContext.ClientOrders
               .Where(x => x.ClientId == clientUserId)
               .OrderBy(x => x.OrderCreatedAt)
               .ToListAsync();

            var clientOrdersDTOs = new List<ClientOrderResponse>();

            foreach (var clientOrder in orders)
            {
                var gig = await gigServices.GetGigAsync(clientOrder.GigId);
                if (gig == null)
                {
                    throw new KeyNotFoundException("The GigID entered is not a Valid ID");
                }

                var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);
                if (caregiver == null)
                {
                    throw new KeyNotFoundException("The UserId entered is not a Valid ID");
                }

                //var client = await careGiverService.GetCaregiverUserAsync(clientOrder.ClientId);
                var client = await clientService.GetClientUserAsync(clientOrder.ClientId);
                if (client == null)
                {
                    throw new KeyNotFoundException("The ClientId entered is not a Valid ID");
                }

                var clientOrderDTO = new ClientOrderResponse()
                {
                    Id = clientOrder.Id.ToString(),
                    ClientId = clientOrder.ClientId,
                    ClientName = client.FirstName + " " + client.LastName,

                    CaregiverId = gig.CaregiverId,
                    CaregiverName = caregiver.FirstName + " " + caregiver.LastName,

                    GigId = clientOrder.GigId,
                    GigTitle = gig.Title,
                    GigImage = gig.Image1,
                    GigPackageDetails = gig.PackageDetails,
                    GigStatus = gig.Status,


                    PaymentOption = clientOrder.PaymentOption,
                    Amount = clientOrder.Amount,
                    TransactionId = clientOrder.TransactionId,
                    ClientOrderStatus = clientOrder.ClientOrderStatus,
                    NoOfOrders = clientOrdersDTOs.Count(),
                    OrderCreatedOn = clientOrder.OrderCreatedAt,

                };

                clientOrdersDTOs.Add(clientOrderDTO);
            }

            return clientOrdersDTOs;
        }

        public async Task<IEnumerable<ClientOrderResponse>> GetCaregiverOrdersAsync(string caregiverId)
        {
            var orders = await careProDbContext.ClientOrders
               .Where(x => x.CaregiverId == caregiverId)
               .OrderBy(x => x.OrderCreatedAt)
               .ToListAsync();

            var clientOrdersDTOs = new List<ClientOrderResponse>();

            foreach (var clientOrder in orders)
            {
                var gig = await gigServices.GetGigAsync(clientOrder.GigId);
                if (gig == null)
                {
                    throw new KeyNotFoundException("The GigID entered is not a Valid ID");
                }

                var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);
                if (caregiver == null)
                {
                    throw new KeyNotFoundException("The UserId entered is not a Valid ID");
                }

                //var client = await careGiverService.GetCaregiverUserAsync(clientOrder.ClientId);
                var client = await clientService.GetClientUserAsync(clientOrder.ClientId);
                if (client == null)
                {
                    throw new KeyNotFoundException("The ClientId entered is not a Valid ID");
                }

                var clientOrderDTO = new ClientOrderResponse()
                {
                    Id = clientOrder.Id.ToString(),
                    ClientId = clientOrder.ClientId,
                    ClientName = client.FirstName + " " + client.LastName,

                    CaregiverId = gig.CaregiverId,
                    CaregiverName = caregiver.FirstName + " " + caregiver.LastName,

                    GigId = clientOrder.GigId,
                    GigTitle = gig.Title,
                    GigImage = gig.Image1,
                    GigPackageDetails = gig.PackageDetails,
                    GigStatus = gig.Status,


                    PaymentOption = clientOrder.PaymentOption,
                    Amount = clientOrder.Amount,
                    TransactionId = clientOrder.TransactionId,
                    ClientOrderStatus = clientOrder.ClientOrderStatus,
                    NoOfOrders = clientOrdersDTOs.Count(),
                    OrderCreatedOn = clientOrder.OrderCreatedAt,

                };

                clientOrdersDTOs.Add(clientOrderDTO);
            }

            return clientOrdersDTOs;
        }

        public async Task<IEnumerable<ClientOrderResponse>> GetAllClientOrdersByGigIdAsync(string gigId)
        {
            var orders = await careProDbContext.ClientOrders
               .Where(x => x.GigId == gigId)
               .OrderBy(x => x.OrderCreatedAt)
               .ToListAsync();

            var clientOrdersDTOs = new List<ClientOrderResponse>();

            foreach (var clientOrder in orders)
            {
                var gig = await gigServices.GetGigAsync(clientOrder.GigId);
                if (gig == null)
                {
                    throw new KeyNotFoundException("The GigID entered is not a Valid ID");
                }

                var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);
                if (caregiver == null)
                {
                    throw new KeyNotFoundException("The UserId entered is not a Valid ID");
                }

                //var client = await careGiverService.GetCaregiverUserAsync(clientOrder.ClientId);
                var client = await clientService.GetClientUserAsync(clientOrder.ClientId);
                if (client == null)
                {
                    throw new KeyNotFoundException("The ClientId entered is not a Valid ID");
                }

                var clientOrderDTO = new ClientOrderResponse()
                {
                    Id = clientOrder.Id.ToString(),
                    ClientId = clientOrder.ClientId,
                    ClientName = client.FirstName + " " + client.LastName,

                    CaregiverId = gig.CaregiverId,
                    CaregiverName = caregiver.FirstName + " " + caregiver.LastName,

                    GigId = clientOrder.GigId,
                    GigTitle = gig.Title,
                    GigImage = gig.Image1,
                    GigPackageDetails = gig.PackageDetails,
                    GigStatus = gig.Status,


                    PaymentOption = clientOrder.PaymentOption,
                    Amount = clientOrder.Amount,
                    TransactionId = clientOrder.TransactionId,
                    ClientOrderStatus = clientOrder.ClientOrderStatus,
                    NoOfOrders = clientOrdersDTOs.Count(),
                    OrderCreatedOn = clientOrder.OrderCreatedAt,

                };

                clientOrdersDTOs.Add(clientOrderDTO);
            }

            return clientOrdersDTOs;
        }




        //public async Task<IEnumerable<ClientOrderResponse>> GetClientOrdersAsync(string clientUserId)
        //{
        //    var pipeline = careProDbContext.ClientOrders
        //        .Aggregate()
        //        // Match client orders by clientId
        //        .Match(x => x.ClientId == clientUserId)

        //        // Join → Gigs collection
        //        .Lookup(
        //            foreignCollection: careProDbContext.Gigs,
        //            localField: o => o.GigId,
        //            foreignField: g => g.Id,
        //            @as: (ClientOrderWithGig temp) => temp.Gigs
        //        )

        //        // Flatten joined gig
        //        .Unwind<ClientOrderWithGig, ClientOrderWithGig>(x => x.Gigs)

        //        // Join → Caregivers collection
        //        .Lookup(
        //            foreignCollection: careProDbContext.CareGivers,
        //            localField: x => x.Gigs.CaregiverId,
        //            foreignField: c => c.Id,
        //            @as: (ClientOrderWithGigCaregiver temp) => temp.Caregivers
        //        )
        //        .Unwind<ClientOrderWithGigCaregiver, ClientOrderWithGigCaregiver>(x => x.Caregivers)

        //        // Join → Clients collection
        //        .Lookup(
        //            foreignCollection: careProDbContext.Clients,
        //            localField: x => x.ClientId,
        //            foreignField: c => c.Id,
        //            @as: (ClientOrderFull temp) => temp.Clients
        //        )
        //        .Unwind<ClientOrderFull, ClientOrderFull>(x => x.Clients)

        //        // Project result to DTO
        //        .Project(x => new ClientOrderResponse
        //        {
        //            Id = x.Id.ToString(),
        //            ClientId = x.ClientId,
        //            ClientName = x.Clients.FirstName + " " + x.Clients.LastName,
        //            CaregiverId = x.Gigs.CaregiverId,
        //            CaregiverName = x.Caregivers.FirstName + " " + x.Caregivers.LastName,
        //            GigId = x.GigId,
        //            GigTitle = x.Gigs.Title,
        //            GigImage = x.Gigs.Image1,
        //            GigPackageDetails = x.Gigs.PackageDetails,
        //            GigStatus = x.Gigs.Status,
        //            PaymentOption = x.PaymentOption,
        //            Amount = x.Amount,
        //            TransactionId = x.TransactionId,
        //            ClientOrderStatus = x.ClientOrderStatus,
        //            OrderCreatedOn = x.OrderCreatedAt
        //        });

        //    var results = await pipeline.ToListAsync();
        //    return results;
        //}




        public async Task<ClientOrderResponse> GetClientOrderAsync(string orderId)
        {
            var order = await careProDbContext.ClientOrders.FirstOrDefaultAsync(x => x.Id.ToString() == orderId);

            if (order == null)
            {
                throw new KeyNotFoundException($"Gig with ID '{orderId}' not found.");
            }

            var gig = await gigServices.GetGigAsync(order.GigId);
            if (gig == null)
            {
                throw new KeyNotFoundException("The GigID entered is not a Valid ID");
            }

            var caregiver = await careGiverService.GetCaregiverUserAsync(gig.CaregiverId);
            if (caregiver == null)
            {
                throw new KeyNotFoundException("The UserId entered is not a Valid ID");
            }

            //var client = await careGiverService.GetCaregiverUserAsync(order.ClientId);
            var client = await clientService.GetClientUserAsync(order.ClientId);
            if (client == null)
            {
                throw new KeyNotFoundException("The ClientId entered is not a Valid ID");
            }

            var clientOrderDTO = new ClientOrderResponse()
            {
                Id = order.Id.ToString(),
                ClientId = order.ClientId,
                ClientName = client.FirstName + " " + client.LastName,

                GigId = order.GigId,
                GigTitle = gig.Title,
                GigPackageDetails = gig.PackageDetails,
                GigStatus = gig.Status,
                GigImage = gig.Image1,


                CaregiverId = gig.CaregiverId,
                CaregiverName = caregiver.FirstName + " " + caregiver.LastName,

                PaymentOption = order.PaymentOption,
                Amount = order.Amount,
                TransactionId = order.TransactionId,
                ClientOrderStatus = order.ClientOrderStatus,
                OrderCreatedOn = order.OrderCreatedAt,

            };

            return clientOrderDTO;
        }

        public async Task<string> UpdateClientOrderStatusAsync(string orderId, UpdateClientOrderStatusRequest updateClientOrderStatusRequest)
        {

            try
            {
                if (!ObjectId.TryParse(orderId, out var objectId))
                {
                    throw new ArgumentException("Invalid order ID format.");
                }

                var existingOrder = await careProDbContext.ClientOrders.FindAsync(objectId);

                if (existingOrder == null)
                {
                    throw new KeyNotFoundException($"Order with ID '{orderId}' not found.");
                }


                existingOrder.ClientOrderStatus = updateClientOrderStatusRequest.ClientOrderStatus;
                existingOrder.OrderUpdatedOn = DateTime.Now;


                careProDbContext.ClientOrders.Update(existingOrder);
                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Order Status updated (ID: {orderId})", updateClientOrderStatusRequest.UserId);
                return $"Order with ID '{orderId}' updated successfully.";
            }
            catch (Exception ex)
            {
                LogException(ex);
                throw new Exception(ex.Message);
            }
        }

        public async Task<string> UpdateOrderStatusToApproveAsync(string orderId)
        {
            try
            {
                if (!ObjectId.TryParse(orderId, out var objectId))
                {
                    throw new ArgumentException("Invalid order ID format.");
                }

                var existingOrder = await careProDbContext.ClientOrders.FindAsync(objectId);

                if (existingOrder == null)
                {
                    throw new KeyNotFoundException($"Order with ID '{orderId}' not found.");
                }


                existingOrder.IsOrderStatusApproved = true;
                existingOrder.OrderUpdatedOn = DateTime.Now;


                careProDbContext.ClientOrders.Update(existingOrder);
                await careProDbContext.SaveChangesAsync();

                return $"Order with ID '{orderId}' updated successfully.";
            }
            catch (Exception ex)
            {
                LogException(ex);
                throw new Exception(ex.Message);
            }
        }

        public async Task<string> UpdateClientOrderStatusHasDisputeAsync(string orderId, UpdateClientOrderStatusHasDisputeRequest updateClientOrderStatusHasDisputeRequest)
        {
            try
            {
                if (!ObjectId.TryParse(orderId, out var objectId))
                {
                    throw new ArgumentException("Invalid order ID format.");
                }

                var existingOrder = await careProDbContext.ClientOrders.FindAsync(objectId);

                if (existingOrder == null)
                {
                    throw new KeyNotFoundException($"Order with ID '{orderId}' not found.");
                }



                //var existingOrder = await careProDbContext.ClientOrders.FindAsync(orderId);

                //if (existingOrder == null)
                //{
                //    throw new KeyNotFoundException($"Order with ID '{orderId}' not found.");
                //}


                existingOrder.ClientOrderStatus = updateClientOrderStatusHasDisputeRequest.ClientOrderStatus;
                existingOrder.HasDispute = true;
                existingOrder.DisputeReason = updateClientOrderStatusHasDisputeRequest.DisputeReason;
                existingOrder.OrderUpdatedOn = DateTime.Now;


                careProDbContext.ClientOrders.Update(existingOrder);
                await careProDbContext.SaveChangesAsync();

                LogAuditEvent($"Order Status updated (ID: {orderId})", updateClientOrderStatusHasDisputeRequest.UserId);
                return $"Order with ID '{orderId}' updated successfully.";
            }
            catch (Exception ex)
            {
                LogException(ex);
                throw new Exception(ex.Message);
            }
        }


        private void LogException(Exception ex)
        {
            logger.LogError(ex, "Exception occurred");
        }

        private void LogAuditEvent(object message, string? userId)
        {
            logger.LogInformation($"Audit Event: {message}. User ID: {userId}. Timestamp: {DateTime.UtcNow}");
        }


    }



}
