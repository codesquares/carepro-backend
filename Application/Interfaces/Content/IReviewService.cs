using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IReviewService
    {
        Task<string> CreateReviewAsync(AddReviewRequest addReviewRequest );

        Task<IEnumerable<ReviewResponse>> GetAllGigReviewAsync(string gigId );

        Task<ReviewResponse> GetGigReviewAsync(string reviewId );

        Task<int> GetReviewCountAsync(string gigId);

    }
}
