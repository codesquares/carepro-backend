using Application.DTOs;

namespace Application.Interfaces.Common
{
    public interface IDojahApiService
    {
        Task<DojahVerificationDataResponse> GetAllVerificationDataAsync(
            string? term = null,
            string? start = null,
            string? end = null,
            string? status = null);
    }
}