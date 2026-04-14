using Blocks.Genesis;
using FluentAssertions;
using IamDriver;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Moq;

namespace XUnitTest.Driver
{
    public class IamDriverServiceTests
    {
        private readonly Mock<IUserManagementMutationService> _userManagementServiceMock;
        private readonly IamDriverService _driverService;

        public IamDriverServiceTests()
        {
            _userManagementServiceMock = new Mock<IUserManagementMutationService>();
            _driverService = new IamDriverService(_userManagementServiceMock.Object);
        }

        private static CreateUserRequest CreateValidUserRequest()
        {
            return new CreateUserRequest
            {
                Email = "test@example.com",
                UserName = "testuser",
                Password = "Test@Pass123",
                FirstName = "John",
                LastName = "Doe",
                UserCreationType = UserCreationType.Portal,
                UserPassType = UserPassType.Password
            };
        }

        private static CreateUserViaSsoRequest CreateValidSsoRequest()
        {
            return new CreateUserViaSsoRequest
            {
                Email = "sso@example.com",
                FirstName = "SSO",
                LastName = "User",
                Platform = "Web",
                MailPurpose = "WelcomeEmail",
                SendWelcomeMail = true,
                ProjectKey = "test-project",
                Memberships = new List<OrganizationMembership>()
            };
        }

        #region CreateUser Tests

        [Fact]
        public async Task CreateUser_WithValidRequest_DelegatesToMutationService()
        {
            // Arrange
            var request = CreateValidUserRequest();
            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "user-123"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUser(request);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeSameAs(expectedResponse);
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("user-123");
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(request), Times.Once);
        }

        [Fact]
        public async Task CreateUser_WithValidationErrors_ReturnsErrorResponse()
        {
            // Arrange
            var request = CreateValidUserRequest();
            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Email", "Email already exists" },
                    { "Password", "Password too weak" }
                }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUser(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().HaveCount(2);
            result.Errors.Should().ContainKey("Email");
            result.Errors.Should().ContainKey("Password");
        }

        [Theory]
        [InlineData("user1@test.com", "user-id-1")]
        [InlineData("user2@test.com", "user-id-2")]
        [InlineData("admin@test.com", "user-id-3")]
        public async Task CreateUser_WithDifferentEmails_CallsServiceCorrectly(string email, string expectedId)
        {
            // Arrange
            var request = CreateValidUserRequest();
            request.Email = email;

            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = expectedId
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUser(request);

            // Assert
            result.ItemId.Should().Be(expectedId);
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(
                It.Is<CreateUserRequest>(r => r.Email == email)), Times.Once);
        }

        [Fact]
        public async Task CreateUser_CallsServiceOnce()
        {
            // Arrange
            var request = CreateValidUserRequest();
            var expectedResponse = new BaseMutationResponse { IsSuccess = true };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            await _driverService.CreateUser(request);

            // Assert
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(request), Times.Once);
        }

        #endregion

        #region CreateUserViaSso Tests

        [Fact]
        public async Task CreateUserViaSso_WithValidRequest_DelegatesToMutationService()
        {
            // Arrange
            var request = CreateValidSsoRequest();
            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "sso-user-456"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserViaSsoAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUserViaSso(request);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeSameAs(expectedResponse);
            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().Be("sso-user-456");
            _userManagementServiceMock.Verify(x => x.CreateUserViaSsoAsync(request), Times.Once);
        }

        [Fact]
        public async Task CreateUserViaSso_WithErrors_ReturnsErrorResponse()
        {
            // Arrange
            var request = CreateValidSsoRequest();
            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Email", "Invalid SSO email" }
                }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserViaSsoAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUserViaSso(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Email");
        }

        [Theory]
        [InlineData("sso1@test.com", true)]
        [InlineData("sso2@test.com", false)]
        public async Task CreateUserViaSso_WithDifferentSettings_CallsServiceCorrectly(string email, bool sendWelcomeMail)
        {
            // Arrange
            var request = CreateValidSsoRequest();
            request.Email = email;
            request.SendWelcomeMail = sendWelcomeMail;

            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = "sso-user"
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserViaSsoAsync(It.IsAny<CreateUserViaSsoRequest>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUserViaSso(request);

            // Assert
            result.IsSuccess.Should().BeTrue();
            _userManagementServiceMock.Verify(x => x.CreateUserViaSsoAsync(
                It.Is<CreateUserViaSsoRequest>(r => 
                    r.Email == email && 
                    r.SendWelcomeMail == sendWelcomeMail)), Times.Once);
        }

        [Fact]
        public async Task CreateUserViaSso_CallsServiceOnce()
        {
            // Arrange
            var request = CreateValidSsoRequest();
            var expectedResponse = new BaseMutationResponse { IsSuccess = true };

            _userManagementServiceMock
                .Setup(x => x.CreateUserViaSsoAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            await _driverService.CreateUserViaSso(request);

            // Assert
            _userManagementServiceMock.Verify(x => x.CreateUserViaSsoAsync(request), Times.Once);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task BothMethods_DelegateCorrectly()
        {
            // Arrange
            var userRequest = CreateValidUserRequest();
            var ssoRequest = CreateValidSsoRequest();

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(userRequest))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "user-1" });
            _userManagementServiceMock
                .Setup(x => x.CreateUserViaSsoAsync(ssoRequest))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "user-2" });

            // Act
            var result1 = await _driverService.CreateUser(userRequest);
            var result2 = await _driverService.CreateUserViaSso(ssoRequest);

            // Assert - Both methods delegate correctly
            result1.IsSuccess.Should().BeTrue();
            result2.IsSuccess.Should().BeTrue();
            _userManagementServiceMock.Verify(x => x.CreateUserAsync(userRequest), Times.Once);
            _userManagementServiceMock.Verify(x => x.CreateUserViaSsoAsync(ssoRequest), Times.Once);
        }

        [Theory]
        [InlineData(true, "user-success")]
        [InlineData(false, null)]
        public async Task CreateUser_WithDifferentResponseStates_ReturnsSameResponse(bool isSuccess, string itemId)
        {
            // Arrange
            var request = CreateValidUserRequest();
            var expectedResponse = new BaseMutationResponse
            {
                IsSuccess = isSuccess,
                ItemId = itemId,
                Errors = isSuccess ? null : new Dictionary<string, string> { { "Error", "Failed" } }
            };

            _userManagementServiceMock
                .Setup(x => x.CreateUserAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.CreateUser(request);

            // Assert
            result.Should().BeSameAs(expectedResponse);
            result.IsSuccess.Should().Be(isSuccess);
        }

        #endregion
    }
}
