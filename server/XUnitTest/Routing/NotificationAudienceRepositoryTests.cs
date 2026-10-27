using Blocks.Genesis.Entities.Iam;
using DomainService.Notification;
using FluentAssertions;
using MongoDB.Bson;
using XUnitTest.TestHelpers;

namespace XUnitTest.Routing;

/// <summary>
/// The IAM reads behind notification audiences, on isolated local MongoDB databases: who counts as active, which
/// permission rows grant roles, and which tenant DB is read.
/// </summary>
public sealed class NotificationAudienceRepositoryTests : IDisposable
{
    private readonly TenantPlacementFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private NotificationAudienceRepository Sut() => new(_fixture.Provider, _fixture.Secret);

    private static User NewUser(string id, bool active, UserLifecycleStatus status, string org = "org-a") => new()
    {
        ItemId = id,
        Active = active,
        Status = status,
        OrganizationIds = [org],
        Roles = new() { [org] = ["admin"] },
        Permissions = new() { [org] = ["reports.read"] },
    };

    private async Task<List<AudienceUser>> ActiveUsers(AudienceUserQuery query)
    {
        var users = new List<AudienceUser>();
        await foreach (var user in Sut().GetActiveUsersAsync(query))
            users.Add(user);
        return users;
    }

    [Fact]
    public async Task ActiveUsers_ExcludeInactiveSuspendedAndPending_ButKeepUsersWithoutAStatus()
    {
        TestBlocksContext.Set("dev");
        var users = _fixture.Dev.GetCollection<User>("Users");
        await users.InsertManyAsync([
            NewUser("active", true, UserLifecycleStatus.Active),
            NewUser("inactive", false, UserLifecycleStatus.Active),
            NewUser("suspended", true, UserLifecycleStatus.Suspended),
            NewUser("pending", true, UserLifecycleStatus.PendingVerification),
        ]);
        // A user written before Status existed.
        await _fixture.Dev.GetCollection<BsonDocument>("Users").InsertOneAsync(
            new BsonDocument { { "_id", "legacy" }, { "Active", true }, { "OrganizationIds", new BsonArray { "org-a" } } });

        var result = await ActiveUsers(AudienceUserQuery.Everyone);

        result.Select(u => u.ItemId).Should().BeEquivalentTo("active", "legacy");
        var active = result.Single(u => u.ItemId == "active");
        active.Roles["org-a"].Should().Equal("admin");
        active.Permissions["org-a"].Should().Equal("reports.read");
        result.Single(u => u.ItemId == "legacy").Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveUsers_DatabaseNarrowing_AppliesUserIdsAndOrganizations()
    {
        TestBlocksContext.Set("dev");
        await _fixture.Dev.GetCollection<User>("Users").InsertManyAsync([
            NewUser("a1", true, UserLifecycleStatus.Active, "org-a"),
            NewUser("a2", true, UserLifecycleStatus.Active, "org-a"),
            NewUser("b1", true, UserLifecycleStatus.Active, "org-b"),
        ]);

        var result = await ActiveUsers(new AudienceUserQuery(["a1", "b1"], ["org-a"]));

        result.Select(u => u.ItemId).Should().Equal("a1");
    }

    [Fact]
    public async Task ActiveUsers_Impersonated_ReadsTheRootDb()
    {
        TestBlocksContext.SetImpersonated("dev", "root");
        await _fixture.Main.GetCollection<User>("Users").InsertOneAsync(NewUser("root-user", true, UserLifecycleStatus.Active));
        await _fixture.Dev.GetCollection<User>("Users").InsertOneAsync(NewUser("dev-user", true, UserLifecycleStatus.Active));

        (await ActiveUsers(AudienceUserQuery.Everyone)).Select(u => u.ItemId).Should().Equal("root-user");
    }

    [Fact]
    public async Task RoleGrants_MatchTheResource_SkipArchivedAndRoleLessRows()
    {
        TestBlocksContext.Set("dev");
        await _fixture.Dev.GetCollection<Permission>("Permissions").InsertManyAsync([
            new Permission { ItemId = "p1", Name = "Read reports", Resource = "reports.read", OrganizationId = "org-a", Roles = ["admin"] },
            new Permission { ItemId = "p2", Name = "reports.read", Resource = "other", OrganizationId = "org-a", Roles = ["viewer"] },
            new Permission { ItemId = "p3", Name = "Old", Resource = "reports.read", OrganizationId = "org-b", Roles = ["admin"], IsArchived = true },
            new Permission { ItemId = "p4", Name = "No roles", Resource = "reports.read", OrganizationId = "org-c", Roles = [] },
        ]);

        var grants = await Sut().GetRoleGrantsAsync(["reports.read"]);

        grants.Should().ContainSingle().Which.Should().BeEquivalentTo(new PermissionRoleGrant("org-a", ["admin"]));
    }
}
