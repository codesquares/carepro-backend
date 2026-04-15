using MediatR;

namespace Application.Commands
{
    /// <summary>
    /// MediatR command that wraps notification creation. Preserves the exact same
    /// parameters as INotificationService.CreateNotificationAsync so the migration
    /// is transparent — the frontend contract is unchanged.
    /// </summary>
    public record SendNotificationCommand(
        string RecipientId,
        string SenderId,
        string Type,
        string Content,
        string? Title,
        string RelatedEntityId,
        string? OrderId = null
    ) : IRequest<string>;
}
