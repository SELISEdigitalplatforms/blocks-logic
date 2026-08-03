using DomainService.Sequence;
using FluentAssertions;
using Moq;

namespace XUnitTest.Sequence
{
    public class SequenceServiceTests
    {
        private readonly Mock<ISequenceRepository> _repo = new();
        private readonly SequenceService _service;

        public SequenceServiceTests()
        {
            _service = new SequenceService(_repo.Object);
        }

        [Fact]
        public async Task GetNextSequenceNumber_ReturnsNumber()
        {
            _repo.Setup(r => r.GetNextSequenceNumberAsync("orders")).ReturnsAsync(42);

            var result = await _service.GetNextSequenceNumberAsync(new SequenceNumberQuery { Context = "orders" });

            result.IsSuccess.Should().BeTrue();
            result.Context.Should().Be("orders");
            result.CurrentNumber.Should().Be(42);
        }

        [Fact]
        public async Task GetNextHexSequenceNumber_ReturnsHexFormatted()
        {
            _repo.Setup(r => r.GetNextHexSequenceNumberAsync("orders")).ReturnsAsync(255);

            var result = await _service.GetNextHexSequenceNumberAsync(new SequenceNumberHexQuery { Context = "orders" });

            result.IsSuccess.Should().BeTrue();
            result.CurrentNumber.Should().Be("0000000FF");
        }

        [Fact]
        public async Task ResetSequenceNumber_ReturnsSuccess()
        {
            var result = await _service.ResetSequenceNumberAsync(new ResetSequenceNumberRequest { Context = "orders", Value = 10 });

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.ResetSequenceNumberAsync("orders", 10), Times.Once);
        }
    }
}
