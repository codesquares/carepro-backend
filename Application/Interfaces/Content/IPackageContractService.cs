using Application.DTOs;
using System.Threading.Tasks;

namespace Application.Interfaces.Content
{
    /// <summary>
    /// Phase 6 — auto-generates a care agreement (Package terms + CarePro standard terms)
    /// when a <see cref="Domain.Entities.PackageRequest"/> reaches "confirmed". Reuses
    /// <c>IContractTemplateService</c> and <c>IContractPdfService</c> unchanged; involves
    /// no LLM, no negotiation, no caregiver input.
    /// </summary>
    public interface IPackageContractService
    {
        /// <summary>
        /// Idempotently generates (or returns the existing) contract for a confirmed
        /// package request. Safe to call from the Phase 4 acceptance path or later.
        /// </summary>
        Task<PackageContractDTO> GenerateForConfirmedRequestAsync(string packageRequestId);

        Task<PackageContractDTO?> GetByPackageRequestAsync(string packageRequestId);

        /// <summary>Renders the contract PDF via the reused ContractPdfService.</summary>
        Task<byte[]> GeneratePdfAsync(string contractId);
    }
}
