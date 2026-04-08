using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DomainService.Worker
{
    public class RefreshTokenWorkerService : IConsumer<RefreshTokenEvent>
    {
        private readonly ILogger<RefreshTokenWorkerService> _logger;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly IUserRepository _userRepository;

        public RefreshTokenWorkerService(ILogger<RefreshTokenWorkerService> logger, IAuthenticationRepository oAuthRepository, IUserRepository userRepository)
        {
            _logger = logger;
            _oAuthRepository = oAuthRepository;
            _userRepository = userRepository;
        }
        public async Task Consume(RefreshTokenEvent context)
        {
            _logger.LogInformation("RefreshTokenWorkerService start");

            await Task.WhenAll(ProcessSession(context), ProcessUserTimelineEvent(context), UpdateUserByLoginInfoAsync(context));
        }

        public async Task UpdateUserByLoginInfoAsync(RefreshTokenEvent refreshTokenEvent)
        {
            _logger.LogInformation("User Mutation event -- initiate to update login info");

            var user = await _userRepository.GetUserByIdAsync(refreshTokenEvent.UserId);

            if (user == null)
            {
                _logger.LogError("User not found by this user id: {Id}", refreshTokenEvent.UserId);
                return;
            }

            if (user.LogInCount == 0)
            {
                user.FirstLoggedInTime = DateTime.Now;
            }

            user.LogInCount += 1;
            user.LastLoggedInTime = DateTime.Now;
            user.LastLoggedInDeviceInfo = JsonSerializer.Serialize(refreshTokenEvent.DeviceInformation);

            await _userRepository.UpdateUserAsync(user);

            _logger.LogInformation("User Mutation event -- end of the update login info");
        }

        public async Task<bool> ProcessSession(RefreshTokenEvent context)
        {
            var session = new Session
            {
                RefreshToken = context.RefreshToken,
                TenantId = context.TenantId,
                IssuedUtc = context.IssuedUtc,
                ExpiresUtc = context.ExpiresUtc,
                IpAddresses = context.IpAddresses,
                UserId = context.UserId,
                DeviceInformation = context.DeviceInformation,
                CreateDate = DateTime.Now,
                UpdateDate = DateTime.Now,
                IsActive = true
            };

            return await _oAuthRepository.InsertSessionAsync(session);
        }

        public async Task<bool> ProcessUserTimelineEvent(RefreshTokenEvent context)
        {
            var userAuthenticationTimeline = new UserAuthenticationTimeline
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = context?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = context?.UserId,
                DeviceInformation = context?.DeviceInformation,
                IpAddresses = context?.IpAddresses ?? string.Empty,
                Event = "issued_refresh_token",
                ActionBy = "RefreshTokenWorkerService"
            };

            return await _oAuthRepository.InsertUserAuthenticationTimelineAsync(userAuthenticationTimeline);
        }
    }
}
