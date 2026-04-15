using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ITaskSheetService
    {
        Task<TaskSheetListResponse> GetTaskSheetsByOrderAsync(string orderId, int? billingCycleNumber, string caregiverId, bool isAdmin);
        Task<TaskSheetDTO> CreateTaskSheetAsync(string orderId, string caregiverId);
        Task<TaskSheetDTO> UpdateTaskSheetAsync(string taskSheetId, UpdateTaskSheetRequest request, string caregiverId);
        Task<TaskSheetDTO> SubmitTaskSheetAsync(string taskSheetId, SubmitTaskSheetRequest request, string caregiverId);

        /// <summary>
        /// Client proposes tasks on a task sheet. Tasks start as "Pending" until caregiver accepts.
        /// </summary>
        Task<TaskSheetDTO> ClientProposeTasksAsync(string taskSheetId, ClientProposeTasksRequest request, string clientId);

        /// <summary>
        /// Caregiver accepts or rejects client-proposed tasks on a task sheet.
        /// </summary>
        Task<TaskSheetDTO> RespondToProposedTasksAsync(string taskSheetId, RespondToProposedTasksRequest request, string caregiverId);

        /// <summary>
        /// Activates a scheduled task sheet, transitioning it from "scheduled" to "in-progress".
        /// Enforces the sequential gate: previous sheet must be submitted and reviewed.
        /// </summary>
        Task<TaskSheetDTO> ActivateTaskSheetAsync(string taskSheetId, string caregiverId);

        /// <summary>
        /// Pre-generates all task sheets for a contract when it is approved.
        /// Sheets are created with "scheduled" status and concrete ScheduledDates.
        /// </summary>
        Task PreGenerateTaskSheetsAsync(string contractId, string orderId);

        /// <summary>
        /// Client reschedules a "scheduled" task sheet to a different date within the contract period.
        /// </summary>
        Task<RescheduleTaskSheetResponse> RescheduleTaskSheetAsync(string taskSheetId, RescheduleTaskSheetRequest request, string clientId);
    }
}
