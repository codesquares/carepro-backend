using Application.Commands;
using Application.Interfaces.Content;
using MediatR;

namespace Infrastructure.Content.Services.Handlers
{
    /// <summary>
    /// Handles SendNotificationCommand by delegating to the existing NotificationService.
    /// This preserves all existing behavior: MongoDB persistence + SignalR real-time push.
    /// </summary>
    public class SendNotificationCommandHandler : IRequestHandler<SendNotificationCommand, string>
    {
        private readonly INotificationService _notificationService;

        public SendNotificationCommandHandler(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task<string> Handle(SendNotificationCommand request, CancellationToken cancellationToken)
        {
            return await _notificationService.CreateNotificationAsync(
                request.RecipientId,
                request.SenderId,
                request.Type,
                request.Content,
                request.Title,
                request.RelatedEntityId,
                request.OrderId
            );
        }
    }
}
