using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using Iam.DomainService.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainService.People
{
    public class PeopleRepository : IPeopleRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ITenants _tenants;
        private readonly IProjectRepository _projectRepository;

        private const string _userCollectionName = "Users";
        private const string _peopleCollectionName = "ProjectPeoples";

        public PeopleRepository(IDbContextProvider dbContextProvider, ITenants tenants, IProjectRepository projectRepository)
        {
            _dbContextProvider = dbContextProvider;
            _tenants = tenants;
            _projectRepository = projectRepository;
        }

        public async Task<(List<GetProjectPeople> peoples, long totalCount, long peoplesTotalCount, bool isOwner)> GetPeoplesAsync(GetPeoplesRequest request)
        {
            var peopleCollection = _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName);
            var userCollection = _dbContextProvider.GetCollection<User>(_userCollectionName);

            var projectIds = await _projectRepository.GetProjectIdsByGroupId(request.ProjectGroupId);
            FilterDefinition<ProjectPeople> projectPeopleFilter = Builders<ProjectPeople>.Filter.In("TenantId", projectIds);

            if (request.EnvironmentIds is { Count: > 0 })
            {
                projectPeopleFilter &= Builders<ProjectPeople>.Filter.In("TenantId", request.EnvironmentIds);
            }

            if (request.IsInvitationConfirmed.HasValue)
            {
                projectPeopleFilter &= Builders<ProjectPeople>.Filter.Eq(x => x.IsInvitationConfirmed, request.IsInvitationConfirmed.Value);
            }

            if (!string.IsNullOrWhiteSpace(request?.Filter))
            {
                var regex = new BsonRegularExpression(request.Filter.ToString(), "i");
                var userFilter = Builders<User>.Filter.Or(
                    Builders<User>.Filter.Regex(x => x.Email, regex),
                    Builders<User>.Filter.Regex(x => x.FirstName, regex),
                    Builders<User>.Filter.Regex(x => x.LastName, regex)
                );
                var matchingUserIds = await userCollection.Find(userFilter).Project(x => x.ItemId).ToListAsync();
                projectPeopleFilter &= Builders<ProjectPeople>.Filter.In(x => x.UserId, matchingUserIds);
            }

            var totalCount = await peopleCollection.CountDocumentsAsync(projectPeopleFilter);
            var peoplesTotalCount = await peopleCollection.Distinct(x => x.UserId, projectPeopleFilter).ToListAsync();

            var options = new FindOptions<ProjectPeople>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize
            };

            var peopleCursor = await peopleCollection.FindAsync(projectPeopleFilter, options);
            var projectPeoples = await peopleCursor.ToListAsync();

            var filter = Builders<User>.Filter.In(x => x.ItemId, projectPeoples.Select(x => x.UserId));
            var users = (await userCollection.Find(filter).ToListAsync()).ToDictionary(x => x.ItemId, x => x);

            var peoples = projectPeoples.Select(x =>
            {
                var projectPeople = new GetProjectPeople
                {
                    ItemId = x.ItemId,
                    peopleDetails = new PeopleDetails { UserId = x.UserId },
                    TenantId = x.TenantId,
                    IsInvitationSent = x.IsInvitationSent,
                    IsInvitationConfirmed = x.IsInvitationConfirmed,
                    IsCreator = x.IsCreator,
                    Enviroment = _tenants.GetTenantByID(x.TenantId)?.Environment ?? string.Empty,
                };

                var user = users.ContainsKey(x.UserId) ? users[x.UserId] : null; if (user != null)
                {
                    projectPeople.peopleDetails.Email = user.Email;
                    projectPeople.peopleDetails.FirstName = user.FirstName;
                    projectPeople.peopleDetails.LastName = user.LastName;
                    projectPeople.peopleDetails.Salutation = user.Salutation;
                    projectPeople.peopleDetails.ProfileImageUrl = user.ProfileImageUrl;
                    projectPeople.peopleDetails.AllowResendActivation = !user.Active || !user.IsVarified;
                }
                return projectPeople;
            });

            var isOwner = await IsOwner(BlocksContext.GetContext().UserId ?? "", projectIds);
            return (peoples.ToList(), totalCount, peoplesTotalCount.Count, isOwner);
        }

        private async Task<bool> IsOwner(string userId, List<string> projectKeys)
        {
            var filter = Builders<ProjectPeople>.Filter.Eq(x => x.UserId, userId) & Builders<ProjectPeople>.Filter.In(x => x.TenantId, projectKeys)
                & Builders<ProjectPeople>.Filter.Eq(x => x.IsCreator, true);

            var result = await _dbContextProvider.GetCollection<ProjectPeople>(_peopleCollectionName).CountDocumentsAsync(filter);

            return result > 0;
        }
    }
}
