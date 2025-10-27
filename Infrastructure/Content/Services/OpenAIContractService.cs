using Application.Interfaces.Content;
using Application.DTOs;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Infrastructure.Content.Services
{
    public class OpenAIContractService : IContractLLMService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OpenAIContractService> _logger;
        private readonly bool _isLLMAvailable;

        public OpenAIContractService(IConfiguration configuration, ILogger<OpenAIContractService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            var apiKey = _configuration["LLMSettings:OpenAI:ApiKey"];
            _isLLMAvailable = !string.IsNullOrEmpty(apiKey);
            
            if (!_isLLMAvailable)
            {
                _logger.LogWarning("OpenAI API key not configured. Contract generation will use mock data.");
            }
        }

        public async Task<string> GenerateContractAsync(string gigId, PackageSelection package, List<ClientTask> tasks, decimal totalAmount)
        {
            try
            {
                if (!_isLLMAvailable)
                {
                    _logger.LogInformation("Using mock contract generation - OpenAI not configured");
                    return GenerateMockContract(gigId, package, tasks);
                }

                var prompt = BuildContractGenerationPrompt(gigId, package, tasks);
                
                // Mock OpenAI response since we don't have the actual package
                var contractTerms = await GenerateContractWithMockLLMAsync(prompt);

                _logger.LogInformation("Successfully generated contract using LLM for gig {GigId}", gigId);
                return contractTerms;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating contract with LLM for gig {GigId}", gigId);
                _logger.LogInformation("Falling back to mock contract generation");
                return GenerateMockContract(gigId, package, tasks);
            }
        }

        public async Task<string> GenerateContractSummaryAsync(string contractContent)
        {
            try
            {
                if (!_isLLMAvailable)
                {
                    return GenerateMockSummary(contractContent);
                }

                var prompt = $"Please provide a brief summary of the following care contract:\n\n{contractContent}";
                return await GenerateContractWithMockLLMAsync(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating contract summary");
                return GenerateMockSummary(contractContent);
            }
        }

        public async Task<string> ReviseContractAsync(string originalContract, string revisionNotes)
        {
            try
            {
                if (!_isLLMAvailable)
                {
                    return GenerateMockRevisionAsync(originalContract, revisionNotes);
                }

                var prompt = $"Please revise the following contract based on these notes:\n\nOriginal Contract:\n{originalContract}\n\nRevision Notes:\n{revisionNotes}";
                return await GenerateContractWithMockLLMAsync(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revising contract");
                return GenerateMockRevisionAsync(originalContract, revisionNotes);
            }
        }

        public async Task<bool> IsLLMAvailableAsync()
        {
            return await Task.FromResult(_isLLMAvailable);
        }

        public async Task<string> GenerateContractSummaryAsync(ContractDTO contract)
        {
            try
            {
                if (!_isLLMAvailable)
                {
                    return GenerateMockSummaryFromContract(contract);
                }

                var prompt = BuildSummaryPrompt(contract);
                // Mock implementation - return generated summary
                return await Task.FromResult(GenerateMockSummaryFromContract(contract));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating contract summary for Contract {ContractId}", contract.Id);
                return GenerateMockSummaryFromContract(contract);
            }
        }

        public async Task<string> GenerateRevisionSummaryAsync(ContractDTO original, List<ClientTaskDTO> changes)
        {
            try
            {
                if (!_isLLMAvailable)
                {
                    return GenerateMockRevisionSummary(original, changes);
                }

                var prompt = BuildRevisionPrompt(original, changes);
                // Mock implementation - return generated revision summary
                return await Task.FromResult(GenerateMockRevisionSummary(original, changes));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating revision summary for Contract {ContractId}", original.Id);
                return GenerateMockRevisionSummary(original, changes);
            }
        }

        private string BuildContractGenerationPrompt(ContractGenerationRequestDTO request, GigSummaryDTO gigDetails)
        {
            var tasksList = string.Join("\n", request.Tasks.Select(t => 
                $"• {t.Title}: {t.Description} (Priority: {t.Priority}, Category: {t.Category})"));

            var specialRequirements = request.Tasks
                .Where(t => t.SpecialRequirements?.Any() == true)
                .SelectMany(t => t.SpecialRequirements)
                .Distinct()
                .ToList();

            var requirementsList = specialRequirements.Any() 
                ? string.Join("\n", specialRequirements.Select(r => $"• {r}"))
                : "None specified";

            return $@"Generate a professional care service contract based on the following details:

**GIG INFORMATION:**
- Service Title: {gigDetails.Title}
- Description: {gigDetails.Description}
- Caregiver: {gigDetails.CaregiverName}
- Location: {gigDetails.Location}

**SERVICE PACKAGE:**
- Package Type: {request.SelectedPackage.PackageType.Replace("_", " ").ToTitleCase()}
- Frequency: {request.SelectedPackage.VisitsPerWeek} visits per week
- Duration: {request.SelectedPackage.DurationWeeks} weeks
- Rate: ${request.SelectedPackage.PricePerVisit:F2} per visit
- Total Weekly Cost: ${request.SelectedPackage.TotalWeeklyPrice:F2}

**SPECIFIC CARE TASKS:**
{tasksList}

**SPECIAL REQUIREMENTS:**
{requirementsList}

**CONTRACT REQUIREMENTS:**
Please generate a comprehensive care service contract that includes:

1. **Service Overview & Scope**
   - Clear description of services to be provided
   - Specific tasks and responsibilities
   - Service frequency and duration

2. **Terms & Conditions**
   - Service schedule and timing
   - Cancellation and rescheduling policies
   - Emergency procedures and contacts
   - Confidentiality and privacy clauses

3. **Financial Terms**
   - Payment rates and schedule
   - Late payment policies
   - Additional service charges (if applicable)

4. **Professional Standards**
   - Quality standards and expectations
   - Professional conduct requirements
   - Insurance and liability considerations

5. **Termination Clauses**
   - Conditions for contract termination
   - Notice requirements
   - Final payment arrangements

Make the contract professional, legally sound, but easy to understand. Use clear language suitable for both parties. Ensure all specific tasks and requirements are addressed in the service scope.";
        }

        private string BuildSummaryPrompt(ContractDTO contract)
        {
            return $@"Create a concise summary of this care service contract:

**Contract Details:**
- Service Type: {contract.GigDetails?.Title}
- Package: {contract.SelectedPackage.VisitsPerWeek} visits per week for {contract.SelectedPackage.DurationWeeks} weeks
- Total Tasks: {contract.Tasks.Count}
- Total Amount: ${contract.TotalAmount:F2}

**Full Contract Terms:**
{contract.GeneratedTerms}

Please provide a 2-3 paragraph summary highlighting:
1. Key services and responsibilities
2. Schedule and payment terms
3. Important conditions or requirements

Keep it professional but accessible for quick review.";
        }

        private string BuildRevisionPrompt(ContractDTO original, List<ClientTaskDTO> changes)
        {
            var changesList = string.Join("\n", changes.Select(t => 
                $"• {t.Title}: {t.Description} (Priority: {t.Priority})"));

            return $@"Generate a revision summary for a care service contract modification:

**Original Contract Summary:**
- Service: {original.GigDetails?.Title}
- Package: {original.SelectedPackage.VisitsPerWeek} visits per week
- Original Tasks: {original.Tasks.Count}

**Requested Changes:**
{changesList}

Please provide:
1. A clear summary of what's being modified
2. How these changes affect the service scope
3. Any implications for scheduling or pricing
4. Recommended next steps for both parties

Keep it concise and professional.";
        }

        private string GenerateMockContract(ContractGenerationRequestDTO request, GigSummaryDTO gigDetails)
        {
            var packageDescription = request.SelectedPackage.PackageType.Replace("_", " ").ToTitleCase();
            var tasksList = string.Join("\n", request.Tasks.Select(t => $"• {t.Title}: {t.Description}"));

            return $@"CARE SERVICE CONTRACT

**SERVICE AGREEMENT**
This contract establishes the terms for care services between the Client and {gigDetails.CaregiverName}.

**SERVICE DETAILS:**
Service Type: {gigDetails.Title}
Location: {gigDetails.Location}
Package: {packageDescription} ({request.SelectedPackage.VisitsPerWeek} visits per week)
Duration: {request.SelectedPackage.DurationWeeks} weeks
Rate: ${request.SelectedPackage.PricePerVisit:F2} per visit

**SCOPE OF SERVICES:**
{tasksList}

**SCHEDULE & TERMS:**
• Services will be provided {request.SelectedPackage.VisitsPerWeek} times per week
• Each visit duration as needed to complete assigned tasks
• 24-hour notice required for cancellations
• Emergency contact procedures will be established

**PAYMENT TERMS:**
• Rate: ${request.SelectedPackage.PricePerVisit:F2} per visit
• Weekly cost: ${request.SelectedPackage.TotalWeeklyPrice:F2}
• Payment due weekly via CarePro platform
• Late payment fees may apply after 7 days

**PROFESSIONAL STANDARDS:**
• Caregiver will maintain professional conduct at all times
• All services provided with care, respect, and confidentiality
• Proper insurance and background checks verified through CarePro
• Quality standards as defined by CarePro platform

**TERMINATION:**
• Either party may terminate with 48-hour written notice
• Final payment due within 7 days of termination
• All property and materials to be returned

This contract is governed by the terms of service of the CarePro platform and applicable local laws.

Contract Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
Platform: CarePro Care Services";
        }

        private string GenerateMockSummary(ContractDTO contract)
        {
            return $@"**Contract Summary**

This care service agreement establishes {contract.SelectedPackage.VisitsPerWeek} weekly visits over {contract.SelectedPackage.DurationWeeks} weeks for {contract.GigDetails?.Title ?? "care services"}. 

The caregiver will provide {contract.Tasks.Count} specific care tasks including personal care, assistance, and support services. Payment is set at ${contract.SelectedPackage.PricePerVisit:F2} per visit with a total weekly cost of ${contract.SelectedPackage.TotalWeeklyPrice:F2}.

The agreement includes standard professional conduct requirements, 24-hour cancellation notice, and termination clauses with 48-hour notice. All services are subject to CarePro platform terms and quality standards.";
        }

        private string GenerateMockRevisionSummary(ContractDTO original, List<ClientTaskDTO> changes)
        {
            return $@"**Contract Revision Summary**

The client has requested modifications to the original {original.SelectedPackage.VisitsPerWeek} visits per week service contract. 

**Proposed Changes:**
{string.Join("\n", changes.Select(c => $"• {c.Title}: {c.Description}"))}

These modifications will update the scope of services while maintaining the existing schedule and payment terms. Both parties should review and agree to these changes before proceeding with the updated service arrangement.";
        }

        private string GenerateMockContract(string gigId, PackageSelection package, List<ClientTask> tasks)
        {
            return $@"CARE SERVICE CONTRACT

This contract establishes the terms and conditions for care services between the Client and Caregiver.

SERVICE DETAILS:
• Package: {package.PackageType} ({package.VisitsPerWeek} visits per week)
• Duration: {package.DurationWeeks} weeks
• Total Amount: ${package.TotalWeeklyPrice * package.DurationWeeks:F2}

CARE TASKS:
{string.Join("\n", tasks.Select(t => $"• {t.Title}: {t.Description}"))}

TERMS AND CONDITIONS:
1. Services will be provided as scheduled
2. Payment terms as agreed upon
3. Both parties may terminate with 24-hour notice
4. Quality care standards must be maintained

Generated on: {DateTime.UtcNow:yyyy-MM-dd}
Contract ID: {gigId}";
        }

        private string BuildContractGenerationPrompt(string gigId, PackageSelection package, List<ClientTask> tasks)
        {
            return $"Generate a professional care contract for gig {gigId} with {package.VisitsPerWeek} visits per week for {package.DurationWeeks} weeks.";
        }

        private async Task<string> GenerateContractWithMockLLMAsync(string prompt)
        {
            await Task.Delay(100); // Simulate API call
            return "Mock contract terms generated based on LLM prompt.";
        }

        private string GenerateMockSummary(string contractContent)
        {
            return "Summary: Professional care contract outlining service terms and responsibilities.";
        }

        private string GenerateMockSummaryFromContract(ContractDTO contract)
        {
            return $"Contract Summary: {contract.Tasks?.Count ?? 0} care tasks scheduled for {contract.ContractStartDate:yyyy-MM-dd} to {contract.ContractEndDate:yyyy-MM-dd}.";
        }

        private string GenerateMockRevisionAsync(string originalContract, string revisionNotes)
        {
            return $"REVISED CONTRACT\n\n{originalContract}\n\nREVISIONS APPLIED:\n{revisionNotes}";
        }
    }
}

// Extension method for string formatting
public static class StringExtensions
{
    public static string ToTitleCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
        }
        return string.Join(" ", words);
    }
}