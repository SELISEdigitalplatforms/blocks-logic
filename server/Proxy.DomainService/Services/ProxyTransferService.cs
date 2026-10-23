using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;
using Blocks.Genesis.Entities.Logic;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Proxy export / import (2026-10-18). A file carries one proxy with its own <c>ItemId</c> and slug, so importing it
    /// into another environment gives the same proxy there. Preview and apply share one analysis, and apply always
    /// runs it again: the client's preview is never trusted.
    /// <para>
    /// Secrets: only <c>{{$VAR.name}}</c> names travel. A literal credential travels as <c>***</c> and an existing value
    /// here is kept. A name with no secret here does NOT refuse the import (approved PX-9 exception, import path
    /// only); it stays unbound and the call path looks it up by name, so adding the secret is the only step left.
    /// Logs carry ids, slug and status only &mdash; never the file or any configured value.
    /// </para>
    /// </summary>
    public sealed class ProxyTransferService : IProxyTransferService
    {
        public const string StatusNew = "new";
        public const string StatusSame = "same";
        public const string StatusChanged = "changed";
        public const string StatusBlocked = "blocked";

        public const string TakeIncoming = "takeIncoming";
        public const string KeepMine = "keepMine";
        public const string Merge = "merge";

        private const string ImportedSummary = "Imported from file";

        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyVersionRepository _proxyVersionRepository;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ProxyTransferService> _logger;
        private readonly IProxyVariableResolver? _variableResolver;

        public ProxyTransferService(
            IProxyRepository proxyRepository,
            IProxyVersionRepository proxyVersionRepository,
            TimeProvider timeProvider,
            ILogger<ProxyTransferService> logger,
            IProxyVariableResolver? variableResolver = null)
        {
            _proxyRepository = proxyRepository;
            _proxyVersionRepository = proxyVersionRepository;
            _timeProvider = timeProvider;
            _logger = logger;
            _variableResolver = variableResolver;
        }

        public async Task<ProxyTransferResponse<ProxyExportFile>> ExportAsync(string tenantId, string itemId)
        {
            var proxy = await _proxyRepository.GetAsync(tenantId, itemId ?? string.Empty);
            if (proxy == null)
            {
                _logger.LogInformation("Proxy export: {ItemId} not found for tenant {TenantId}.", itemId, tenantId);
                return ProxyTransferResponse<ProxyExportFile>.Fail(
                    404, ProxyErrorCodes.NotFound, $"Proxy '{itemId}' was not found.");
            }

            var file = ProxyTransferCodec.BuildFile(proxy, _timeProvider.GetUtcNow().UtcDateTime);
            _logger.LogInformation(
                "Exported proxy {ItemId} (slug {Slug}) for tenant {TenantId}; {Masked} value(s) masked.",
                proxy.ItemId, proxy.Slug, tenantId, file.MaskedValues?.Count ?? 0);
            return ProxyTransferResponse<ProxyExportFile>.Ok(file);
        }

        public async Task<ProxyTransferResponse<ProxyImportResultDto>> PreviewAsync(string tenantId, ProxyExportFile? file)
        {
            var analysis = await AnalyzeAsync(tenantId, file);
            if (analysis.Failure is not null) return analysis.Failure;

            _logger.LogInformation(
                "Proxy import preview for tenant {TenantId}: {ItemId} (slug {Slug}) is {Status}.",
                tenantId, analysis.Result.ItemId, analysis.Result.Slug, analysis.Result.Status);
            return ProxyTransferResponse<ProxyImportResultDto>.Ok(analysis.Result);
        }

        public async Task<ProxyTransferResponse<ProxyImportResultDto>> ApplyAsync(
            string tenantId, ProxyImportApplyRequestDto request)
        {
            var analysis = await AnalyzeAsync(tenantId, request?.File);
            if (analysis.Failure is not null) return analysis.Failure;

            var result = analysis.Result;
            switch (result.Status)
            {
                case StatusBlocked:
                    _logger.LogWarning(
                        "Proxy import for tenant {TenantId} refused: {ItemId} (slug {Slug}) is blocked ({ReasonCode}).",
                        tenantId, result.ItemId, result.Slug, result.ReasonCode);
                    return ProxyTransferResponse<ProxyImportResultDto>.Fail(
                        409, ProxyErrorCodes.ImportBlocked, result.Reason ?? "This file cannot be imported here.", data: result);
                case StatusSame:
                    // Replaying a file is safe: nothing to write.
                    _logger.LogInformation(
                        "Proxy import for tenant {TenantId}: {ItemId} already matches the file; nothing written.",
                        tenantId, result.ItemId);
                    return ProxyTransferResponse<ProxyImportResultDto>.Ok(result);
                case StatusNew:
                    return await InsertAsync(tenantId, analysis);
                default:
                    return await UpdateAsync(tenantId, analysis, request!);
            }
        }

        /// <summary>
        /// The new internal create path: keeps the file's id and slug (public Create always makes a new id, and still
        /// does). Missing or duplicate secret names are left unbound, never refused.
        /// </summary>
        private async Task<ProxyTransferResponse<ProxyImportResultDto>> InsertAsync(string tenantId, Analysis analysis)
        {
            var result = analysis.Result;
            var proxy = analysis.Incoming;
            var userId = ProxyVersionFactory.CurrentUserId();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            proxy.TenantId = tenantId;
            proxy.CreatedBy = userId;
            proxy.LastUpdatedBy = userId;
            proxy.CreatedDate = now;
            proxy.LastUpdatedDate = now;
            proxy.CurrentVersion = result.CurrentVersion ?? 1;
            proxy.Stats = new ProxyStats();
            proxy.SecretIds = analysis.Secrets.Ids;

            try
            {
                await _proxyRepository.InsertAsync(proxy);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // The id or the slug is taken after all: a concurrent import, or a row this caller cannot see.
                _logger.LogWarning(
                    "Proxy import for tenant {TenantId}: insert of {ItemId} (slug {Slug}) hit an existing id or slug.",
                    tenantId, proxy.ItemId, proxy.Slug);
                result.Status = StatusBlocked;
                result.ReasonCode = "slugConflict";
                result.Reason = "A proxy with this id or address already exists here. Preview the file again.";
                return ProxyTransferResponse<ProxyImportResultDto>.Fail(
                    409, ProxyErrorCodes.ImportBlocked, result.Reason, data: result);
            }

            var version = ProxyVersionFactory.Build(
                proxy, proxy.CurrentVersion, ProxyVersionKind.Create, ImportedSummary,
                new List<ProxyFieldChange>(), ProxyVersionFactory.SnapshotOf(proxy), userId,
                ProxyVersionFactory.CurrentUserName());
            await _proxyVersionRepository.InsertAsync(version);

            result.Applied = true;
            _logger.LogInformation(
                "Imported new proxy {ItemId} (slug {Slug}) for tenant {TenantId}; wrote version {Version}; "
                + "{Unbound} secret name(s) left unbound.",
                proxy.ItemId, proxy.Slug, tenantId, proxy.CurrentVersion,
                analysis.Secrets.Missing.Count + analysis.Secrets.Duplicates.Count);
            return ProxyTransferResponse<ProxyImportResultDto>.Ok(result);
        }

        private async Task<ProxyTransferResponse<ProxyImportResultDto>> UpdateAsync(
            string tenantId, Analysis analysis, ProxyImportApplyRequestDto request)
        {
            var result = analysis.Result;
            var current = analysis.Current!;
            var incoming = analysis.Incoming;

            var resolution = ParseResolution(request.Resolution);
            if (resolution is null)
            {
                return Invalid("resolution", "Choose takeIncoming, keepMine or merge.");
            }

            if (resolution == KeepMine)
            {
                _logger.LogInformation(
                    "Proxy import for tenant {TenantId}: kept this environment's {ItemId}; nothing written.",
                    tenantId, current.ItemId);
                return ProxyTransferResponse<ProxyImportResultDto>.Ok(result);
            }

            if (request.ExpectedVersion is { } expected && expected != current.CurrentVersion)
            {
                return VersionConflict(tenantId, current.ItemId, expected, current.CurrentVersion);
            }

            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            if (resolution == Merge)
            {
                foreach (var (field, choice) in request.FieldChoices ?? new Dictionary<string, string>())
                {
                    if (!string.Equals(choice, "mine", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(choice, "incoming", StringComparison.OrdinalIgnoreCase))
                    {
                        return Invalid("fieldChoices", "Each field choice must be mine or incoming.");
                    }

                    choices[field] = choice.ToLowerInvariant();
                }
            }

            var before = ProxyVersionFactory.SnapshotOf(current);
            var readVersion = current.CurrentVersion;

            if (resolution == TakeIncoming)
            {
                CopyConfig(incoming, current);
            }
            else
            {
                // Only addresses the server's own diff produced: a made-up address in the request changes nothing.
                foreach (var change in result.Changes)
                {
                    if (choices.TryGetValue(change.Field, out var choice) && choice == "incoming")
                    {
                        ApplyIncoming(current, incoming, change.Field);
                    }
                }
            }

            // A *** from the file never replaces a value this environment has.
            ProxyTransferCodec.RestoreMasked(current, before);

            // A per-field merge can combine fields into something neither side had, so the result goes through the
            // validator again; its normalized values are what gets saved.
            var final = ValidateConfig(ProxyTransferCodec.ToExportConfig(current, mask: false));
            if (!final.IsValid)
            {
                _logger.LogWarning(
                    "Proxy import for tenant {TenantId}: merged config for {ItemId} is invalid: {Fields}.",
                    tenantId, current.ItemId, string.Join(", ", final.Errors.Keys));
                return ProxyTransferResponse<ProxyImportResultDto>.Fail(
                    400, ProxyErrorCodes.Validation, "The merged proxy configuration is invalid.", final.Errors);
            }

            if (!string.Equals(final.Slug, current.Slug, StringComparison.Ordinal))
            {
                // Same rule as Update: a rename INTO another proxy's slug is refused.
                var owner = await _proxyRepository.GetBySlugAsync(tenantId, final.Slug);
                if (owner != null && !string.Equals(owner.ItemId, current.ItemId, StringComparison.Ordinal))
                {
                    return ProxyTransferResponse<ProxyImportResultDto>.Fail(
                        409, ProxyErrorCodes.SlugConflict, $"A proxy named '{final.Name}' already exists.");
                }
            }

            ApplyValidated(final, current);
            var changes = ProxyChangeSet.Diff(before, ProxyVersionFactory.SnapshotOf(current));
            if (changes.Count == 0)
            {
                result.Status = StatusSame;
                _logger.LogInformation(
                    "Proxy import for tenant {TenantId}: chosen fields leave {ItemId} unchanged; nothing written.",
                    tenantId, current.ItemId);
                return ProxyTransferResponse<ProxyImportResultDto>.Ok(result);
            }

            // Ids are bound for the config being saved, the same lazy way as an insert. A name that does not resolve
            // here loses its id; ids from the other environment never reach this point (they are not in the file).
            var secrets = await ProxySecretIdBinder.CheckForImportAsync(
                _variableResolver, tenantId, ProxyVarRef.NamesIn(current), current.SecretIds, _logger);
            current.SecretIds = secrets.Ids;
            result.MissingSecrets = secrets.Missing;
            result.DuplicateSecrets = secrets.Duplicates;
            result.SecretCheckUnavailable = secrets.Unavailable;
            result.MaskedValues = ProxyTransferCodec.MaskedAddresses(current);

            current.LastUpdatedDate = _timeProvider.GetUtcNow().UtcDateTime;
            current.LastUpdatedBy = ProxyVersionFactory.CurrentUserId();
            current.CurrentVersion = readVersion + 1;

            if (!await _proxyRepository.SaveConfigAsync(current, readVersion))
            {
                return VersionConflict(tenantId, current.ItemId, readVersion, null);
            }

            var summary = $"{ImportedSummary}: {ProxyChangeSet.Summarize(changes)}";
            var version = ProxyVersionFactory.Build(
                current, current.CurrentVersion, ProxyVersionKind.ConfigUpdate, summary, changes,
                ProxyVersionFactory.SnapshotOf(current), current.LastUpdatedBy ?? "system",
                ProxyVersionFactory.CurrentUserName());
            await _proxyVersionRepository.InsertAsync(version);

            result.CurrentVersion = current.CurrentVersion;
            result.Applied = true;
            _logger.LogInformation(
                "Imported file onto proxy {ItemId} for tenant {TenantId} ({Resolution}); wrote version {Version}.",
                current.ItemId, tenantId, resolution, current.CurrentVersion);
            return ProxyTransferResponse<ProxyImportResultDto>.Ok(result);
        }

        /// <summary>Everything preview reports and apply needs. <see cref="Failure"/> set ⇒ the file was refused.</summary>
        private sealed class Analysis
        {
            public ProxyTransferResponse<ProxyImportResultDto>? Failure { get; init; }

            public ProxyImportResultDto Result { get; init; } = new();

            public ProxyDetailEntity Incoming { get; init; } = null!;

            public ProxyDetailEntity? Current { get; init; }

            public ProxySecretCheck Secrets { get; init; } = new();
        }

        private async Task<Analysis> AnalyzeAsync(string tenantId, ProxyExportFile? file)
        {
            if (file is null || file.Proxy is null)
            {
                return Refuse("file", "This is not a Blocks proxy export file.");
            }

            if (!string.Equals(file.Format, ProxyTransferCodec.Format, StringComparison.Ordinal))
            {
                return Refuse("format", "This is not a Blocks proxy export file.");
            }

            if (file.SchemaVersion != ProxyTransferCodec.SchemaVersion)
            {
                return Refuse("schemaVersion", $"This file uses export version {file.SchemaVersion?.ToString() ?? "(none)"}, which this Blocks version cannot read.");
            }

            var canonical = ProxyTransferCodec.Canonicalize(file.Proxy);
            if (Encoding.UTF8.GetByteCount(canonical) > ProxyTransferCodec.MaxConfigBytes)
            {
                return new Analysis
                {
                    Failure = ProxyTransferResponse<ProxyImportResultDto>.Fail(
                        413, ProxyErrorCodes.Validation, "The file is too large.",
                        Errors("file", "A proxy file may be at most 256 KB.")),
                };
            }

            var hash = ProxyTransferCodec.Hash(file.Proxy);
            if (!string.Equals(hash, file.ContentHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Refuse("contentHash", "The file was changed after it was exported. Export it again.");
            }

            ProxyExportConfig? config;
            try
            {
                config = file.Proxy.Deserialize<ProxyExportConfig>(ProxyTransferCodec.Json);
            }
            catch (JsonException)
            {
                return Refuse("proxy", "The proxy in this file cannot be read.");
            }

            if (config is null || !ProxyTransferCodec.IsValidItemId(config.ItemId))
            {
                return Refuse("itemId", "The proxy id in this file is not valid.");
            }

            if (!ProxyTransferCodec.IsValidSlug(config.Slug))
            {
                return Refuse("slug", "The proxy address (slug) in this file is not valid.");
            }

            var validation = ValidateConfig(config);
            if (!validation.IsValid)
            {
                _logger.LogWarning(
                    "Proxy import for tenant {TenantId}: file for {ItemId} has an invalid config: {Fields}.",
                    tenantId, config.ItemId, string.Join(", ", validation.Errors.Keys));
                return new Analysis
                {
                    Failure = ProxyTransferResponse<ProxyImportResultDto>.Fail(
                        400, ProxyErrorCodes.Validation, "The proxy configuration in this file is invalid.", validation.Errors),
                };
            }

            var incoming = new ProxyDetailEntity
            {
                ItemId = config.ItemId!,
                TenantId = tenantId,
                Name = validation.Name,
                Slug = config.Slug!,
                Upstream = validation.Upstream,
                Enabled = config.Enabled,
            };
            ApplyValidated(validation, incoming);

            var result = new ProxyImportResultDto
            {
                ItemId = incoming.ItemId,
                Slug = incoming.Slug,
                Name = incoming.Name,
                MaskedValues = ProxyTransferCodec.MaskedAddresses(incoming),
                IncomingJson = ProxyTransferCodec.Canonicalize(file.Proxy, indented: true),
            };

            var current = await _proxyRepository.GetAsync(tenantId, incoming.ItemId);
            if (current == null)
            {
                var slugOwner = await _proxyRepository.GetBySlugAsync(tenantId, incoming.Slug);
                if (slugOwner != null)
                {
                    result.Status = StatusBlocked;
                    result.ReasonCode = "slugConflict";
                    result.Reason = $"Another proxy here already uses the address '{incoming.Slug}'. "
                        + "Rename or delete that proxy first.";
                }
                else
                {
                    // A deleted proxy keeps its history; a re-import numbers its versions after it.
                    var last = (await _proxyVersionRepository.GetForProxyAsync(tenantId, incoming.ItemId, 1, 0)).FirstOrDefault();
                    result.Status = StatusNew;
                    result.HistoryExists = last is not null;
                    result.CurrentVersion = (last?.VersionNumber ?? 0) + 1;
                }
            }
            else
            {
                var mine = ProxyTransferCodec.ToJson(ProxyTransferCodec.ToExportConfig(current));
                result.CurrentVersion = current.CurrentVersion;
                result.MineJson = ProxyTransferCodec.Canonicalize(mine, indented: true);

                if (!string.Equals(current.Slug, incoming.Slug, StringComparison.Ordinal))
                {
                    result.Status = StatusBlocked;
                    result.ReasonCode = "slugMismatch";
                    result.Reason = $"The proxy with this id here has the address '{current.Slug}', the file has "
                        + $"'{incoming.Slug}'. A proxy address never changes, so this file cannot be applied.";
                }
                else if (string.Equals(ProxyTransferCodec.Hash(mine), hash, StringComparison.Ordinal))
                {
                    result.Status = StatusSame;
                }
                else
                {
                    // Compared with credentials masked on both sides: a *** never shows up as a change, and no stored
                    // secret reaches the response.
                    var masked = ProxyTransferCodec.MaskSnapshot(ProxyVersionFactory.SnapshotOf(current));
                    var changes = ProxyChangeSet.Diff(masked, ProxyVersionFactory.SnapshotOf(incoming));
                    result.Status = changes.Count == 0 ? StatusSame : StatusChanged;
                    result.Changes = changes.Select(c => new ProxyImportFieldDto
                    {
                        Field = c.Field,
                        Label = c.Label,
                        Mine = ProxyChangeSet.MaskForDisplay(c.Field, c.Before),
                        Incoming = ProxyChangeSet.MaskForDisplay(c.Field, c.After),
                    }).ToList();
                }
            }

            var secrets = await ProxySecretIdBinder.CheckForImportAsync(
                _variableResolver, tenantId, ProxyVarRef.NamesIn(incoming), current?.SecretIds, _logger);
            result.MissingSecrets = secrets.Missing;
            result.DuplicateSecrets = secrets.Duplicates;
            result.SecretCheckUnavailable = secrets.Unavailable;

            return new Analysis { Result = result, Incoming = incoming, Current = current, Secrets = secrets };

            static Analysis Refuse(string field, string message) => new()
            {
                Failure = ProxyTransferResponse<ProxyImportResultDto>.Fail(
                    400, ProxyErrorCodes.Validation, message, Errors(field, message)),
            };
        }

        private static ProxyConfigValidationResult ValidateConfig(ProxyExportConfig config) =>
            ProxyConfigValidator.Validate(
                config.Name, config.Upstream, config.Methods, config.Headers, config.Query, config.MethodConfigs,
                config.BodyMerge, config.ResponseMode, config.ResponseInclude, config.Routes, config.Access,
                config.Resilience, config.RequestsPerMinute);

        private static void ApplyValidated(ProxyConfigValidationResult validation, ProxyDetailEntity target)
        {
            target.Name = validation.Name;
            target.Upstream = validation.Upstream;
            target.Methods = validation.Methods;
            target.Headers = validation.Headers;
            target.Query = validation.Query;
            target.BodyMerge = validation.BodyMerge;
            target.MethodConfigs = validation.MethodConfigs;
            target.Routes = validation.Routes;
            target.ResponseMode = validation.ResponseMode;
            target.ResponseInclude = validation.ResponseInclude;
            target.Access = validation.Access;
            target.Resilience = validation.Resilience;
            target.RequestsPerMinute = validation.RequestsPerMinute;
        }

        /// <summary>Take incoming: every config field from the file, Enabled included. Id, slug, audit and stats stay.</summary>
        private static void CopyConfig(ProxyDetailEntity from, ProxyDetailEntity to)
        {
            to.Name = from.Name;
            to.Upstream = from.Upstream;
            to.Methods = new List<HttpMethodType>(from.Methods);
            to.Enabled = from.Enabled;
            to.Headers = from.Headers.Select(ProxyVersionFactory.CloneKeyValue).ToList();
            to.Query = from.Query.Select(ProxyVersionFactory.CloneKeyValue).ToList();
            to.BodyMerge = from.BodyMerge.Select(ProxyVersionFactory.CloneKeyValue).ToList();
            to.MethodConfigs = from.MethodConfigs.Select(ProxyVersionFactory.CloneMethodConfig).ToList();
            to.Routes = from.Routes.Select(ProxyVersionFactory.CloneRoute).ToList();
            to.ResponseMode = from.ResponseMode;
            to.ResponseInclude = new List<string>(from.ResponseInclude);
            to.Access = from.Access.Clone();
            to.Resilience = ProxyVersionFactory.CloneResilience(from.Resilience);
            to.RequestsPerMinute = from.RequestsPerMinute;
        }

        /// <summary>
        /// One field of the file onto this environment's proxy, by its change-set address. The three resilience rows
        /// are words in the change set (not parseable back), so they are copied from the file's object directly.
        /// </summary>
        private static void ApplyIncoming(ProxyDetailEntity current, ProxyDetailEntity incoming, string field)
        {
            var source = ProxyVersionFactory.CloneResilience(incoming.Resilience);
            switch (field)
            {
                case "resilience:timeout":
                    SetResilience(r => r.TimeoutSeconds = source?.TimeoutSeconds);
                    return;
                case "resilience:retry":
                    SetResilience(r => r.Retry = source?.Retry);
                    return;
                case "resilience:breaker":
                    SetResilience(r => r.Breaker = source?.Breaker);
                    return;
                default:
                    ProxyChangeSet.ApplyField(current, field, ProxyChangeSet.ReadField(incoming, field));
                    return;
            }

            void SetResilience(Action<ProxyResilienceConfig> set)
            {
                var target = current.Resilience ?? new ProxyResilienceConfig();
                set(target);
                // All three unset is "not configured", the state Create gives; keep it null, not an empty object.
                current.Resilience = target.TimeoutSeconds is null && target.Retry is null && target.Breaker is null
                    ? null
                    : target;
            }
        }

        private static string? ParseResolution(string? raw) =>
            raw?.Trim() switch
            {
                var r when string.Equals(r, TakeIncoming, StringComparison.OrdinalIgnoreCase) => TakeIncoming,
                var r when string.Equals(r, KeepMine, StringComparison.OrdinalIgnoreCase) => KeepMine,
                var r when string.Equals(r, Merge, StringComparison.OrdinalIgnoreCase) => Merge,
                _ => null,
            };

        private static Dictionary<string, string> Errors(string field, string message) =>
            new(StringComparer.Ordinal) { [field] = message };

        private static ProxyTransferResponse<ProxyImportResultDto> Invalid(string field, string message) =>
            ProxyTransferResponse<ProxyImportResultDto>.Fail(400, ProxyErrorCodes.Validation, message, Errors(field, message));

        /// <summary>409 for an import that lost to another write (PX-16). Nothing was written.</summary>
        private ProxyTransferResponse<ProxyImportResultDto> VersionConflict(string tenantId, string itemId, int expected, int? stored)
        {
            _logger.LogWarning(
                "Proxy import onto {ItemId} (tenant {TenantId}) refused: expected version {Expected}, stored {Stored}.",
                itemId, tenantId, expected, stored?.ToString() ?? "changed meanwhile");
            return ProxyTransferResponse<ProxyImportResultDto>.Fail(
                409, ProxyErrorCodes.VersionConflict,
                "Someone else changed this proxy. Preview the file again, then apply.");
        }
    }
}
