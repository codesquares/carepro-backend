using Domain.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/public-config")]
    public class PublicConfigController : ControllerBase
    {
        private readonly IOptions<CommitmentFeeSettings> _commitmentFeeSettings;

        private readonly IConfiguration _configuration;

        public PublicConfigController(IOptions<CommitmentFeeSettings> commitmentFeeSettings, IConfiguration configuration)
        {
            _commitmentFeeSettings = commitmentFeeSettings;
            _configuration = configuration;
        }

        /// <summary>
        /// VettingLockout:EnforcementDate (UTC, ISO). Unset or unparseable = null = the caregiver vetting lockout is NOT enforced.
        /// Deliberately fail-OPEN, the opposite of the commitment gate's fail-closed default.
        /// </summary>
        public static DateTime? ParseLockoutDate(string? raw) =>
            !string.IsNullOrWhiteSpace(raw)
            && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
                ? d : null;

        [HttpGet]
        [AllowAnonymous]
        public IActionResult GetPublicConfig()
        {
            Response.Headers["Cache-Control"] = "public, max-age=60";

            return Ok(new
            {
                commitmentGateEnabled = _commitmentFeeSettings.Value.Enabled,
                vettingLockoutEnforcementDate = ParseLockoutDate(_configuration["VettingLockout:EnforcementDate"])
            });
        }
    }
}
