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

    public class FunctionLimitsValidator : AbstractValidator<FunctionLimits>
    {
        public FunctionLimitsValidator()
        {
            RuleFor(x => x.CpuMillicores).InclusiveBetween(1, FunctionLimits.Ceiling.CpuMillicores);
            RuleFor(x => x.MemoryMb).InclusiveBetween(FunctionLimits.Ceiling.MinMemoryMb, FunctionLimits.Ceiling.MemoryMb);
            RuleFor(x => x.TimeoutSeconds).InclusiveBetween(1, FunctionLimits.Ceiling.TimeoutSeconds);
            RuleFor(x => x.Concurrency).InclusiveBetween(
                FunctionLimits.Ceiling.MinConcurrency, FunctionLimits.Ceiling.MaxConcurrency);

            // Rejected outright rather than silently clamped: unlike the ceilings (where
            // clamping just means "you got less than you asked for"), a caller who typed a
            // negative rate limit almost certainly meant something else and deserves to be told.
            RuleFor(x => x.RequestsPerMinute).GreaterThan(0).When(x => x.RequestsPerMinute.HasValue);
            RuleFor(x => x.RequestsPerDay).GreaterThan(0).When(x => x.RequestsPerDay.HasValue);
        }
    }

    public class RetryPolicyValidator : AbstractValidator<RetryPolicy>
    {
        public RetryPolicyValidator()
        {
            RuleFor(x => x.Attempts).InclusiveBetween(1, 5);
            RuleFor(x => x.InitialDelaySeconds).InclusiveBetween(1, 300);
            RuleFor(x => x.MaxDelaySeconds).InclusiveBetween(1, 900);
            RuleFor(x => x)
                .Must(x => x.MaxDelaySeconds >= x.InitialDelaySeconds)
                .WithMessage("The maximum retry delay cannot be shorter than the initial delay.");
        }
    }

    public class TriggerConfigValidator : AbstractValidator<TriggerConfig>
    {
        public TriggerConfigValidator()
        {
            RuleForEach(x => x.Roles).NotEmpty().MaximumLength(200);
            RuleForEach(x => x.Permissions).NotEmpty().MaximumLength(200);
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

    public class RollbackFunctionRequestValidator : AbstractValidator<RollbackFunctionRequestDto>
    {
        public RollbackFunctionRequestValidator()
        {
            RuleFor(x => x.FunctionId).NotEmpty();
            RuleFor(x => x.VersionNumber).GreaterThan(0);
        }
    }
}
