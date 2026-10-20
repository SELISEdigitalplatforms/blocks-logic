using FluentValidation;
using Functions.DomainService.Dtos;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Repositories;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Logic;

namespace Functions.DomainService.Services
{
    /// <summary>
    /// Export / import of one function per file, identical across environments (2026-10-18): the same id everywhere,
    /// only the tenant differs. Functions-only; Proxy has its own (2026-10-07: they share no code).
    /// </summary>
    public interface IFunctionTransferService
    {
        Task<FunctionExportFile> ExportAsync(
            string tenantId, string functionId, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);

        /// <summary>Validates the file and compares it with this environment. Writes nothing.</summary>
        Task<FunctionImportPreviewDto> PreviewAsync(
            string tenantId, FunctionImportPreviewRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>Re-runs every check, then inserts, updates (conditionally) or does nothing; then builds and deploys.</summary>
        Task<FunctionImportResultDto> ApplyAsync(
            string tenantId, FunctionImportApplyRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="IFunctionTransferService"/>
    /// <remarks>
    /// <para><b>Secrets.</b> The file carries names, never ids or values. Export maps each <c>{{secret.&lt;id&gt;}}</c> to
    /// <c>{{secretName.&lt;name&gt;}}</c> through this environment's catalog. Import binds a name that matches exactly
    /// one secret back to <c>{{secret.&lt;id&gt;}}</c>, and stores any other as <c>{{secretName.&lt;name&gt;}}</c>, which the
    /// runner and the output-action processor resolve by exact name at run start — so adding the secret is the only
    /// step left. The catalog being down never fails an import: every name then simply stays unbound.</para>
    /// <para><b>Reads.</b> Through the repository's policy-checked reads (2026-10-12). A document the caller cannot see
    /// previews as <c>new</c>; the insert then hits its <c>_id</c> and answers 409 — it is never overwritten.</para>
    /// <para><b>Logs and audit.</b> Ids, statuses, field names and hashes only — never source, variable values or headers.</para>
    /// </remarks>
    public sealed class FunctionTransferService : IFunctionTransferService
    {
        /// <summary>Distinct secret names one imported function may use.</summary>
        internal const int MaxSecretNames = 50;

        private const string Resolution_TakeIncoming = "takeIncoming";
        private const string Resolution_KeepMine = "keepMine";
        private const string Resolution_Merge = "merge";

        private readonly IFunctionRepository _repository;
        private readonly IFunctionSecretCatalog _secrets;
        private readonly IFunctionAuditService _audit;
        private readonly IFunctionDeploymentService _deployments;
        private readonly IValidator<SaveFunctionRequestDto> _saveValidator;
        private readonly IValidator<UpdateFunctionRequestDto> _detailsValidator;
        private readonly ILogger<FunctionTransferService> _logger;

        public FunctionTransferService(
            IFunctionRepository repository,
            IFunctionSecretCatalog secrets,
            IFunctionAuditService audit,
            IFunctionDeploymentService deployments,
            IValidator<SaveFunctionRequestDto> saveValidator,
            IValidator<UpdateFunctionRequestDto> detailsValidator,
            ILogger<FunctionTransferService> logger)
        {
            _repository = repository;
            _secrets = secrets;
            _audit = audit;
            _deployments = deployments;
            _saveValidator = saveValidator;
            _detailsValidator = detailsValidator;
            _logger = logger;
        }

        // ================================================================ export ====

        public async Task<FunctionExportFile> ExportAsync(
            string tenantId, string functionId, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            var function = await _repository.GetByIdAsync(tenantId, functionId, cancellationToken)
                ?? throw new FunctionNotFoundException($"function '{functionId}' was not found");

            IReadOnlyDictionary<string, string> namesById;
            try
            {
                namesById = await _secrets.NamesByIdAsync(
                    FunctionSecretRefs.IdsIn(function.Variables, function.OutputActions), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A file with the ids left in would not import anywhere, and one with guessed names would bind to the
                // wrong secret: no file is better than either.
                _logger.LogWarning("Export of function {FunctionId}: the secret catalog could not be read ({Type})",
                    functionId, ex.GetType().Name);
                throw new FunctionUnavailableException(
                    "the secret catalog could not be read, so secret references cannot be written as names; try again");
            }

            var (body, unresolved) = ToExportBody(function, namesById);
            var file = new FunctionExportFile
            {
                Format = FunctionTransferCodec.Format,
                SchemaVersion = FunctionTransferCodec.SchemaVersion,
                ExportedAt = DateTime.UtcNow,
                Function = body,
                SecretNames = SecretNamesOf(body),
                UnresolvedSecretRefs = unresolved,
                ContentHash = FunctionTransferCodec.Hash(body),
            };

            await _audit.RecordAsync(
                tenantId, function.ItemId, FunctionsConstants.AuditActions.Exported, actorId, actorEmail,
                new { file.ContentHash }, cancellationToken);
            return file;
        }

        /// <summary>
        /// The file form of a stored function: only the portable fields, and every <c>{{secret.&lt;id&gt;}}</c> written as
        /// the secret's name here (or <c>{{secretUnresolved}}</c> when the id has no secret). Also used to show "mine" in
        /// a preview and to compare, so the stored side and the file side are always in the same form.
        /// </summary>
        internal static (FunctionExportBody Body, List<string> Unresolved) ToExportBody(
            FunctionEntity function, IReadOnlyDictionary<string, string> namesById)
        {
            var unresolved = new List<string>();
            foreach (var (where, value) in FunctionSecretRefs.Locations(function.Variables, function.OutputActions))
            {
                if (FunctionSecretRefs.IdsIn(value).Any(id => !namesById.ContainsKey(id))) unresolved.Add(where);
            }

            var (variables, actions) = FunctionSecretRefs.Rewrite(function.Variables, function.OutputActions, value =>
                FunctionSecretRefs.ReplaceIds(value, id =>
                    namesById.TryGetValue(id, out var name) && FunctionSecretRefs.IsValidName(name)
                        ? FunctionSecretRefs.NameToken(name)
                        : FunctionSecretRefs.UnresolvedMarker));

            var source = function.Source ?? new FunctionSource();
            var body = new FunctionExportBody
            {
                ItemId = function.ItemId,
                Name = function.Name,
                Description = function.Description,
                Source = new FunctionExportSource
                {
                    IndexJs = source.IndexJs,
                    PackageJson = source.PackageJson,
                    LockJson = source.LockJson,
                    AllowInstallScripts = source.AllowInstallScripts,
                },
                RequestsPerMinute = (function.Limits ?? new FunctionLimits()).Clamp().RequestsPerMinute,
                Trigger = function.Trigger ?? new TriggerConfig(),
                OutputActions = actions,
                Variables = variables,
            };
            return (body, unresolved);
        }

        private static List<FunctionExportSecretName> SecretNamesOf(FunctionExportBody body)
        {
            var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (where, value) in FunctionSecretRefs.Locations(body.Variables, body.OutputActions))
            {
                foreach (var name in FunctionSecretRefs.NamesIn(value).Distinct(StringComparer.Ordinal))
                {
                    if (!byName.TryGetValue(name, out var list)) byName[name] = list = [];
                    list.Add(where);
                }
            }
            return byName.Select(kv => new FunctionExportSecretName { Name = kv.Key, UsedBy = kv.Value }).ToList();
        }

        // =============================================================== preview ====

        public async Task<FunctionImportPreviewDto> PreviewAsync(
            string tenantId, FunctionImportPreviewRequestDto request, CancellationToken cancellationToken = default)
        {
            var file = await ValidateFileAsync(request.File);
            var incoming = FunctionTransferCodec.Normalized(file.Function!);

            var preview = new FunctionImportPreviewDto
            {
                FunctionId = incoming.ItemId,
                Name = incoming.Name,
                ContentHash = file.ContentHash,
                AllowInstallScriptsRequested = file.Function!.Source.AllowInstallScripts,
                UnresolvedSecretRefs = UnresolvedIn(incoming),
            };

            var check = await CheckSecretNamesAsync(incoming, cancellationToken);
            preview.MissingSecrets = check.Missing;
            preview.DuplicateSecrets = check.Duplicates;
            preview.SecretsUnchecked = check.Unavailable;
            AddWarnings(preview.Warnings, preview.AllowInstallScriptsRequested, check, preview.UnresolvedSecretRefs);

            var existing = await _repository.GetByIdAsync(tenantId, incoming.ItemId, cancellationToken);
            if (existing is null)
            {
                if (await _repository.GetDeletedAsync(tenantId, incoming.ItemId, cancellationToken) is not null)
                {
                    preview.Status = FunctionImportStatuses.Blocked;
                    preview.BlockedReason = BlockedReason;
                    return preview;
                }
                preview.Status = FunctionImportStatuses.New;
                return preview;
            }

            preview.TargetName = existing.Name;
            preview.TargetLastUpdatedDate = existing.LastUpdatedDate;
            var mine = await MineAsync(existing, cancellationToken);
            if (FunctionTransferCodec.Canonical(mine) == FunctionTransferCodec.Canonical(incoming))
            {
                preview.Status = FunctionImportStatuses.Same;
                return preview;
            }

            preview.Status = FunctionImportStatuses.Changed;
            Diff(mine, incoming, preview);
            return preview;
        }

        private const string BlockedReason =
            "A function with this id was deleted here and is still being cleaned up. Its id cannot be reused until the " +
            "clean-up has finished (usually within minutes); import again after that.";

        private static void Diff(FunctionExportBody mine, FunctionExportBody incoming, FunctionImportPreviewDto preview)
        {
            void Field<T>(string field, string label, T a, T b, bool pretty = true)
            {
                var left = FunctionTransferCodec.CanonicalOf(a);
                var right = FunctionTransferCodec.CanonicalOf(b);
                if (left == right) return;
                preview.Fields.Add(new FunctionImportFieldDiffDto
                {
                    Field = field,
                    Label = label,
                    Mine = pretty ? FunctionTransferCodec.Pretty(a) : a?.ToString(),
                    Incoming = pretty ? FunctionTransferCodec.Pretty(b) : b?.ToString(),
                });
            }

            void File(string field, string name, string language, string? a, string? b)
            {
                if (string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal)
                    && (a is null) == (b is null)) return;
                preview.Files.Add(new FunctionImportFileDiffDto { Field = field, File = name, Language = language, Mine = a, Incoming = b });
            }

            Field(FunctionTransferCodec.Fields.Name, "Name", mine.Name, incoming.Name, pretty: false);
            Field(FunctionTransferCodec.Fields.Description, "Description", mine.Description, incoming.Description, pretty: false);
            Field(FunctionTransferCodec.Fields.Trigger, "Trigger", mine.Trigger, incoming.Trigger);
            Field(FunctionTransferCodec.Fields.RateLimit, "Rate limit (calls per minute)",
                mine.RequestsPerMinute, incoming.RequestsPerMinute, pretty: false);
            Field(FunctionTransferCodec.Fields.Variables, "Variables", mine.Variables, incoming.Variables);
            Field(FunctionTransferCodec.Fields.OutputActions, "Output actions", mine.OutputActions, incoming.OutputActions);
            File(FunctionTransferCodec.Fields.IndexJs, "index.js", "javascript", mine.Source.IndexJs, incoming.Source.IndexJs);
            File(FunctionTransferCodec.Fields.PackageJson, "package.json", "json", mine.Source.PackageJson, incoming.Source.PackageJson);
            File(FunctionTransferCodec.Fields.LockJson, "package-lock.json", "json", mine.Source.LockJson, incoming.Source.LockJson);
        }

        // ================================================================= apply ====

        public async Task<FunctionImportResultDto> ApplyAsync(
            string tenantId, FunctionImportApplyRequestDto request, string? actorId, string? actorEmail,
            CancellationToken cancellationToken = default)
        {
            var file = await ValidateFileAsync(request.File);
            var incoming = FunctionTransferCodec.Normalized(file.Function!);
            var requestedScripts = file.Function!.Source.AllowInstallScripts;

            var check = await CheckSecretNamesAsync(incoming, cancellationToken);
            var result = new FunctionImportResultDto
            {
                FunctionId = incoming.ItemId,
                Name = incoming.Name,
                MissingSecrets = check.Missing,
                DuplicateSecrets = check.Duplicates,
            };

            var existing = await _repository.GetByIdAsync(tenantId, incoming.ItemId, cancellationToken);
            if (existing is null)
            {
                if (await _repository.GetDeletedAsync(tenantId, incoming.ItemId, cancellationToken) is not null)
                {
                    throw new FunctionConflictException(BlockedReason);
                }
                await CreateImportedAsync(tenantId, incoming, check, actorId, cancellationToken);
                result.Status = "created";
                result.AllowInstallScriptsReset = requestedScripts;
                await _audit.RecordAsync(
                    tenantId, incoming.ItemId, FunctionsConstants.AuditActions.Imported, actorId, actorEmail,
                    new { Status = result.Status, file.ContentHash }, cancellationToken);
            }
            else
            {
                var changed = await UpdateImportedAsync(tenantId, existing, incoming, check, request, actorId, cancellationToken);
                if (changed.Count == 0)
                {
                    // Same file again, keepMine, or a merge that kept everything: a no-op, nothing written or deployed.
                    result.Status = "unchanged";
                    result.Messages.Add("Nothing changed: this function already matches what you chose.");
                    return result;
                }
                result.Status = "updated";
                // Install scripts keep the target's setting; an import never turns them on.
                result.AllowInstallScriptsReset = requestedScripts && !(existing.Source?.AllowInstallScripts ?? false);
                await _audit.RecordAsync(
                    tenantId, incoming.ItemId, FunctionsConstants.AuditActions.Imported, actorId, actorEmail,
                    new { Status = result.Status, Fields = changed, file.ContentHash }, cancellationToken);
            }

            if (result.AllowInstallScriptsReset)
            {
                result.Messages.Add(
                    "The file had install scripts turned on. An import always leaves them off; turn them on in the editor if this function needs them.");
            }
            if (check.Missing.Count > 0 || check.Duplicates.Count > 0)
            {
                result.Messages.Add(
                    "Some secrets are not bound yet. Add a secret with each missing name in Blocks OS secrets (or remove the duplicates); the next run picks it up with no other change.");
            }

            result.Deploy = await DeployAsync(tenantId, incoming.ItemId, actorId, actorEmail, cancellationToken);
            return result;
        }

        /// <summary>
        /// The new internal create path (the public Create is unchanged): the file's id, the importer as author, now as
        /// the dates, Draft with no active version, install scripts off.
        /// </summary>
        private async Task CreateImportedAsync(
            string tenantId, FunctionExportBody incoming, SecretCheck check, string? actorId, CancellationToken cancellationToken)
        {
            var (variables, actions) = Bind(incoming.Variables, incoming.OutputActions, check);
            var source = new FunctionSource
            {
                IndexJs = incoming.Source.IndexJs,
                PackageJson = incoming.Source.PackageJson,
                LockJson = incoming.Source.LockJson,
                AllowInstallScripts = false,
            };
            var now = DateTime.UtcNow;
            var function = new FunctionEntity
            {
                ItemId = incoming.ItemId,
                CreatedDate = now,
                LastUpdatedDate = now,
                CreatedBy = actorId ?? string.Empty,
                LastUpdatedBy = actorId ?? string.Empty,
                Name = incoming.Name,
                Description = incoming.Description,
                Status = FunctionStatus.Draft,
                ActiveVersionId = null,
                Source = source,
                SourceHash = FunctionHashing.SourceHash(source),
                // Fixed by the platform: the file never carries them, the import always stores the platform profile.
                Limits = new FunctionLimits { RequestsPerMinute = incoming.RequestsPerMinute }.Clamp(),
                Retry = RetryPolicy.Fixed,
                Trigger = incoming.Trigger,
                OutputActions = actions,
                Variables = variables,
            };

            try
            {
                await _repository.CreateAsync(tenantId, function, cancellationToken);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // The preview said new because the read could not see it (data access policy, or a create in between).
                // Never overwritten.
                throw new FunctionConflictException(
                    "A function with this id already exists here (you may not be allowed to see it). Nothing was changed.");
            }
        }

        /// <summary>The fields an update changed (empty = nothing to write). Throws 409 when the target moved.</summary>
        private async Task<List<string>> UpdateImportedAsync(
            string tenantId, FunctionEntity existing, FunctionExportBody incoming, SecretCheck check,
            FunctionImportApplyRequestDto request, string? actorId, CancellationToken cancellationToken)
        {
            var resolution = request.Resolution ?? string.Empty;
            if (resolution is not (Resolution_TakeIncoming or Resolution_KeepMine or Resolution_Merge))
            {
                throw new FunctionValidationException(
                    $"resolution must be \"{Resolution_TakeIncoming}\", \"{Resolution_KeepMine}\" or \"{Resolution_Merge}\".");
            }
            foreach (var (field, choice) in request.FieldChoices ?? [])
            {
                if (!FunctionTransferCodec.Fields.All.Contains(field, StringComparer.Ordinal) || choice is not ("mine" or "incoming"))
                {
                    throw new FunctionValidationException($"fieldChoices: \"{field}\" must be one of the import fields, set to \"mine\" or \"incoming\".");
                }
            }

            bool TakeIncoming(string field) => resolution switch
            {
                Resolution_TakeIncoming => true,
                Resolution_KeepMine => false,
                _ => request.FieldChoices is not null && request.FieldChoices.TryGetValue(field, out var c) && c == "incoming",
            };

            var mineSource = existing.Source ?? new FunctionSource();
            var (boundVariables, boundActions) = Bind(incoming.Variables, incoming.OutputActions, check);
            var changed = new List<string>();

            T Pick<T>(string field, T mine, T theirs, Func<T, T, bool> same)
            {
                if (!TakeIncoming(field) || same(mine, theirs)) return mine;
                changed.Add(field);
                return theirs;
            }

            // Compared in the file's form (names), so a secret bound here by id is "the same" as its name in the file.
            var mineBody = await MineAsync(existing, cancellationToken);
            bool SameJson<T>(T a, T b) => FunctionTransferCodec.CanonicalOf(a) == FunctionTransferCodec.CanonicalOf(b);

            var name = Pick(FunctionTransferCodec.Fields.Name, existing.Name, incoming.Name, string.Equals);
            var description = Pick(FunctionTransferCodec.Fields.Description, existing.Description, incoming.Description, string.Equals);
            var trigger = Pick(FunctionTransferCodec.Fields.Trigger, existing.Trigger, incoming.Trigger, (_, b) => SameJson(mineBody.Trigger, b));
            var rateLimit = Pick(FunctionTransferCodec.Fields.RateLimit, mineBody.RequestsPerMinute, incoming.RequestsPerMinute,
                (a, b) => a == b);
            var variables = TakeIncoming(FunctionTransferCodec.Fields.Variables) && !SameJson(mineBody.Variables, incoming.Variables)
                ? Changed(FunctionTransferCodec.Fields.Variables, boundVariables)
                : existing.Variables;
            var actions = TakeIncoming(FunctionTransferCodec.Fields.OutputActions) && !SameJson(mineBody.OutputActions, incoming.OutputActions)
                ? Changed(FunctionTransferCodec.Fields.OutputActions, boundActions)
                : existing.OutputActions;
            var indexJs = Pick(FunctionTransferCodec.Fields.IndexJs, mineSource.IndexJs, incoming.Source.IndexJs, string.Equals);
            var packageJson = Pick(FunctionTransferCodec.Fields.PackageJson, mineSource.PackageJson, incoming.Source.PackageJson, string.Equals);
            var lockJson = Pick(FunctionTransferCodec.Fields.LockJson, mineSource.LockJson, incoming.Source.LockJson,
                (a, b) => string.Equals(a, b, StringComparison.Ordinal));

            if (changed.Count == 0) return changed;

            if (request.ExpectedLastUpdatedDate is not { } expected)
            {
                throw new FunctionValidationException(
                    "expectedLastUpdatedDate is required to change an existing function: preview the file first.");
            }

            // The result is a function like any other save: same checks. A value already stored unchanged still passes
            // the credential check, exactly as in Save.
            var source = new FunctionSource
            {
                IndexJs = indexJs,
                PackageJson = packageJson,
                LockJson = lockJson,
                AllowInstallScripts = mineSource.AllowInstallScripts,
            };
            var limits = new FunctionLimits { RequestsPerMinute = rateLimit }.Clamp();
            var retry = RetryPolicy.Fixed;
            await ValidateResultAsync(existing.ItemId, name, description, source, limits, retry, trigger, actions, variables);
            var literals = FunctionSecretLiterals.FindNewLiterals(variables, actions, existing.Variables, existing.OutputActions);
            if (literals.Count > 0) throw new FunctionValidationException(string.Join("; ", literals));

            var updated = await _repository.UpdateImportedAsync(
                tenantId, existing.ItemId, AsUtc(expected), name, description, source, FunctionHashing.SourceHash(source),
                limits, retry, FunctionService.NormalizeForStorage(trigger),
                actions ?? [], variables ?? [], actorId, cancellationToken);
            if (!updated)
            {
                throw new FunctionConflictException(
                    "This function changed after the preview (or is no longer here). Nothing was written; preview the file again.");
            }

            _logger.LogInformation("Function {FunctionId} updated by import; fields: {Fields}", existing.ItemId, string.Join(", ", changed));
            return changed;

            TValue Changed<TValue>(string field, TValue value)
            {
                changed.Add(field);
                return value;
            }
        }

        private async Task<FunctionImportDeployDto> DeployAsync(
            string tenantId, string functionId, string? actorId, string? actorEmail, CancellationToken cancellationToken)
        {
            // The console's Deploy, unchanged: build (or reuse) and wait a short while; a longer build answers
            // "building" and the console watches it and deploys that build, exactly as after Deploy. A failure here
            // never undoes the import — the function is saved, and Deploy can be retried from the editor.
            try
            {
                var outcome = await _deployments.DeployAsync(
                    tenantId, new DeployFunctionRequestDto { FunctionId = functionId, Note = "Imported" },
                    actorId, actorEmail, cancellationToken);
                return outcome.Version is { } version
                    ? new FunctionImportDeployDto { State = FunctionImportDeployStates.Live, VersionNumber = version.Number }
                    : new FunctionImportDeployDto { State = FunctionImportDeployStates.Building, BuildId = outcome.BuildId };
            }
            catch (FunctionBuildFailedException ex)
            {
                return new FunctionImportDeployDto { State = FunctionImportDeployStates.Failed, BuildId = ex.BuildId, Error = ex.Message };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Imported function {FunctionId} could not be deployed ({Type})", functionId, ex.GetType().Name);
                var message = ex is FunctionValidationException or FunctionUnavailableException or FunctionNotFoundException
                    ? ex.Message
                    : "the deploy could not start";
                return new FunctionImportDeployDto { State = FunctionImportDeployStates.Failed, Error = message };
            }
        }

        // =============================================================== helpers ====

        /// <summary>
        /// Every check an uploaded file must pass, for preview and apply alike: size, format, schema, id, hash, no secret
        /// ids, no typed credentials, and the normal function validation.
        /// </summary>
        private async Task<FunctionExportFile> ValidateFileAsync(System.Text.Json.JsonElement raw)
        {
            var file = FunctionTransferCodec.Parse(raw);

            if (!string.Equals(file.Format, FunctionTransferCodec.Format, StringComparison.Ordinal))
            {
                throw new FunctionValidationException($"This is not a function export file (format must be \"{FunctionTransferCodec.Format}\").");
            }
            if (file.SchemaVersion is >= 1 and < FunctionTransferCodec.SchemaVersion)
            {
                // Functions are dev-only (2026-10-06): no reader for the old shape that carried limits and retry.
                throw new FunctionValidationException(
                    $"This export was made by an older version (schema {file.SchemaVersion}). Export the function again and import the new file.");
            }
            if (file.SchemaVersion != FunctionTransferCodec.SchemaVersion)
            {
                throw new FunctionValidationException(
                    $"This export uses schema version {file.SchemaVersion}; this environment reads version {FunctionTransferCodec.SchemaVersion} only.");
            }
            var body = file.Function ?? throw new FunctionValidationException("The export file has no function in it.");
            body.Source ??= new FunctionExportSource();
            body.Trigger ??= new TriggerConfig();
            body.Variables ??= [];
            body.OutputActions ??= [];

            if (!FunctionTransferCodec.IsValidItemId(body.ItemId))
            {
                throw new FunctionValidationException("The function id in the file is not a valid function id.");
            }
            if (!string.Equals(FunctionTransferCodec.Hash(body), file.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new FunctionValidationException(
                    "The file's content does not match its contentHash: it was changed after it was exported. Export it again.");
            }

            var rawIds = FunctionSecretRefs.Locations(body.Variables, body.OutputActions)
                .Where(l => FunctionSecretRefs.IdsIn(l.Value).Any())
                .Select(l => l.Where)
                .ToList();
            if (rawIds.Count > 0)
            {
                throw new FunctionValidationException(
                    $"The file contains secret ids ({string.Join(", ", rawIds)}). Secret ids differ per environment, so an " +
                    "export carries secret names only — export the function again.");
            }

            var literals = FunctionSecretLiterals.FindNewLiterals(body.Variables, body.OutputActions, null, null);
            if (literals.Count > 0) throw new FunctionValidationException(string.Join("; ", literals));

            // Each name costs a paged catalog search here and at every run start: a file is not a way to make the
            // platform search the catalog thousands of times.
            if (FunctionSecretRefs.NamesIn(body.Variables, body.OutputActions).Count > MaxSecretNames)
            {
                throw new FunctionValidationException($"The file uses more than {MaxSecretNames} different secret names.");
            }

            var normalized = FunctionTransferCodec.Normalized(body);
            await ValidateResultAsync(
                body.ItemId, normalized.Name, normalized.Description,
                new FunctionSource
                {
                    IndexJs = normalized.Source.IndexJs,
                    PackageJson = normalized.Source.PackageJson,
                    LockJson = normalized.Source.LockJson,
                },
                new FunctionLimits { RequestsPerMinute = normalized.RequestsPerMinute }.Clamp(), RetryPolicy.Fixed,
                normalized.Trigger, normalized.OutputActions, normalized.Variables);
            return file;
        }

        private async Task ValidateResultAsync(
            string functionId, string name, string? description, FunctionSource source,
            FunctionLimits? limits, RetryPolicy? retry, TriggerConfig? trigger,
            List<OutputAction>? actions, List<VariableBinding>? variables)
        {
            var errors = new List<string>();
            var details = await _detailsValidator.ValidateAsync(new UpdateFunctionRequestDto
            {
                FunctionId = functionId,
                Name = name,
                Description = description,
            });
            errors.AddRange(details.Errors.Select(e => e.ErrorMessage));

            var save = await _saveValidator.ValidateAsync(new SaveFunctionRequestDto
            {
                FunctionId = functionId,
                IndexJs = source.IndexJs ?? string.Empty,
                PackageJson = source.PackageJson ?? string.Empty,
                LockJson = source.LockJson,
                Limits = limits ?? new FunctionLimits(),
                Retry = retry ?? RetryPolicy.Fixed,
                Trigger = trigger ?? new TriggerConfig(),
                OutputActions = actions ?? [],
                Variables = variables ?? [],
            });
            errors.AddRange(save.Errors.Select(e => e.ErrorMessage));

            // The lockfile has no bound in the save validator; the runner refuses one over the source cap anyway.
            if (System.Text.Encoding.UTF8.GetByteCount(source.LockJson ?? string.Empty) > FunctionTransferCodec.MaxImportBytes
                || System.Text.Encoding.UTF8.GetByteCount(source.PackageJson ?? string.Empty) > FunctionTransferCodec.MaxImportBytes)
            {
                errors.Add("package.json or the lockfile is too large.");
            }

            if (errors.Count > 0) throw new FunctionValidationException(string.Join("; ", errors.Distinct()));
        }

        /// <summary>The stored function in the file's form, for comparing and for the preview's "mine" side.</summary>
        private async Task<FunctionExportBody> MineAsync(FunctionEntity existing, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, string> namesById;
            try
            {
                namesById = await _secrets.NamesByIdAsync(
                    FunctionSecretRefs.IdsIn(existing.Variables, existing.OutputActions), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unknown names show as "unresolved" in the diff; an apply only ever writes what was chosen.
                _logger.LogWarning("Import compare for {FunctionId}: the secret catalog could not be read ({Type})",
                    existing.ItemId, ex.GetType().Name);
                namesById = new Dictionary<string, string>();
            }
            return FunctionTransferCodec.Normalized(ToExportBody(existing, namesById).Body);
        }

        private sealed record SecretCheck(
            Dictionary<string, string> Ids, List<string> Missing, List<string> Duplicates, bool Unavailable);

        private async Task<SecretCheck> CheckSecretNamesAsync(FunctionExportBody body, CancellationToken cancellationToken)
        {
            var names = FunctionSecretRefs.NamesIn(body.Variables, body.OutputActions);
            var check = new SecretCheck(new Dictionary<string, string>(StringComparer.Ordinal), [], [], false);
            if (names.Count == 0) return check;

            try
            {
                var found = await _secrets.IdsByNameAsync(names, cancellationToken);
                foreach (var name in names)
                {
                    var ids = found.TryGetValue(name, out var list) ? list : [];
                    if (ids.Count == 0) check.Missing.Add(name);
                    else if (ids.Count > 1) check.Duplicates.Add(name);
                    else check.Ids[name] = ids[0];
                }
                return check;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never refuses the import: every name just stays unbound and is looked up at run start.
                _logger.LogWarning("Import secret check: the secret catalog could not be read ({Type})", ex.GetType().Name);
                return check with { Unavailable = true };
            }
        }

        /// <summary>A name with exactly one secret here becomes <c>{{secret.&lt;id&gt;}}</c>; any other stays a name.</summary>
        private static (List<VariableBinding>, List<OutputAction>) Bind(
            List<VariableBinding> variables, List<OutputAction> actions, SecretCheck check) =>
            FunctionSecretRefs.Rewrite(variables, actions, value =>
                FunctionSecretRefs.ReplaceNames(value, name =>
                    check.Ids.TryGetValue(name, out var id) ? FunctionSecretRefs.IdToken(id) : FunctionSecretRefs.NameToken(name)));

        private static List<string> UnresolvedIn(FunctionExportBody body) =>
            FunctionSecretRefs.Locations(body.Variables, body.OutputActions)
                .Where(l => l.Value.Contains(FunctionSecretRefs.UnresolvedMarker, StringComparison.Ordinal))
                .Select(l => l.Where)
                .ToList();

        private static void AddWarnings(
            List<string> warnings, bool allowInstallScripts, SecretCheck check, List<string> unresolved)
        {
            if (allowInstallScripts)
            {
                warnings.Add("The file has install scripts turned on. They arrive OFF here; turn them on in the editor only if you trust every package.");
            }
            if (check.Missing.Count > 0)
            {
                warnings.Add($"No secret named {string.Join(", ", check.Missing)} exists here. Add it in Blocks OS secrets with that exact name; nothing else is needed.");
            }
            if (check.Duplicates.Count > 0)
            {
                warnings.Add($"More than one secret is named {string.Join(", ", check.Duplicates)}. Runs that use it fail until only one is left.");
            }
            if (check.Unavailable)
            {
                warnings.Add("The secret store could not be checked. Secret names stay unbound and are looked up when the function runs.");
            }
            if (unresolved.Count > 0)
            {
                warnings.Add($"Some references had no secret where the file was exported ({string.Join(", ", unresolved)}). Pick a secret for them in the editor after import.");
            }
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
