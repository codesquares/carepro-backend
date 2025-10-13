using Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    public interface IClientService
    {
        Task<ClientDTO> CreateClientUserAsync(AddClientUserRequest addClientUserRequest, string? origin);

        Task<string> ConfirmEmailAsync(string token);
        Task<string> ResendEmailConfirmationAsync(string email, string? origin);


        Task<(bool IsValid, string? UserId, string? Email, string? ErrorMessage)> ValidateEmailTokenAsync(string token);

        Task<string> ConfirmEmailFromFrontendAsync(string userId);



        Task<ClientResponse> GetClientUserAsync(string clientId);

        Task<IEnumerable<ClientResponse>> GetAllClientUserAsync();

        Task<string> UpdateClientUserAsync(string clientId, UpdateClientUserRequest updateClientUserRequest );

        Task<string> UpdateProfilePictureAsync(string clientId, UpdateProfilePictureRequest updateProfilePictureRequest);

      //  Task<string> UpdateCaregiverInformationAsync(string caregiverId, UpdateCaregiverAdditionalInfoRequest updateCaregiverAdditionalInfoRequest);



        Task<string> SoftDeleteClientAsync(string clientId);

        Task ChangePasswordAsync(ResetPasswordRequest resetPasswordRequest);

        Task GeneratePasswordResetTokenAsync(PasswordResetRequestDto passwordResetRequestDto, string? origin);

        Task ResetPasswordWithJwtAsync(PasswordResetDto request);

    }
}
