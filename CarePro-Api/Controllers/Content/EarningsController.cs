using Application.DTOs;
using Application.Interfaces.Content;
using Infrastructure.Content.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CarePro_Api.Controllers.Content
{
    [Route("api/[controller]")]
    [ApiController]
   // [Authorize]
    public class EarningsController : ControllerBase
    {
        private readonly IEarningsService _earningsService;

        public EarningsController(IEarningsService earningsService)
        {
            _earningsService = earningsService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetEarningsById(string id)
        {
            try
            {
                var earnings = await _earningsService.GetEarningsByIdAsync(id);
                if (earnings == null)
                    return NotFound("Earnings record not found");

                return Ok(earnings);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpGet("result/{caregiverId}")]
        public async Task<IActionResult> GetEarningsByCaregiverId(string caregiverId)
        {
            try
            {
                var earnings = await _earningsService.GetEarningByCaregiverIdAsync(caregiverId);
                if (earnings == null)
                {
                    // return NotFound("No earnings record found for this result");
                    return Ok (earnings);
                }
                    

                return Ok(earnings);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPost]
       // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> CreateEarnings([FromBody] AddEarningsRequest addEarningsRequest)
        {
            try
            {
                // Check if earnings already exist for this result
                bool exists = await _earningsService.DoesEarningsExistForCaregiverAsync(addEarningsRequest.CaregiverId);
                if (exists)
                    return BadRequest("Earnings record already exists for this result");

                var earnings = await _earningsService.CreateEarningsAsync(addEarningsRequest);
               // return CreatedAtAction(nameof(GetEarningsById), new { id = earnings.Id }, earnings);
                return Ok (earnings);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }

        [HttpPut("{id}")]
       // [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateEarnings(string id, [FromBody] UpdateEarningsRequest request)
        {
            try
            {
                var earnings = await _earningsService.UpdateEarningsAsync(id, request);
                if (earnings == null)
                    return NotFound("Earnings record not found");

                return Ok(earnings);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }



        [HttpGet("EarningHistory/{caregiverId}")]
        public async Task<IActionResult> GetAllEarningsByCaregiverAsync(string caregiverId)
        {
            try
            {
                var result = await _earningsService.GetAllCaregiverEarningAsync(caregiverId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }



        [HttpGet("transaction-history/{caregiverId}")]
        public async Task<IActionResult> GetCaregiverTransactionHistoryAsync(string caregiverId)
        {
            try
            {
                var result = await _earningsService.GetCaregiverTransactionHistoryAsync(caregiverId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ErrorMessage = ex.Message });
            }
        }
    }
}
