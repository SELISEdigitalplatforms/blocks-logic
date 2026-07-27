using Cloud.LmtService.Models.Logs;
using Cloud.LmtService.Models.Trace;
using Cloud.LmtService.Repositories.Logs;
using Cloud.LmtService.Repositories.Trace;
using Cloud.LmtService.Services.Logs;
using Cloud.LmtService.Services.Trace;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Cloud
{
    public class TraceServiceTests
    {
        private readonly Mock<ITraceRepository> _repo = new();
        private readonly TraceService _service;

        public TraceServiceTests()
        {
            _service = new TraceService(Mock.Of<ILogger<TraceService>>(), _repo.Object);
        }

        [Fact]
        public async Task GetTrace_MissingTraceId_ReturnsError()
        {
            var result = await _service.GetTraceAsync(new GetTraceRequest { TraceId = "" });
            result.Errors.Should().ContainKey("Error");
        }

        [Fact]
        public async Task GetTrace_WithTraceId_ReturnsData()
        {
            _repo.Setup(r => r.GetTraces(It.IsAny<GetTraceRequest>()))
                .ReturnsAsync(new List<SingleTraceProjection>().AsQueryable());

            var result = await _service.GetTraceAsync(new GetTraceRequest { TraceId = "t1" });

            result.Data.Should().NotBeNull();
        }

        [Fact]
        public async Task GetTraces_ReturnsDataAndTotal()
        {
            _repo.Setup(r => r.GetTraces(It.IsAny<GetTracesRequest>()))
                .ReturnsAsync((new List<TraceProjection>().AsQueryable(), 7L));

            var result = await _service.GetTracesAsync(new GetTracesRequest());

            result.TotalCount.Should().Be(7);
        }

        [Fact]
        public async Task GetOperationalAnalytics_ReturnsRepoResult()
        {
            var payload = new { x = 1 };
            _repo.Setup(r => r.GetOperationalAnalytics(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(payload);

            var result = await _service.GetOperationalAnalytics(new GetApiAnalyticsRequest { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow, ServiceName = "svc" });

            result.Should().BeSameAs(payload);
        }

        [Fact]
        public async Task GetServiceAnalytics_ReturnsRepoResult()
        {
            var payload = new { y = 2 };
            _repo.Setup(r => r.GetServiceAnalytics(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>()))
                .ReturnsAsync(payload);

            var result = await _service.GetServiceAnalytics(new GetHttpStatusAnalyticsRequest { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow });

            result.Should().BeSameAs(payload);
        }
    }

    public class LogServiceTests
    {
        private readonly Mock<ILogRepository> _repo = new();
        private readonly LogService _service;

        public LogServiceTests()
        {
            _service = new LogService(Mock.Of<ILogger<LogService>>(), _repo.Object);
        }

        [Fact]
        public async Task GetLiveLogs_MissingName_ReturnsError()
        {
            var result = await _service.GetLiveLogsAsync(new LiveLogRequest { Name = "", LastDate = DateTime.MinValue });
            result.Errors.Should().ContainKey("Error");
        }

        [Fact]
        public async Task GetLiveLogs_Valid_ReturnsData()
        {
            _repo.Setup(r => r.GetLogs(It.IsAny<LiveLogRequest>()))
                .ReturnsAsync(new List<LogProjection>().AsQueryable());

            var result = await _service.GetLiveLogsAsync(new LiveLogRequest { Name = "svc", LastDate = DateTime.UtcNow });

            result.Data.Should().NotBeNull();
        }

        [Fact]
        public async Task GetLogs_MissingServiceName_ReturnsError()
        {
            var result = await _service.GetLogsAsync(new GetLogsRequest { ServiceName = "" });
            result.Errors.Should().ContainKey("Error");
        }

        [Fact]
        public async Task GetLogs_Valid_ReturnsDataAndTotal()
        {
            _repo.Setup(r => r.GetLogs(It.IsAny<GetLogsRequest>()))
                .ReturnsAsync((new List<LogProjection>().AsQueryable(), 5L));

            var result = await _service.GetLogsAsync(new GetLogsRequest { ServiceName = "svc" });

            result.TotalCount.Should().Be(5);
        }

        [Fact]
        public async Task GetLogsByDate_MissingServiceName_ReturnsError()
        {
            var result = await _service.GetLogsByDateAsync(new LogsByDateRequest { ServiceName = "" });
            result.Errors.Should().ContainKey("Error");
        }

        [Fact]
        public async Task GetLogsByDate_Valid_ReturnsDataAndTotal()
        {
            _repo.Setup(r => r.GetLogs(It.IsAny<LogsByDateRequest>()))
                .ReturnsAsync((new List<LogProjection>().AsQueryable(), 3L));

            var result = await _service.GetLogsByDateAsync(new LogsByDateRequest { ServiceName = "svc" });

            result.TotalCount.Should().Be(3);
        }
    }
}
