using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IVisitCheckinService
    {
        Task<VisitCheckinResponse> CheckinAsync(VisitCheckinRequest request, string caregiverId);
        Task<VisitCheckinDTO?> GetCheckinByTaskSheetIdAsync(string taskSheetId);
    }
}
