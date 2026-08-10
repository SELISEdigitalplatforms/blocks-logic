using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace DomainService.People
{
    public class PeopleService : IPeopleService
    {
        private readonly IPeopleRepository _peopleRepository;
        private readonly IProjectRepository _projectRepository;

        public PeopleService(IPeopleRepository peopleRepository, IProjectRepository projectRepository)
        {
            _peopleRepository = peopleRepository;
            _projectRepository = projectRepository;
        }

        public async Task<GetPeoplesResponse> GetPeoplesAsync(GetPeoplesRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ProjectGroupId))
            {
                return new GetPeoplesResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "empty_group_id", "Project groupId is required" }
                    }
                };
            }

            var sharedEnvironments = await _projectRepository.GetProjectPeoplesAsync(request.ProjectGroupId);
            if (sharedEnvironments == null || sharedEnvironments.Count == 0)
            {
                return new GetPeoplesResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                        {
                            { "no_projects", "No projects are shared with user" }
                        }
                };
            }

            var result = await _peopleRepository.GetPeoplesAsync(request);

            var peoples = result.peoples
                .GroupBy(p => p.peopleDetails)
                .Select(g => new GetPeoples
                {
                    peopleDetails = g.Key,
                    SharedEnviroments = g.Select(p => new SharedEnviroment
                    {
                        ItemId = p.ItemId,
                        TenantId = p.TenantId,
                        IsInvitationSent = p.IsInvitationSent,
                        IsInvitationConfirmed = p.IsInvitationConfirmed,
                        IsCreator = p.IsCreator,
                        Enviroment = p.Enviroment
                    }).ToList()
                })
                .ToList();

            return new GetPeoplesResponse
            {
                IsSuccess = true,
                Peoples = peoples,
                IsOwner = result.isOwner,
                TotalCount = result.totalCount,
                PeoplesTotalCount = result.peoplesTotalCount
            };
        }
    }
}