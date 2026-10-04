using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace XUnitTest.Functions
{
    /// <summary>
    /// <see cref="FunctionEntity.IsDirty"/> is <c>[BsonIgnore]</c> on purpose: it is derived by
    /// comparing the working source hash with the active version's
    /// (<c>FunctionService.IsDirty</c>), never stored. Naming it in an update definition asks the
    /// driver for a field that has no BSON mapping, and it answers
    /// <c>"Expression not supported: f.IsDirty"</c> — a 500 on Save and on Deploy, which is
    /// exactly what shipped. Rendering the update is what catches that at build time.
    /// </summary>
    public class FunctionEntityMappingTests
    {
        private static string Render(UpdateDefinition<FunctionEntity> update)
        {
            var serializer = BsonSerializer.SerializerRegistry.GetSerializer<FunctionEntity>();
            return update.Render(new RenderArgs<FunctionEntity>(serializer, BsonSerializer.SerializerRegistry))
                .ToString();
        }

        [Fact]
        public void IsDirty_IsNotPersisted()
        {
            var map = BsonClassMap.LookupClassMap(typeof(FunctionEntity));

            map.AllMemberMaps.Should().NotContain(
                m => m.MemberName == nameof(FunctionEntity.IsDirty),
                "IsDirty is derived, so it must stay out of the document");
        }

        [Fact]
        public void SettingIsDirty_CannotBeRendered_WhichIsWhyTheRepositoryMustNotTry()
        {
            var update = Builders<FunctionEntity>.Update.Set(f => f.IsDirty, true);

            var act = () => Render(update);

            act.Should().Throw<Exception>()
                .WithMessage("*IsDirty*", "this is the 500 the Save path used to return");
        }

        [Fact]
        public void TheFieldsSaveActuallyWrites_AllRender()
        {
            // The shape of SaveSourceAsync's update, minus IsDirty.
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.SourceHash, "hash")
                .Set(f => f.Limits, new FunctionLimits())
                .Set(f => f.Retry, new RetryPolicy())
                .Set(f => f.Trigger, new TriggerConfig())
                .Set(f => f.OutputActions, [])
                .Set(f => f.Variables, []);

            var act = () => Render(update);

            act.Should().NotThrow();
        }

        [Fact]
        public void TheFieldsDeployActuallyWrites_AllRender()
        {
            var update = Builders<FunctionEntity>.Update
                .Set(f => f.ActiveVersionId, "v1")
                .Set(f => f.LastVersionNumber, 1)
                .Set(f => f.Status, FunctionStatus.Live)
                .Set(f => f.LastDeployedAt, DateTime.UtcNow);

            var act = () => Render(update);

            act.Should().NotThrow();
        }
    }
}
