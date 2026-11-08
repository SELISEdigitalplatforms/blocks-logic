using FluentAssertions;
using Mail.DomainService.Mails;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// SPEC10 6.5 / example 5 through MVC's own model validation: with nullable reference types on, a non-nullable
    /// property is an implicit [Required], and [ApiController] answers a missing one with an automatic 400 before the
    /// action runs. A test mail by templateId carries no Purpose or Language, so both must be optional on the request;
    /// a missing value on any other send is still reported by the mail validator inside the BaseMutationResponse.
    /// </summary>
    public class MailRequestModelValidationTests
    {
        private static ActionContext Validate(object model)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddControllers();
            var provider = services.BuildServiceProvider();

            var context = new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor());
            provider.GetRequiredService<IObjectModelValidator>().Validate(context, validationState: null, prefix: string.Empty, model);
            return context;
        }

        [Fact]
        public void TestMailByTemplateId_WithoutPurposeOrLanguage_PassesModelValidation()
        {
            var context = Validate(new SendMailToAny { IsTestMail = true, TemplateId = "T2", To = ["a@x.io"] });

            context.ModelState.IsValid.Should().BeTrue(
                string.Join("; ", context.ModelState.Where(e => e.Value!.Errors.Count > 0).Select(e => e.Key)));
        }
    }
}
