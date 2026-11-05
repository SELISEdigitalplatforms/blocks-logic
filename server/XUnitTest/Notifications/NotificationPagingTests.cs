using DomainService.Notification;
using FluentAssertions;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// #402 C17: the <c>GetNotifications</c> paging rule. Page 0 or greater, PageSize 1–100, and Page × PageSize
    /// within an int (the Mongo skip). It used to be unchecked, so large values threw an OverflowException (a 500).
    /// </summary>
    public class NotificationPagingTests
    {
        private const string PageNegative = "Page must be 0 or greater.";
        private const string PageSizeRange = "PageSize must be between 1 and 100.";
        private const string PageTooLarge = "Page is too large for this PageSize.";

        private static Dictionary<string, string>? Validate(int page, int pageSize) =>
            NotificationPaging.Validate(new GetNotificationsRequest { Page = page, PageSize = pageSize });

        [Theory]
        [InlineData(0, 10)]   // H5
        [InlineData(2, 10)]   // H6
        [InlineData(0, 100)]  // H7
        [InlineData(0, 1)]
        [InlineData(21474836, 100)] // 2,147,483,600: the largest page within int for PageSize 100
        [InlineData(int.MaxValue, 1)]
        public void ValidPaging_HasNoErrors(int page, int pageSize)
        {
            Validate(page, pageSize).Should().BeNull();
        }

        [Fact]
        public void H8_TheDefaults_AreValid()
        {
            var request = new GetNotificationsRequest();

            request.Page.Should().Be(0);
            request.PageSize.Should().Be(10);
            NotificationPaging.Validate(request).Should().BeNull();
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void C1_NegativePage(int page)
        {
            Validate(page, 10).Should().BeEquivalentTo(new Dictionary<string, string> { ["Page"] = PageNegative });
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(101)]
        [InlineData(int.MaxValue)]
        public void C2_PageSizeOutsideOneToHundred(int pageSize)
        {
            Validate(0, pageSize).Should().BeEquivalentTo(new Dictionary<string, string> { ["PageSize"] = PageSizeRange });
        }

        [Theory]
        [InlineData(int.MaxValue, 10)]   // spec example 4
        [InlineData(21474837, 100)]      // 2,147,483,700: one page past int for PageSize 100
        [InlineData(1073741824, 2)]      // exactly int.MaxValue + 1
        public void C3_PageTimesPageSizeBeyondInt_IsTooLarge_WithoutOverflow(int page, int pageSize)
        {
            var act = () => Validate(page, pageSize);

            act.Should().NotThrow();
            act().Should().BeEquivalentTo(new Dictionary<string, string> { ["Page"] = PageTooLarge });
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(-1, 101)]
        public void C4_BothInvalid_ReportsBothKeys(int page, int pageSize)
        {
            Validate(page, pageSize).Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["Page"] = PageNegative,
                ["PageSize"] = PageSizeRange,
            });
        }

        [Fact]
        public void LargePage_WithAnInvalidPageSize_ReportsOnlyThePageSize()
        {
            // "Too large" is judged only against a valid PageSize (spec §6.2).
            Validate(int.MaxValue, 0).Should().BeEquivalentTo(new Dictionary<string, string> { ["PageSize"] = PageSizeRange });
        }
    }
}
