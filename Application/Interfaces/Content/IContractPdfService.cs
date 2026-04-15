using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IContractPdfService
    {
        /// <summary>
        /// Generates a PDF of the care service contract and returns the raw bytes.
        /// </summary>
        byte[] GeneratePdf(ContractGenerationDataDTO data);
    }
}
