using Application.DTOs;

namespace Application.Interfaces.Content
{
    public interface IContractTemplateService
    {
        /// <summary>
        /// Renders a standardized care service contract from negotiation data.
        /// Returns formatted HTML suitable for in-app display and PDF conversion.
        /// </summary>
        string RenderContract(ContractGenerationDataDTO data);
    }
}
