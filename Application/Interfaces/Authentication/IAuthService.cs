using Application.DTOs.Authentication;
using Application.DTOs;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Interfaces.Authentication
{
    public interface IAuthService
    {
        Task<AppUserDTO> AuthenticateUserAsync(LoginRequest loginRequest);

        Task<LoginResponse> AuthenticateUserLoginAsync(LoginRequest loginRequest);

    }
}
