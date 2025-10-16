# Contributing to CarePro Backend

Thank you for your interest in contributing to the CarePro backend API! We welcome contributions from the community and are grateful for your help in making this project better.

## 🚀 Quick Start

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/yourusername/carepro-backend.git
   cd carepro-backend
   ```
3. **Set up the development environment** (see [Development Setup](#development-setup))
4. **Create a new branch** for your feature:
   ```bash
   git checkout -b feature/your-feature-name
   ```
5. **Make your changes** and test them
6. **Submit a pull request**

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Pull Request Process](#pull-request-process)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Documentation](#documentation)
- [Issue Reporting](#issue-reporting)
- [Community](#community)

## 📜 Code of Conduct

This project adheres to a [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## 🛠️ Development Setup

### Prerequisites

- **.NET 8 SDK** or later
- **MongoDB** 6.0 or later
- **Visual Studio 2022** or **VS Code** with C# extension
- **Git**
- **Docker** (optional, for containerized development)

### Environment Setup

1. **Clone the repository**:
   ```bash
   git clone https://github.com/codesquares/carepro-backend.git
   cd carepro-backend
   ```

2. **Set up configuration**:
   ```bash
   cp appsettings.Development.json.example appsettings.Development.json
   # Edit the configuration with your local settings
   ```

3. **Install dependencies**:
   ```bash
   dotnet restore
   ```

4. **Run the application**:
   ```bash
   dotnet run --project CarePro-Api
   ```

5. **Run tests**:
   ```bash
   dotnet test
   ```

### Docker Development (Alternative)

```bash
# Build and run with Docker Compose
docker-compose up --build

# Run tests in container
docker-compose exec api dotnet test
```

## 🤝 How to Contribute

### Types of Contributions

We welcome several types of contributions:

- **🐛 Bug fixes**: Fix issues and improve stability
- **✨ New features**: Add new functionality
- **📚 Documentation**: Improve or add documentation
- **🧪 Tests**: Add or improve test coverage
- **♻️ Refactoring**: Improve code quality and structure
- **⚡ Performance**: Optimize performance
- **🔒 Security**: Enhance security measures

### Getting Started

1. **Check existing issues** to see if your contribution is already being worked on
2. **Create an issue** if one doesn't exist for your contribution
3. **Discuss your approach** in the issue before starting work
4. **Fork and create a branch** for your work
5. **Make your changes** following our guidelines
6. **Test your changes** thoroughly
7. **Submit a pull request**

## 🔄 Pull Request Process

### Before Submitting

- [ ] **Code follows** our coding standards
- [ ] **Tests pass** locally
- [ ] **Documentation** is updated if needed
- [ ] **No merge conflicts** with the main branch
- [ ] **Commit messages** follow our format
- [ ] **Security considerations** addressed

### PR Checklist

1. **Create a descriptive title**:
   ```
   [TYPE] Brief description of changes
   
   Examples:
   [FEATURE] Add smart contract generation API
   [BUGFIX] Fix memory leak in contract processing
   [DOCS] Update API documentation for webhooks
   ```

2. **Fill out the PR template** completely
3. **Link related issues** using keywords (fixes #123, closes #456)
4. **Add appropriate labels**
5. **Request review** from maintainers

### Review Process

1. **Automated checks** must pass (CI/CD pipeline)
2. **Code review** by at least one maintainer
3. **Security review** for security-related changes
4. **Testing** in staging environment
5. **Merge** after approval

## 📝 Coding Standards

### C# Style Guidelines

We follow the [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/inside-a-program/coding-conventions) with these additions:

#### Naming Conventions

```csharp
// Classes: PascalCase
public class ContractService

// Methods: PascalCase
public async Task<Contract> CreateContractAsync()

// Properties: PascalCase
public string ContractId { get; set; }

// Private fields: camelCase with underscore
private readonly IContractRepository _contractRepository;

// Constants: PascalCase
public const string DefaultCurrency = "USD";

// Local variables: camelCase
var contractDetails = await GetContractAsync();
```

#### Code Organization

```csharp
// File organization
using System;                    // System namespaces first
using System.Threading.Tasks;
                                // Blank line
using Microsoft.Extensions;     // Microsoft namespaces
                                // Blank line
using CarePro.Domain;          // Project namespaces

namespace CarePro.Application   // Namespace matches folder structure
{
    /// <summary>
    /// Service for managing contracts
    /// </summary>
    public class ContractService : IContractService
    {
        // Private fields first
        private readonly IContractRepository _repository;
        
        // Constructor
        public ContractService(IContractRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }
        
        // Public methods
        public async Task<Contract> CreateAsync(CreateContractRequest request)
        {
            // Implementation
        }
        
        // Private methods last
        private void ValidateContract(Contract contract)
        {
            // Implementation
        }
    }
}
```

#### Error Handling

```csharp
// Use specific exceptions
throw new ArgumentNullException(nameof(request));
throw new InvalidOperationException("Contract already exists");

// Use Result pattern for business logic
public async Task<Result<Contract>> CreateContractAsync(CreateContractRequest request)
{
    try
    {
        // Business logic
        return Result<Contract>.Success(contract);
    }
    catch (ValidationException ex)
    {
        return Result<Contract>.Failure(ex.Message);
    }
}
```

### Architecture Patterns

We follow **Clean Architecture** principles:

- **Domain Layer**: Entities, value objects, domain services
- **Application Layer**: Use cases, DTOs, interfaces
- **Infrastructure Layer**: Repositories, external services
- **API Layer**: Controllers, middleware, filters

### Database Conventions

```csharp
// Entity configuration
public class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToCollection("contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
    }
}

// Repository pattern
public interface IContractRepository
{
    Task<Contract?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Contract>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<string> CreateAsync(Contract contract, CancellationToken cancellationToken = default);
    Task UpdateAsync(Contract contract, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
```

## 🧪 Testing Guidelines

### Test Structure

We follow the **AAA pattern** (Arrange, Act, Assert):

```csharp
[Test]
public async Task CreateContractAsync_WithValidRequest_ShouldReturnContract()
{
    // Arrange
    var request = new CreateContractRequest
    {
        Title = "Test Contract",
        Description = "Test Description"
    };
    var expectedContract = new Contract(request.Title, request.Description);
    
    _mockRepository.Setup(x => x.CreateAsync(It.IsAny<Contract>(), default))
               .ReturnsAsync(expectedContract.Id);
    
    // Act
    var result = await _contractService.CreateContractAsync(request);
    
    // Assert
    Assert.That(result.IsSuccess, Is.True);
    Assert.That(result.Value.Title, Is.EqualTo(request.Title));
}
```

### Test Categories

1. **Unit Tests**: Test individual components in isolation
2. **Integration Tests**: Test component interactions
3. **End-to-End Tests**: Test complete user scenarios
4. **Performance Tests**: Test performance characteristics

### Test Requirements

- **Minimum 80% code coverage** for new code
- **All public methods** should have tests
- **Edge cases** should be covered
- **Error scenarios** should be tested
- **Async methods** should be tested properly

### Mocking Guidelines

```csharp
// Use Moq for mocking
private readonly Mock<IContractRepository> _mockRepository;
private readonly Mock<ILogger<ContractService>> _mockLogger;
private readonly ContractService _contractService;

[SetUp]
public void Setup()
{
    _mockRepository = new Mock<IContractRepository>();
    _mockLogger = new Mock<ILogger<ContractService>>();
    _contractService = new ContractService(_mockRepository.Object, _mockLogger.Object);
}
```

## 📚 Documentation

### Code Documentation

```csharp
/// <summary>
/// Creates a new contract with the specified details
/// </summary>
/// <param name="request">The contract creation request containing contract details</param>
/// <param name="cancellationToken">Cancellation token for the operation</param>
/// <returns>A task representing the asynchronous operation with the created contract</returns>
/// <exception cref="ArgumentNullException">Thrown when request is null</exception>
/// <exception cref="ValidationException">Thrown when request validation fails</exception>
public async Task<Contract> CreateContractAsync(
    CreateContractRequest request, 
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### API Documentation

We use **Swagger/OpenAPI** for API documentation:

```csharp
/// <summary>
/// Creates a new contract
/// </summary>
/// <param name="request">Contract creation request</param>
/// <returns>Created contract</returns>
/// <response code="201">Contract created successfully</response>
/// <response code="400">Invalid request data</response>
/// <response code="401">Unauthorized</response>
[HttpPost]
[ProducesResponseType(typeof(ContractResponse), StatusCodes.Status201Created)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
public async Task<ActionResult<ContractResponse>> CreateContract([FromBody] CreateContractRequest request)
{
    // Implementation
}
```

### README Updates

When adding new features, update the README.md with:
- New API endpoints
- Configuration options
- Setup instructions
- Usage examples

## 🐛 Issue Reporting

### Bug Reports

Use the **bug report template** and include:
- Clear description of the issue
- Steps to reproduce
- Expected vs. actual behavior
- Environment details
- Logs and error messages

### Feature Requests

Use the **feature request template** and include:
- Clear description of the feature
- Use cases and motivation
- Acceptance criteria
- Implementation suggestions

### Security Issues

**Never report security issues publicly**. Use our [Security Policy](SECURITY.md) for reporting security vulnerabilities.

## 💬 Community

### Communication Channels

- **GitHub Issues**: For bug reports and feature requests
- **GitHub Discussions**: For questions and general discussion
- **Email**: development@carepro.com for direct communication

### Getting Help

1. **Check the documentation** first
2. **Search existing issues** for similar problems
3. **Ask in discussions** for general questions
4. **Create an issue** for bugs or feature requests

### Code of Conduct

We are committed to providing a welcoming and inclusive environment. Please read our [Code of Conduct](CODE_OF_CONDUCT.md) before participating.

## 🎯 Development Workflow

### Branch Strategy

- **main**: Production-ready code
- **staging**: Integration testing
- **feature/***: New features
- **bugfix/***: Bug fixes
- **hotfix/***: Critical production fixes

### Commit Message Format

```
[TYPE] Brief description (50 chars or less)

Detailed explanation of what changed and why.
Include any breaking changes or migration notes.

Closes #123
```

Types:
- **FEATURE**: New feature
- **BUGFIX**: Bug fix
- **DOCS**: Documentation changes
- **STYLE**: Code style changes
- **REFACTOR**: Code refactoring
- **TEST**: Test changes
- **CHORE**: Maintenance tasks

### Release Process

1. **Feature freeze** on staging branch
2. **Testing** and bug fixes
3. **Version bump** and changelog update
4. **Merge** to main branch
5. **Tag** the release
6. **Deploy** to production

## 🏆 Recognition

We appreciate all contributions and recognize contributors in:
- **CONTRIBUTORS.md** file
- **Release notes**
- **Project documentation**

Thank you for contributing to CarePro! 🎉