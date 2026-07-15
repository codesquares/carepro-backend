using Domain.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CarePro_Api.Controllers
{
    [ApiController]
    [Route("api/public-config")]
    public class PublicConfigController : ControllerBase
    {
        private readonly IOptions<CommitmentFeeSettings> _commitmentFeeSettings;

        public PublicConfigController(IOptions<CommitmentFeeSettings> commitmentFeeSettings)
        {
            _commitmentFeeSettings = commitmentFeeSettings;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult GetPublicConfig()
        {
            Response.Headers["Cache-Control"] = "public, max-age=60";

            return Ok(new
            {
                commitmentGateEnabled = _commitmentFeeSettings.Value.Enabled
            });
        }
    }
}
