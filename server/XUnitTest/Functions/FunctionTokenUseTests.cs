using FluentAssertions;
using Functions.DomainService.Models;
using Functions.DomainService.Utils;
using Xunit;

namespace XUnitTest.Functions
{
    /// <summary>
    /// A version gets a delegation grant (and the runner an IAM exchange per call) only when its
    /// code can read <c>ctx.blocks.accessToken</c>. Over-eager on purpose: a false "uses it" only
    /// costs speed, a false "does not" would hand the code <c>undefined</c>.
    /// </summary>
    public class FunctionTokenUseTests
    {
        private static FunctionSource Source(string code) => new() { IndexJs = code };

        [Theory]
        [InlineData("export default async (ctx) => ctx.blocks.accessToken;")]
        [InlineData("export default async ({ blocks }) => blocks.accessToken;")]
        [InlineData("export default async (ctx) => { const { blocks } = ctx; return blocks; };")]
        [InlineData("export default async (ctx) => ctx[\"blocks\"];")]
        [InlineData("const t = ctx.blocks?.['accessToken'];")]
        [InlineData("// uses accessToken later")]
        [InlineData("const { accessToken } = ctx.blocks;")]
        public void Code_that_can_reach_the_token_may_read_it(string code) =>
            FunctionTokenUse.MayRead(Source(code)).Should().BeTrue();

        [Theory]
        [InlineData("export default async function handler(ctx) { return { ok: true }; }")]
        [InlineData("import { MongoClient } from 'mongodb'; export default async (ctx) => ctx.input;")]
        [InlineData("const client = createBlocksClient({ baseUrl }); // no token")]
        [InlineData("const myblocks = 1; const blocksize = 2;")]
        public void Code_that_never_names_it_does_not(string code) =>
            FunctionTokenUse.MayRead(Source(code)).Should().BeFalse();

        [Fact]
        public void Unknown_or_empty_source_is_treated_as_reading_it()
        {
            FunctionTokenUse.MayRead(null).Should().BeTrue();
            FunctionTokenUse.MayRead(Source(string.Empty)).Should().BeTrue();
        }
    }
}
