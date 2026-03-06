using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface ITaskSheetService
    {
        Task<TaskSheetListResponse> GetTaskSheetsByOrderAsync(string orderId, int? billingCycleNumber, string caregiverId, bool isAdmin);
        Task<TaskSheetDTO> CreateTaskSheetAsync(string orderId, string caregiverId);
        Task<TaskSheetDTO> UpdateTaskSheetAsync(string taskSheetId, UpdateTaskSheetRequest request, string caregiverId);
        Task<TaskSheetDTO> SubmitTaskSheetAsync(string taskSheetId, string caregiverId);
    }
}
