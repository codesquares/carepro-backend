using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Authentication;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminsController : ControllerBase
    {
        private readonly IAdminUserService adminUserService;

        public AdminsController(IAdminUserService adminUserService)
        {
            this.adminUserService = adminUserService;
        }

        /// ENDPOINT TO CREATE  ADMIN USERS TO THE DATABASE        
        [HttpPost]
       // [Route("AdminUser")]
        public async Task<IActionResult> AddAdminUserAsync([FromBody] AddAdminUserRequest  addAdminUserRequest)
        {
            try
            {                
                // Pass Domain Object to Repository to Persist this
                var adminUser = await adminUserService.CreateAdminUserAsync(addAdminUserRequest);

                // Send DTO response back to ClientUser
                return Ok(adminUser);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message }); // Or BadRequest
            }
            catch (AuthenticationException authEx)
            {
                // Handle authentication-related exceptions
                return BadRequest(new { StatusCode = 400, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database update-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }
        }


        [HttpGet]
        [Route("AllAdminUsers")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetAllCaregiverAsync()
        {
            try
            {
                var adminUsers = await adminUserService.GetAllAdminUsersAsync();
                return Ok(adminUsers);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message }); // Or BadRequest
            }
            catch (AuthenticationException authEx)
            {
                // Handle authentication-related exceptions
                return BadRequest(new { StatusCode = 400, ErrorMessage = authEx.Message });
            }
            catch (HttpRequestException httpEx)
            {
                // Handle HTTP request-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = httpEx.Message });
            }
            catch (DbUpdateException dbEx)
            {
                // Handle database update-related exceptions
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = dbEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { StatusCode = 500, ErrorMessage = ex.Message });
            }

        }


        [HttpGet]
        [Route("{adminUserId}")]
        //[Authorize(Roles = "Client,Admin")]
        public async Task<IActionResult> GetAdminUserAsync(string adminUserId)
        {
            try
            {
                var adminUser = await adminUserService.GetAdminUserByIdAsync(adminUserId);
                return Ok(adminUser);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

    }
}
