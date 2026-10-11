using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Models;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;

namespace Functions.DomainService.Validation
{
    /// <summary>Shared building blocks the individual validators below compose.</summary>
    internal static class FunctionValidationRules
    {
        // A conservative environment-variable-style name: this becomes a JavaScript property
        // access (ctx.env.KEY), so it has to be a valid identifier, not just a valid Mongo key.
        internal static readonly Regex VariableKeyPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        internal static readonly string[] AllowedHttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

        /// <summary>2 MB, matching the runner's own build-time source cap (spec, mirrored in HANDOFF.md).</summary>
        internal const int MaxSourceBytes = 2 * 1024 * 1024;

        /// <summary>
        /// Name and description bounds, kept identical to the create dialog's zod schema
        /// (function-create-dialog.tsx). Two places state them, so they are named here rather
        /// than written inline in each validator.
        /// </summary>
        internal const int MinNameLength = 2;

        /// <inheritdoc cref="MinNameLength"/>
        internal const int MaxNameLength = 64;

        /// <inheritdoc cref="MinNameLength"/>
        internal const int MaxDescriptionLength = 200;

        /// <summary>
        /// What the description was allowed to be before the bound above matched the dialog.
        /// Update keeps it: the detail page has no description field, so a rename sends the
        /// stored value back unchanged — and holding that pass-through to the new, shorter bound
        /// would make a function created through the API with a long description impossible to
        /// rename. Create is where the bound has to bite.
        /// </summary>
        internal const int LegacyMaxDescriptionLength = 1000;

        internal static bool IsValidAbsoluteHttpUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }

    public class CreateFunctionRequestValidator : AbstractValidator<CreateFunctionRequestDto>
    {
        public CreateFunctionRequestValidator()
        {
            // Name is a label, not an address — a function is addressed by its ItemId — so
            // nothing here has to be unique. The bounds are the ones the create dialog already
            // enforces in zod; they were 1..200 / 0..1000 here, so anything not typed into that
            // dialog (the API directly, a script, a workflow import) was held to a different
            // contract from everything the product shows.
            RuleFor(x => x.Name)
                .NotEmpty()
                // ApplyConditionTo.CurrentValidator, or the condition would also suppress the
                // NotEmpty above it — a chained When in FluentValidation covers the whole chain.
                .Length(FunctionValidationRules.MinNameLength, FunctionValidationRules.MaxNameLength)
                    .When(x => !string.IsNullOrEmpty(x.Name), ApplyConditionTo.CurrentValidator);
            RuleFor(x => x.Description).MaximumLength(FunctionValidationRules.MaxDescriptionLength);

            RuleFor(x => x.Template)
                .Must(FunctionStarterTemplates.IsKnown)
                .WithMessage(_ =>
                    $"Template must be one of: {string.Join(", ", FunctionStarterTemplates.Names)}.");
        }
    }

    public class UpdateFunctionRequestValidator : AbstractValidator<UpdateFunctionRequestDto>
    {
        public UpdateFunctionRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();
            RuleFor(x => x.Name)
                .NotEmpty()
                // ApplyConditionTo.CurrentValidator, or the condition would also suppress the
                // NotEmpty above it — a chained When in FluentValidation covers the whole chain.
                .Length(FunctionValidationRules.MinNameLength, FunctionValidationRules.MaxNameLength)
                    .When(x => !string.IsNullOrEmpty(x.Name), ApplyConditionTo.CurrentValidator);
            RuleFor(x => x.Description)
                .MaximumLength(FunctionValidationRules.LegacyMaxDescriptionLength);
        }
    }

    public class SaveFunctionRequestValidator : AbstractValidator<SaveFunctionRequestDto>
    {
        public SaveFunctionRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();

            RuleFor(x => x.IndexJs)
                .NotEmpty().WithMessage("The function must have an entry point.")
                .Must(js => System.Text.Encoding.UTF8.GetByteCount(js) <= FunctionValidationRules.MaxSourceBytes)
                .WithMessage($"The entry point exceeds the {FunctionValidationRules.MaxSourceBytes} byte source limit.");

            RuleFor(x => x.PackageJson)
                .NotEmpty()
                .Must(BeValidModuleManifest)
                .WithMessage("""package.json must be valid JSON and declare "type": "module".""");

            RuleForEach(x => x.Variables).SetValidator(new VariableBindingValidator());
            RuleForEach(x => x.OutputActions).SetValidator(new OutputActionValidator());

            RuleFor(x => x.Limits).SetValidator(new FunctionLimitsValidator());
            RuleFor(x => x.Retry).SetValidator(new RetryPolicyValidator());
            RuleFor(x => x.Trigger).SetValidator(new TriggerConfigValidator());
        }

        private static bool BeValidModuleManifest(string packageJson)
        {
            try
            {
                using var document = JsonDocument.Parse(packageJson);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "module";
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Checks the one limit a tenant may choose: the HTTP rate limit (FN-19).
    /// <para>
    /// <c>requestsPerMinute</c> must be 1–<see cref="FunctionLimits.Ceiling.MaxRequestsPerMinute"/>
    /// or null (= the default). <c>requestsPerDay</c> must be null: there is no per-day quota.
    /// Before this, <see cref="FunctionLimits.Clamp"/> silently turned both into "default", so a
    /// caller asking for a limit got none and was never told. Clamp still runs on every read, so a
    /// stored document that carries an old per-day value keeps loading and running.
    /// </para>
    /// <para>
    /// cpu / memory / timeout / concurrency are still not checked: they are fixed, Clamp discards
    /// them, and refusing a leftover field would fail a save that changes nothing.
    /// </para>
    /// </summary>
    public class FunctionLimitsValidator : AbstractValidator<FunctionLimits>
    {
        public FunctionLimitsValidator()
        {
            RuleFor(x => x.RequestsPerDay)
                .Null()
                .WithMessage("requestsPerDay is not supported; use requestsPerMinute");

            RuleFor(x => x.RequestsPerMinute)
                .InclusiveBetween(1, FunctionLimits.Ceiling.MaxRequestsPerMinute)
                .When(x => x.RequestsPerMinute.HasValue)
                .WithMessage($"requestsPerMinute must be between 1 and {FunctionLimits.Ceiling.MaxRequestsPerMinute}");
        }
    }

    /// <summary>Empty for the same reason as <see cref="FunctionLimitsValidator"/>.</summary>
    public class RetryPolicyValidator : AbstractValidator<RetryPolicy>
    {
    }

    public class TriggerConfigValidator : AbstractValidator<TriggerConfig>
    {
        public TriggerConfigValidator()
        {
            RuleForEach(x => x.Roles).NotEmpty().MaximumLength(200);
            RuleForEach(x => x.Permissions).NotEmpty().MaximumLength(200);

            // The five verbs the public route is registered for; anything else could never be
            // reached, so storing it would only make the editor show a verb that 405s.
            RuleForEach(x => x.HttpMethods)
                .Must(m => m is not null && FunctionValidationRules.AllowedHttpMethods.Contains(m.Trim(), StringComparer.OrdinalIgnoreCase))
                .WithMessage($"HTTP methods must be among: {string.Join(", ", FunctionValidationRules.AllowedHttpMethods)}.");
            RuleFor(x => x.HttpMethods)
                .Must(list => list is null
                    || list.Where(m => m is not null).Select(m => m.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        == list.Count(m => m is not null))
                .WithMessage("Each HTTP method may be listed only once.");

            RuleFor(x => x.ResponseMode)
                .Must(m => m is null or TriggerConfig.ResponseModes.Async or TriggerConfig.ResponseModes.Sync)
                .WithMessage($"Response mode must be \"{TriggerConfig.ResponseModes.Async}\" or \"{TriggerConfig.ResponseModes.Sync}\".");
        }
    }

    public class VariableBindingValidator : AbstractValidator<VariableBinding>
    {
        public VariableBindingValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty()
                .Matches(FunctionValidationRules.VariableKeyPattern)
                .WithMessage("Variable names must be a valid identifier (letters, digits, underscore; not starting with a digit).")
                .MaximumLength(100);

            RuleFor(x => x.Value).MaximumLength(4096);
        }
    }

    public class OutputActionValidator : AbstractValidator<OutputAction>
    {
        public OutputActionValidator()
        {
            When(x => x.Kind == Enums.OutputActionKind.ExternalHttp && x.Enabled, () =>
            {
                RuleFor(x => x.Url)
                    .Must(FunctionValidationRules.IsValidAbsoluteHttpUrl)
                    .WithMessage("The output action URL must be an absolute http:// or https:// address.");

                // SSRF, save-time half: a literal private / loopback / link-local / metadata
                // address (in any spelling Uri canonicalises), localhost-style and single-label
                // hosts. DNS is not asked here — the processor resolves and vets again at send,
                // which is the half that catches a public name pointing inward.
                RuleFor(x => x.Url)
                    .Must(url => !Utils.FunctionOutboundGuard.IsDisallowedUrl(url, out _))
                    .When(x => FunctionValidationRules.IsValidAbsoluteHttpUrl(x.Url))
                    .WithMessage(x =>
                    {
                        Utils.FunctionOutboundGuard.IsDisallowedUrl(x.Url, out var reason);
                        return reason;
                    });

                RuleFor(x => x.Method)
                    .Must(m => FunctionValidationRules.AllowedHttpMethods.Contains(m, StringComparer.OrdinalIgnoreCase))
                    .WithMessage($"Method must be one of: {string.Join(", ", FunctionValidationRules.AllowedHttpMethods)}.");
            });

            RuleFor(x => x.TimeoutSeconds).InclusiveBetween(1, 60);
        }
    }

    public class TestFunctionRequestValidator : AbstractValidator<TestFunctionRequestDto>
    {
        public TestFunctionRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();
            RuleFor(x => x.InputJson)
                .Must(json => System.Text.Encoding.UTF8.GetByteCount(json!) <= FunctionLimits.Ceiling.InputBytes)
                .When(x => x.InputJson is not null)
                .WithMessage($"Input exceeds the {FunctionLimits.Ceiling.InputBytes} byte limit.");
        }
    }

    public class DeployFunctionRequestValidator : AbstractValidator<DeployFunctionRequestDto>
    {
        public DeployFunctionRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();
            RuleFor(x => x.Note).MaximumLength(500);
        }
    }

}
