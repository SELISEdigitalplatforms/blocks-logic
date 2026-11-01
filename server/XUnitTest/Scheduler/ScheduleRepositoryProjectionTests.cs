using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Scheduler.DomainService.Repositories;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// <see cref="ScheduleRepository.WithTimeZone"/>: a list read keeps <c>TimeZone</c> even when the tenant's stored
    /// "Schedule" schema definition predates it and the resolver's projection leaves it out.
    /// </summary>
    public class ScheduleRepositoryProjectionTests
    {
        private static string Render(ProjectionDefinition<Schedule> projection) =>
            projection.Render(new RenderArgs<Schedule>(BsonSerializer.LookupSerializer<Schedule>(), BsonSerializer.SerializerRegistry)).ToJson();

        private static ProjectionDefinition<Schedule> Doc(string json) =>
            new BsonDocumentProjectionDefinition<Schedule>(BsonDocument.Parse(json));

        [Fact]
        public void NoProjection_StaysNone()
        {
            ScheduleRepository.WithTimeZone(null).Should().BeNull();
        }

        [Fact]
        public void InclusionProjection_FromAStaleDefinition_GetsTimeZone()
        {
            var result = ScheduleRepository.WithTimeZone(Doc("{ Name: 1, CronExpression: 1, _id: 1 }"));

            Render(result!).Should().Be(BsonDocument.Parse("{ Name: 1, CronExpression: 1, _id: 1, TimeZone: 1 }").ToJson());
        }

        [Fact]
        public void InclusionProjection_ThatAlreadyHasTimeZone_IsUnchanged()
        {
            var projection = Doc("{ Name: 1, TimeZone: 1 }");

            ScheduleRepository.WithTimeZone(projection).Should().BeSameAs(projection);
        }

        [Theory]
        [InlineData("{ Payload: 0 }")]
        [InlineData("{ Payload: false, _id: 1 }")]
        public void ExclusionProjection_IsUnchanged(string json)
        {
            var projection = Doc(json);

            ScheduleRepository.WithTimeZone(projection).Should().BeSameAs(projection);
        }
    }
}
