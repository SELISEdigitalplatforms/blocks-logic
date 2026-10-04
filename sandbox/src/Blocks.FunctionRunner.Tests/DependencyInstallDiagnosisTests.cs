using System.Text;
using Blocks.FunctionRunner.Builds;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// One npm failure does not mean what it says. Docker's embedded resolver is unreachable under
    /// gVisor, so every sandbox gets the host's resolv.conf bind-mounted over its own; when that
    /// file is missing or misconfigured the install dies at DNS and npm reports it as the registry
    /// being unreachable. Told apart here so the tenant is pointed at the host, not at their
    /// package.json — and so a genuine package error is never blamed on the host either.
    /// </summary>
    public class DependencyInstallDiagnosisTests
    {
        private static StringBuilder Log(string text) => new(text);

        [Theory]
        // What getaddrinfo actually surfaces as, in the two forms npm prints.
        [InlineData("npm error code EAI_AGAIN")]
        [InlineData("npm error errno ENOTFOUND")]
        [InlineData("request to https://registry.npmjs.org/dayjs failed, reason: getaddrinfo EAI_AGAIN registry.npmjs.org")]
        public void RecognisesNameResolutionFailures(string output)
        {
            DependencyInstaller.LooksLikeDnsFailure(Log(output)).Should().BeTrue();
        }

        [Theory]
        // A registry that resolves and answers is a package problem. Reporting these as a host
        // fault would send an operator to check DNS while the tenant's dependency stays broken.
        [InlineData("npm error code E404\nnpm error 404 Not Found - GET https://registry.npmjs.org/no-such-pkg")]
        [InlineData("npm error code ETARGET\nnpm error notarget No matching version found for left-pad@9.9.9")]
        [InlineData("npm error code E403")]
        [InlineData("npm error code ERESOLVE unable to resolve dependency tree")]
        [InlineData("")]
        public void LeavesGenuinePackageFailuresAlone(string output)
        {
            DependencyInstaller.LooksLikeDnsFailure(Log(output)).Should().BeFalse();
        }
    }
}
