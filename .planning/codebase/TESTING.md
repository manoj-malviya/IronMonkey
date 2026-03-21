# Testing Patterns

**Analysis Date:** 2026-03-19

## Test Framework

**Framework Status:**
- No test framework currently configured
- No test projects exist in solution
- No xUnit, NUnit, or MSTest packages referenced in any `.csproj` files

**Expected Framework:**
- For a .NET 10.0 project, recommended frameworks are:
  - xUnit (modern, recommended for .NET)
  - NUnit (classic .NET testing)
  - MSTest (Microsoft's testing framework)

**Assertion Library:**
- Not yet selected
- Common options: FluentAssertions, Shouldly

**Run Commands (when tests are added):**
```bash
dotnet test                    # Run all tests
dotnet test --watch           # Watch mode (if using xUnit)
dotnet test /p:CollectCoverage=true  # Coverage with OpenCover
```

## Test File Organization

**Current Status:**
- No test files or test projects exist in codebase
- Recommended location: Create `IronMonkey.ApiService.Tests`, `IronMonkey.Data.Tests` projects

**Recommended Naming:**
- Test class: `[ClassUnderTest]Tests.cs` (e.g., `CreateUserEndpointTests.cs`, `EmailServiceTests.cs`)
- Test method: `[Method]_[Scenario]_[ExpectedBehavior]` or `[Method]Should[ExpectedBehavior]When[Condition]`

**Recommended Structure:**
```
IronMonkey.ApiService.Tests/
├── Authentication/
│   └── Endpoints/
│       ├── CreateUserTests.cs
│       ├── CreateRoleTests.cs
│       └── ...
├── Common/
│   ├── Services/
│   │   └── EmailServiceTests.cs
│   ├── Auth/
│   │   └── JwtTests.cs
│   └── Extensions/
│       └── ValidationExtensionsTests.cs
├── Fixtures/
│   ├── UserFixture.cs
│   ├── RoleFixture.cs
│   └── ...
└── appsettings.Test.json

IronMonkey.Data.Tests/
├── AppDbContextTests.cs
├── Entities/
│   ├── UserEntityTests.cs
│   ├── RoleEntityTests.cs
│   └── ...
├── Fixtures/
│   └── DbContextFixture.cs
└── appsettings.Test.json
```

## Test Structure

**Recommended Test Suite Organization:**

Based on code patterns observed, tests should follow this structure:

```csharp
public class CreateUserEndpointTests
{
    private readonly AppDbContext _dbContext;
    private readonly CreateUser.RequestValidator _validator;

    public CreateUserEndpointTests()
    {
        // Setup shared test database
        _dbContext = CreateTestDbContext();
        _validator = new CreateUser.RequestValidator();
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesUserAndReturnsSuccess()
    {
        // Arrange
        var role = Role.Create(1, "Admin");
        await _dbContext.Set<Role>().AddAsync(role);
        await _dbContext.SaveChangesAsync();

        var request = new CreateUser.Request(
            TenantId: Guid.NewGuid(),
            Name: "John Doe",
            Email: "john@example.com",
            Password: "password123",
            RoleId: 1
        );

        // Act
        var result = await CreateUser.Handle(request, _dbContext);

        // Assert
        Assert.NotNull(result);
        // Verify user was persisted
    }

    [Fact]
    public async Task Handle_WithInvalidRole_ReturnsNotFound()
    {
        // Arrange
        var request = new CreateUser.Request(
            TenantId: Guid.NewGuid(),
            Name: "John Doe",
            Email: "john@example.com",
            Password: "password123",
            RoleId: 999 // Non-existent role
        );

        // Act & Assert
        var result = await CreateUser.Handle(request, _dbContext);
        Assert.IsType<NotFound>(result);
    }

    [Theory]
    [MemberData(nameof(InvalidRequestData))]
    public void RequestValidator_WithInvalidRequest_FailsValidation(CreateUser.Request request)
    {
        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
    }
}
```

**Setup/Teardown Patterns:**
- Constructor for shared fixture setup
- Consider IAsyncLifetime for async initialization
- Dispose pattern for resource cleanup (DbContext, services)
- TestInitialize/TestCleanup for test-specific setup (if using MSTest)

**Assertion Patterns:**
- Use `Assert.*` methods from framework
- For complex assertions, use FluentAssertions: `result.Should().BeOfType<Ok<Response>>()`
- Explicit assertions preferred over implicit expectations

## Mocking

**Framework:**
- No mocking framework currently configured
- Recommended: Moq (most popular for .NET)
- Alternative: NSubstitute

**Patterns (when implemented):**

For interfaces, use dependency injection directly in tests:
```csharp
[Fact]
public async Task EmailService_SendWriterInvitation_LogsErrorOnException()
{
    // Arrange
    var mockSmtpSettings = Options.Create(new SmtpSettings
    {
        Server = "smtp.test.com",
        Port = 587,
        // ... other properties
    });
    var mockLogger = new Mock<ILogger<EmailService>>();
    var service = new EmailService(mockLogger.Object, mockSmtpSettings);

    // Act & Assert
    await Assert.ThrowsAsync<Exception>(() =>
        service.SendWriterInvitationAsync("test@example.com", "Pub", null, "http://link")
    );
    mockLogger.Verify(l => l.LogError(
        It.IsAny<Exception>(),
        It.IsAny<string>(),
        It.IsAny<object[]>()
    ));
}
```

**What to Mock:**
- External dependencies (IEmailService, ILogger)
- HTTP clients (if testing API clients)
- Database context (in unit tests, use in-memory SQLite)
- Time providers (System.TimeProvider or custom ITimeProvider)

**What NOT to Mock:**
- Database context (use real in-memory database for integration tests)
- Domain entities (use factory methods like `User.Create()`)
- Validation validators (test actual validation logic)
- Request/Response DTOs (create real instances)
- Configuration objects (use real IOptions<T>)

## Fixtures and Factories

**Test Data (when implemented):**

Leverage existing entity factory methods:
```csharp
public class UserFixture
{
    public static User CreateValidUser(
        Guid? tenantId = null,
        string? name = null,
        string? email = null,
        Role? role = null)
    {
        var actualTenantId = tenantId ?? Guid.NewGuid();
        var actualName = name ?? "Test User";
        var actualEmail = email ?? "test@example.com";
        var actualRole = role ?? Role.Create(1, "TestRole");

        return User.Create(
            actualTenantId,
            actualName,
            actualEmail,
            "password123",
            actualRole
        );
    }

    public static CreateUser.Request CreateValidRequest()
    {
        return new CreateUser.Request(
            TenantId: Guid.NewGuid(),
            Name: "Test User",
            Email: "test@example.com",
            Password: "password123",
            RoleId: 1
        );
    }
}

public class RoleFixture
{
    public static Role CreateAdminRole() => Role.Create(1, "Admin");
    public static Role CreateOwnerRole() => Role.Create(301, "Owner");
    public static Role CreateTeleCallerRole() => Role.Create(302, "TeleCaller");
}
```

**Fixture Location:**
- `IronMonkey.ApiService.Tests/Fixtures/UserFixture.cs`
- `IronMonkey.ApiService.Tests/Fixtures/RoleFixture.cs`
- `IronMonkey.Data.Tests/Fixtures/DbContextFixture.cs`

**Database Fixture Pattern:**
```csharp
public class DbContextFixture : IAsyncLifetime
{
    private readonly DbContextOptions<AppDbContext> _options;
    public AppDbContext DbContext { get; private set; }

    public DbContextFixture()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    public async Task InitializeAsync()
    {
        DbContext = new AppDbContext(_options);
        await DbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await DbContext.Database.EnsureDeletedAsync();
        await DbContext.DisposeAsync();
    }
}
```

## Coverage

**Requirements:**
- Not enforced currently
- Recommended targets:
  - Unit tests: 80%+ coverage for business logic
  - Integration tests: cover critical paths
  - Endpoints: test happy path + at least 2 error cases

**View Coverage (when configured):**
```bash
dotnet test /p:CollectCoverage=true /p:CoverageFormat=json
dotnet test /p:CollectCoverage=true /p:CoverageFormat=lcov
```

**Coverage Tools:**
- OpenCover: Works with .NET Framework and .NET Core
- Coverlet: Modern coverage for .NET Core
- Both integrate with CI/CD pipelines (GitHub Actions, Azure DevOps)

## Test Types

**Unit Tests:**
- Scope: Individual methods/classes in isolation
- Approach:
  - Test entity factory methods (e.g., `User.Create()`, `Role.Create()`)
  - Test validators (e.g., `CreateUser.RequestValidator`)
  - Test service methods with mocked dependencies (e.g., `EmailService.SendWriterInvitationAsync()`)
  - No database interaction in pure unit tests
  - Use in-memory doubles for dependencies

**Integration Tests:**
- Scope: Multiple components working together
- Approach:
  - Test endpoints with real DbContext (in-memory SQLite)
  - Test repository patterns
  - Test database save/concurrency handling
  - Verify domain event creation and outbox messages
  - Use real AppDbContext with in-memory database
  - Example: `CreateUser endpoint → validation → database save → user created`

**E2E Tests:**
- Framework: Not yet implemented
- Recommended: xUnit + TestServer (Microsoft.AspNetCore.Mvc.Testing)
- Approach:
  - Full HTTP request/response testing
  - Start entire WebApplication
  - Test authentication flows
  - Test error scenarios end-to-end
  - Can use in-memory database or containerized database
  - Example: POST /users → JWT validation → DB transaction → HTTP 200 response

## Common Patterns

**Async Testing:**

For async methods like `SendWriterInvitationAsync()`:
```csharp
[Fact]
public async Task SendWriterInvitationAsync_WithValidEmail_SendsSuccessfully()
{
    // Arrange
    var emailService = new EmailService(_mockLogger, _mockOptions);

    // Act
    await emailService.SendWriterInvitationAsync(
        "user@example.com",
        "PublisherName",
        "Optional message",
        "https://invitation-link.com"
    );

    // Assert - verify no exception thrown and logging occurred
    _mockLogger.Verify(...);
}

[Fact]
public async Task SendWriterInvitationAsync_OnSmtpError_LogsAndThrows()
{
    // Arrange
    // Setup mock SMTP to fail

    // Act & Assert
    await Assert.ThrowsAsync<SmtpException>(() =>
        emailService.SendWriterInvitationAsync(...)
    );
}
```

**Error Testing:**

Test exception handling in `AppDbContext.SaveChangesAsync()`:
```csharp
[Fact]
public async Task SaveChangesAsync_OnConcurrencyException_ThrowsConcurrencyException()
{
    // Arrange
    var user = User.Create(...);
    _dbContext.Add(user);
    await _dbContext.SaveChangesAsync();

    // Simulate concurrent modification
    user.Name = "Name1";
    _dbContext.Update(user);

    // Act & Assert
    await Assert.ThrowsAsync<ConcurrencyException>(() =>
        _dbContext.SaveChangesAsync()
    );
}
```

**Validation Testing:**

Test FluentValidation validators:
```csharp
[Theory]
[InlineData("", "Invalid name")]
[InlineData("a@b@c", "Invalid email")]
[InlineData("123", "Invalid tenant ID")]
public void RequestValidator_InvalidInput_ReturnsValidationError(
    string testInput,
    string scenario)
{
    // Arrange
    var validator = new CreateUser.RequestValidator();
    var request = new CreateUser.Request(
        TenantId: Guid.Empty,
        Name: testInput,
        Email: testInput,
        Password: testInput,
        RoleId: 0
    );

    // Act
    var result = validator.Validate(request);

    // Assert
    Assert.False(result.IsValid);
    Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Name));
}
```

**Typed Result Testing:**

Test minimal API result patterns:
```csharp
[Fact]
public async Task Handle_ReturnsOkWithResponse_OnSuccess()
{
    // Arrange
    var request = new CreateRole.Request("Admin");
    var dbContext = CreateTestDbContext();

    // Act
    var result = await CreateRole.Handle(request, dbContext);

    // Assert
    Assert.IsType<Results<Ok<CreateRole.Response>, ValidationError>>(result);
    var okResult = Assert.IsType<Ok<CreateRole.Response>>(result);
    Assert.NotNull(okResult.Value);
    Assert.Equal("Admin", okResult.Value.RoleId);
}

[Fact]
public async Task Handle_ReturnsValidationError_WhenRoleExists()
{
    // Arrange
    var dbContext = CreateTestDbContext();
    var role = Role.Create(1, "Admin");
    dbContext.Set<Role>().Add(role);
    await dbContext.SaveChangesAsync();

    var request = new CreateRole.Request("Admin");

    // Act
    var result = await CreateRole.Handle(request, dbContext);

    // Assert
    Assert.IsType<ValidationError>(result);
}
```

## Test Project Setup

**Project Configuration (when created):**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Moq" Version="4.20.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.2" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.2" />
    <PackageReference Include="Testcontainers" Version="3.7.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\IronMonkey.ApiService\IronMonkey.ApiService.csproj" />
    <ProjectReference Include="..\IronMonkey.Data\IronMonkey.Data.csproj" />
  </ItemGroup>
</Project>
```

## Recommended Implementation Strategy

1. **Start with unit tests** for:
   - Entity factory methods (`User.Create()`, `Role.Create()`)
   - Validators (using real FluentValidation)
   - Utility methods

2. **Add integration tests** for:
   - Endpoint handlers (`CreateUser.Handle()`, `CreateRole.Handle()`)
   - Database concurrency handling
   - Service methods with real dependencies

3. **Add E2E tests** for:
   - Authentication flows
   - Full request/response cycles
   - Cross-cutting concerns (logging, caching)

4. **Mock only external dependencies:**
   - SMTP client (EmailService)
   - Cache (use real ICacheService)
   - HTTP clients (use HttpClientFactory)

---

*Testing analysis: 2026-03-19*
