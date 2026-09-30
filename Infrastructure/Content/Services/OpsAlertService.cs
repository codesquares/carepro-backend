using Application.Commands;
using Application.Interfaces.Content;
using Application.Interfaces.Email;
using Domain.Entities;
using Infrastructure.Content.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Content.Services
{
    public class OpsAlertService : IOpsAlertService
    {
        private readonly CareProDbContext _db;
        private readonly IMediator _mediator;
        private readonly IEmailService _emailService;
        private readonly ILogger<OpsAlertService> _logger;

        public OpsAlertService(CareProDbContext db, IMediator mediator, IEmailService emailService, ILogger<OpsAlertService> logger)
        {
            _db = db;
            _mediator = mediator;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task NotifyAdminsAsync(string type, string title, string content, string relatedEntityId, bool alsoEmail = false)
        {
            try
            {
                // Same rule as the login gate (AuthService): only Approved admins have working access, and
                // a null/blank Status (accounts predating the field) counts as Approved. Filtered in memory —
                // the Mongo LINQ provider doesn't reliably translate StringComparison overloads.
                var admins = (await _db.AdminUsers.Where(a => !a.IsDeleted).ToListAsync())
                    .Where(a => string.IsNullOrWhiteSpace(a.Status)
                                || string.Equals(a.Status, AdminUserStatus.Approved, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (admins.Count == 0)
                    _logger.LogError("Ops alert '{Type}' has no admin recipients: {Content}", type, content);

                foreach (var admin in admins)
                {
                    try
                    {
                        await _mediator.Send(new SendNotificationCommand(
                            admin.Id.ToString(), "system", type, content, title, relatedEntityId));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ops in-app alert '{Type}' failed for admin {AdminId}", type, admin.Id);
                    }

                    if (!alsoEmail || string.IsNullOrWhiteSpace(admin.Email)) continue;
                    try
                    {
                        // Always-Send (operational failure needing staff action) — no preference gate.
                        await _emailService.SendGenericNotificationEmailAsync(
                            admin.Email, admin.FirstName ?? "Team", $"[Action needed] {title}", content);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ops alert email '{Type}' failed for admin {AdminId}", type, admin.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ops alert '{Type}' could not be delivered. Content: {Content}", type, content);
            }
        }
    }
}
