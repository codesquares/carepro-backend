using Application.DTOs;
using Application.Interfaces.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarePro_Api.Controllers.Content
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Policy = "AnalyticsPolicy")]
    public class AdminExportController : ControllerBase
    {
        private readonly IAdminExportService _exportService;
        private readonly ICaregiverSnapshotService _snapshotService;
        private readonly ILogger<AdminExportController> _logger;

        public AdminExportController(
            IAdminExportService exportService,
            ICaregiverSnapshotService snapshotService,
            ILogger<AdminExportController> logger)
        {
            _exportService = exportService;
            _snapshotService = snapshotService;
            _logger = logger;
        }

        // ── Excel exports ──────────────────────────────────────────────────────

        /// <summary>
        /// Export all caregivers (optionally filtered by registration date) as Excel.
        /// </summary>
        [HttpGet("export/caregivers")]
        public async Task<IActionResult> ExportCaregivers([FromQuery] ExportQuery query)
        {
            var bytes = await _exportService.ExportCaregiversAsync(query);
            var filename = $"caregivers_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        }

        /// <summary>
        /// Export all clients (optionally filtered by registration date) as Excel.
        /// </summary>
        [HttpGet("export/clients")]
        public async Task<IActionResult> ExportClients([FromQuery] ExportQuery query)
        {
            var bytes = await _exportService.ExportClientsAsync(query);
            var filename = $"clients_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        }

        /// <summary>
        /// Export caregiver journey snapshots filtered by journey signals as Excel.
        /// </summary>
        [HttpGet("export/caregiver-snapshots")]
        public async Task<IActionResult> ExportCaregiverSnapshots([FromQuery] CaregiverSnapshotQuery query)
        {
            // Override pagination — export should return all matching rows
            query.PageNumber = 1;
            query.PageSize = 10000;

            var bytes = await _exportService.ExportCaregiverSnapshotsAsync(query);
            var filename = $"caregiver_journey_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        }

        // ── Snapshot query (JSON) ──────────────────────────────────────────────

        /// <summary>
        /// Query caregiver journey snapshots with filtering and pagination.
        /// Snapshot data is at most 15 minutes old.
        /// </summary>
        [HttpGet("caregiver-snapshots")]
        public async Task<IActionResult> GetCaregiverSnapshots([FromQuery] CaregiverSnapshotQuery query)
        {
            var result = await _snapshotService.GetSnapshotsAsync(query);
            return Ok(new { success = true, data = result });
        }
    }
}
