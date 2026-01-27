using Application.Interfaces.Content;
using Application.DTOs;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace Infrastructure.Content.Services
{
    public class OpenAIContractService : IContractLLMService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OpenAIContractService> _logger;
        private readonly HttpClient _httpClient;
        private readonly bool _isLLMAvailable;
        private readonly string? _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;
        private readonly double _temperature;

        public OpenAIContractService(IConfiguration configuration, ILogger<OpenAIContractService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _apiKey = _configuration["LLMSettings:OpenAI:ApiKey"];
            _model = _configuration["LLMSettings:OpenAI:Model"] ?? "gpt-3.5-turbo";
            _maxTokens = int.TryParse(_configuration["LLMSettings:OpenAI:MaxTokens"], out var mt) ? mt : 2000;
            _temperature = double.TryParse(_configuration["LLMSettings:OpenAI:Temperature"], out var temp) ? temp : 0.7;
            
            _isLLMAvailable = !string.IsNullOrEmpty(_apiKey);

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.openai.com/v1/")
            };

            if (_isLLMAvailable)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _logger.LogInformation("OpenAI service initialized with model: {Model}", _model);
            }
            else
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

                var contractTerms = await CallOpenAIAsync(prompt);

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
                return await CallOpenAIAsync(prompt);
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
                return await CallOpenAIAsync(prompt);
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

        private async Task<string> CallOpenAIAsync(string prompt)
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = "You are a professional legal assistant specializing in care service contracts. Generate clear, comprehensive, and legally sound contract terms." },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = _maxTokens,
                    temperature = _temperature
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation("Calling OpenAI API with model: {Model}", _model);

                var response = await _httpClient.PostAsync("chat/completions", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenAI API error: {StatusCode} - {Response}", response.StatusCode, responseContent);
                    throw new HttpRequestException($"OpenAI API returned {response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                var generatedText = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                _logger.LogInformation("Successfully received response from OpenAI");
                return generatedText ?? "Contract generation failed - empty response";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenAI API");
                throw;
            }
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

        // ========================================
        // NEW: Contract Generation with Full Enriched Data
        // ========================================

        public async Task<string> GenerateContractWithScheduleAsync(ContractGenerationDataDTO data)
        {
            try
            {
                if (!_isLLMAvailable)
                {
                    _logger.LogInformation("Using mock contract generation - OpenAI not configured");
                    return GenerateMockContractWithEnrichedData(data);
                }

                var prompt = BuildEnrichedContractPrompt(data);
                var contractTerms = await CallOpenAIAsync(prompt);

                _logger.LogInformation("Successfully generated enriched contract using LLM for order {OrderId}", data.OrderId);
                return contractTerms;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating enriched contract for order {OrderId}", data.OrderId);
                return GenerateMockContractWithEnrichedData(data);
            }
        }

        private string BuildEnrichedContractPrompt(ContractGenerationDataDTO data)
        {
            var scheduleList = string.Join("\n", data.Schedule.Select(s => 
                $"   - {s.DayOfWeek}: {s.StartTime} to {s.EndTime}"));

            var tasksList = data.Tasks.Any() 
                ? string.Join("\n", data.Tasks.Select(t => $"   - {t.Title}: {t.Description}" + 
                    $" (Priority: {t.Priority})"))
                : "   - General care services as discussed and agreed upon";

            var specialReqsList = data.Tasks
                .Where(t => t.SpecialRequirements?.Any() == true)
                .SelectMany(t => t.SpecialRequirements)
                .Distinct()
                .ToList();
            
            var specialRequirementsText = specialReqsList.Any()
                ? string.Join("\n", specialReqsList.Select(r => $"   - {r}"))
                : "None";

            return $@"Generate a professional, legally-sound care service contract using the EXACT information provided below. Do NOT use placeholders - all data is real and accurate. Focus on care responsibilities and schedule - payment has already been completed.

═══════════════════════════════════════════════════════════════
CONTRACT INFORMATION
═══════════════════════════════════════════════════════════════

CONTRACT REFERENCE:
- Contract ID: {data.ContractId}
- Order Reference: {data.OrderId}
- Generated: {data.GeneratedAt:MMMM dd, yyyy 'at' HH:mm} UTC

───────────────────────────────────────────────────────────────
PARTIES TO THIS AGREEMENT
───────────────────────────────────────────────────────────────

CLIENT (Care Recipient):
- Full Name: {data.ClientFullName}
- Client ID: {data.ClientId}
{(string.IsNullOrEmpty(data.ClientPhone) ? "" : $"- Contact Phone: {data.ClientPhone}")}

CAREGIVER (Service Provider):
- Full Name: {data.CaregiverFullName}
- Caregiver ID: {data.CaregiverId}
{(string.IsNullOrEmpty(data.CaregiverQualifications) ? "" : $"- Qualifications: {data.CaregiverQualifications}")}

───────────────────────────────────────────────────────────────
SERVICE DETAILS
───────────────────────────────────────────────────────────────

SERVICE TYPE: {data.GigTitle}
{(string.IsNullOrEmpty(data.GigDescription) ? "" : $"Description: {data.GigDescription}")}
{(string.IsNullOrEmpty(data.GigCategory) ? "" : $"Category: {data.GigCategory}")}

SERVICE PACKAGE:
- Package Type: {data.Package.PackageType?.Replace("_", " ") ?? "Standard"}
- Visits Per Week: {data.Package.VisitsPerWeek}
- Hours Per Visit: 4-6 hours
- Contract Duration: {data.Package.DurationWeeks} weeks

CONTRACT PERIOD:
- Start Date: {data.ContractStartDate:dddd, MMMM dd, yyyy}
- End Date: {data.ContractEndDate:dddd, MMMM dd, yyyy}

───────────────────────────────────────────────────────────────
AGREED WEEKLY SCHEDULE
───────────────────────────────────────────────────────────────

{scheduleList}

───────────────────────────────────────────────────────────────
SERVICE LOCATION
───────────────────────────────────────────────────────────────

Address: {data.ServiceAddress}
{(string.IsNullOrEmpty(data.City) ? "" : $"City: {data.City}")}
{(string.IsNullOrEmpty(data.State) ? "" : $"State: {data.State}")}

{(string.IsNullOrEmpty(data.AccessInstructions) ? "" : $@"ACCESS INSTRUCTIONS:
{data.AccessInstructions}")}

───────────────────────────────────────────────────────────────
CARE RESPONSIBILITIES
───────────────────────────────────────────────────────────────

PRIMARY CARE TASKS:
{tasksList}

{(string.IsNullOrEmpty(data.SpecialClientRequirements) ? "" : $@"CLIENT'S SPECIAL REQUIREMENTS:
{data.SpecialClientRequirements}")}

TASK-SPECIFIC REQUIREMENTS:
{specialRequirementsText}

{(string.IsNullOrEmpty(data.CaregiverNotes) ? "" : $@"CAREGIVER'S NOTES:
{data.CaregiverNotes}")}

───────────────────────────────────────────────────────────────
INSTRUCTIONS FOR CONTRACT GENERATION
───────────────────────────────────────────────────────────────

Generate a comprehensive care service contract that:
1. Uses ALL the exact names, dates, and details provided above
2. DOES NOT include payment terms (payment already completed via CarePro)
3. Focuses on care responsibilities, schedule, and service quality
4. Includes clear sections for: Parties, Services, Schedule, Location, Responsibilities, Safety, Cancellation/Rescheduling, Confidentiality, Termination
5. Is professional but warm in tone - this is a care relationship
6. Mentions that this contract is facilitated through the CarePro platform
7. Includes the exact contract dates and schedule times
8. References the specific care tasks listed above

Make the contract thorough but readable, suitable for both parties to understand their commitments.";
        }

        private string GenerateMockContractWithEnrichedData(ContractGenerationDataDTO data)
        {
            var scheduleText = string.Join("\n", data.Schedule.Select(s => 
                $"      {s.DayOfWeek}: {s.StartTime} - {s.EndTime}"));

            var tasksList = data.Tasks.Any()
                ? string.Join("\n", data.Tasks.Select(t => $"      • {t.Title}: {t.Description}"))
                : "      • General care services as discussed and agreed upon";

            return $@"
╔═══════════════════════════════════════════════════════════════════════════════╗
║                         CARE SERVICE CONTRACT                                  ║
║                           CarePro Platform                                     ║
╚═══════════════════════════════════════════════════════════════════════════════╝

Contract Reference: {data.ContractId}
Order Reference: {data.OrderId}
Generated: {data.GeneratedAt:MMMM dd, yyyy 'at' HH:mm} UTC

════════════════════════════════════════════════════════════════════════════════
SECTION 1: PARTIES TO THIS AGREEMENT
════════════════════════════════════════════════════════════════════════════════

This Care Service Contract (""Agreement"") is entered into between:

CLIENT (Care Recipient):
   Name: {data.ClientFullName}
   Client ID: {data.ClientId}
   {(string.IsNullOrEmpty(data.ClientPhone) ? "" : $"Phone: {data.ClientPhone}")}

AND

CAREGIVER (Service Provider):
   Name: {data.CaregiverFullName}
   Caregiver ID: {data.CaregiverId}
   {(string.IsNullOrEmpty(data.CaregiverQualifications) ? "" : $"Qualifications: {data.CaregiverQualifications}")}

This Agreement is facilitated through the CarePro care services platform.

════════════════════════════════════════════════════════════════════════════════
SECTION 2: SERVICE DESCRIPTION
════════════════════════════════════════════════════════════════════════════════

2.1 SERVICE TYPE
   {data.GigTitle}
   {(string.IsNullOrEmpty(data.GigDescription) ? "" : $"   {data.GigDescription}")}
   {(string.IsNullOrEmpty(data.GigCategory) ? "" : $"   Category: {data.GigCategory}")}

2.2 SERVICE PACKAGE
   • Package Type: {data.Package.PackageType?.Replace("_", " ") ?? "Standard"}
   • Visits Per Week: {data.Package.VisitsPerWeek}
   • Duration Per Visit: 4-6 hours
   • Contract Duration: {data.Package.DurationWeeks} weeks

════════════════════════════════════════════════════════════════════════════════
SECTION 3: CONTRACT PERIOD
════════════════════════════════════════════════════════════════════════════════

This Agreement shall be effective from:

   Start Date: {data.ContractStartDate:dddd, MMMM dd, yyyy}
   End Date:   {data.ContractEndDate:dddd, MMMM dd, yyyy}

The contract may be renewed by mutual agreement of both parties through the 
CarePro platform.

════════════════════════════════════════════════════════════════════════════════
SECTION 4: AGREED SCHEDULE
════════════════════════════════════════════════════════════════════════════════

The following weekly schedule has been mutually agreed upon by both parties:

{scheduleText}

4.1 SCHEDULE MODIFICATIONS
   • Either party may request schedule changes with 48-hour advance notice
   • Permanent schedule changes require mutual agreement
   • Emergency schedule changes should be communicated immediately
   • All schedule changes must be documented through the CarePro platform

════════════════════════════════════════════════════════════════════════════════
SECTION 5: SERVICE LOCATION
════════════════════════════════════════════════════════════════════════════════

All services will be provided at:

   Address: {data.ServiceAddress}
   {(string.IsNullOrEmpty(data.City) ? "" : $"City: {data.City}")}
   {(string.IsNullOrEmpty(data.State) ? "" : $"State: {data.State}")}

{(string.IsNullOrEmpty(data.AccessInstructions) ? "" : $@"ACCESS INSTRUCTIONS:
   {data.AccessInstructions}")}

════════════════════════════════════════════════════════════════════════════════
SECTION 6: CARE RESPONSIBILITIES
════════════════════════════════════════════════════════════════════════════════

6.1 PRIMARY CARE TASKS
   The Caregiver agrees to provide the following services:

{tasksList}

{(string.IsNullOrEmpty(data.SpecialClientRequirements) ? "" : $@"6.2 SPECIAL CLIENT REQUIREMENTS
   {data.SpecialClientRequirements}")}

{(string.IsNullOrEmpty(data.CaregiverNotes) ? "" : $@"6.3 CAREGIVER NOTES
   {data.CaregiverNotes}")}

6.4 SERVICE STANDARDS
   • All services shall be provided with professionalism, compassion, and care
   • The Caregiver shall follow established care protocols and best practices
   • Regular communication with the Client and/or family members as appropriate
   • Maintain accurate records of care provided through the CarePro platform

════════════════════════════════════════════════════════════════════════════════
SECTION 7: CANCELLATION & RESCHEDULING
════════════════════════════════════════════════════════════════════════════════

7.1 CANCELLATION BY CLIENT
   • 24-hour notice required for scheduled visit cancellations
   • Cancellations with less than 24-hour notice may be subject to review
   • Emergency cancellations will be evaluated on a case-by-case basis

7.2 CANCELLATION BY CAREGIVER
   • 24-hour notice required for cancellations when possible
   • CarePro may arrange replacement care for urgent situations
   • Repeated cancellations may affect caregiver standing on the platform

7.3 NO-SHOW POLICY
   • Client no-show without notice: Visit considered completed
   • Caregiver no-show: Replacement service or account credit provided

════════════════════════════════════════════════════════════════════════════════
SECTION 8: SAFETY & CONFIDENTIALITY
════════════════════════════════════════════════════════════════════════════════

8.1 SAFETY PROTOCOLS
   • The Caregiver has been verified through CarePro's background check process
   • Emergency protocols shall be established for medical situations
   • All incidents must be reported through the CarePro platform
   • Both parties agree to maintain a safe care environment

8.2 CONFIDENTIALITY
   • All personal, medical, and financial information shall be kept strictly 
     confidential
   • No information shall be shared with third parties without explicit consent
   • Both parties agree to comply with applicable privacy laws and regulations
   • Confidentiality obligations survive termination of this Agreement

════════════════════════════════════════════════════════════════════════════════
SECTION 9: TERMINATION
════════════════════════════════════════════════════════════════════════════════

9.1 TERMINATION BY EITHER PARTY
   • Either party may terminate this Agreement with 48-hour written notice
   • Notice must be provided through the CarePro platform
   • Outstanding obligations shall be settled within 7 days of termination

9.2 IMMEDIATE TERMINATION (FOR CAUSE)
   Either party may terminate immediately in cases of:
   • Breach of safety protocols
   • Unprofessional conduct or harassment
   • Violation of confidentiality
   • Failure to provide agreed services
   • Any conduct that endangers either party

════════════════════════════════════════════════════════════════════════════════
SECTION 10: DISPUTE RESOLUTION
════════════════════════════════════════════════════════════════════════════════

Any disputes arising from this Agreement shall be:
   1. First addressed through CarePro's support and mediation services
   2. Escalated to formal dispute resolution if mediation is unsuccessful
   3. Subject to the laws of the jurisdiction where services are provided

════════════════════════════════════════════════════════════════════════════════
SECTION 11: AGREEMENT
════════════════════════════════════════════════════════════════════════════════

By approving this contract through the CarePro platform, both parties:
   • Acknowledge reading and understanding all terms and conditions
   • Agree to fulfill their respective obligations as outlined
   • Commit to maintaining a professional and respectful care relationship
   • Authorize CarePro to facilitate this Agreement

────────────────────────────────────────────────────────────────────────────────

CLIENT: {data.ClientFullName}
Status: Pending Approval

CAREGIVER: {data.CaregiverFullName}
Status: Contract Submitted

────────────────────────────────────────────────────────────────────────────────

Contract ID: {data.ContractId}
Platform: CarePro Care Services
Generated: {data.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC

╔═══════════════════════════════════════════════════════════════════════════════╗
║                           END OF CONTRACT                                      ║
╚═══════════════════════════════════════════════════════════════════════════════╝";
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