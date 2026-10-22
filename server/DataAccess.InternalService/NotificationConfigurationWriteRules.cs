using Blocks.Genesis;
using Blocks.Genesis.Entities.Os;

namespace DataAccess.InternalService;

/// <summary>
/// The fields a notification configuration save may carry. The built-in fields are optional: <c>null</c>
/// means "not sent", so a client that knows only the original five fields behaves exactly as before.
/// </summary>
public sealed class NotificationConfigurationWrite
{
    public string Name { get; init; } = string.Empty;
    public NotifierTypes ChannelToNotify { get; init; }
    public NotificationReceiverTypes NotificationType { get; init; }
    public string NotifyMethod { get; init; } = string.Empty;
    public bool EnablePersistence { get; init; }

    public bool? IsBuiltIn { get; init; }
    public bool? IsDisabled { get; init; }
    public string? Description { get; init; }
    public NotificationImportance? Importance { get; init; }
    public NotificationKind? Kind { get; init; }
    public string? SourceService { get; init; }
    public string? ConsumerAppRecommendation { get; init; }
    public string? ConsumerAppNote { get; init; }
}

/// <summary>
/// One set of save and delete rules for notification configurations, shared by every writer in this
/// service so they cannot drift (same rules as the blocks-os configuration API):
/// <list type="number">
/// <item>A new built-in configuration is created only by the root tenant.</item>
/// <item>The root tenant may change every field of a built-in configuration; <c>IsBuiltIn</c> keeps the stored value when not sent.</item>
/// <item>Another tenant may change only <c>IsDisabled</c> of a built-in configuration; other fields must equal the stored values.</item>
/// <item><c>IsDisabled = true</c> is refused when the configuration cannot be disabled.</item>
/// <item>Custom configurations work as before; <c>IsBuiltIn</c> is taken only from the root tenant.</item>
/// <item>A built-in configuration is deleted only by the root tenant.</item>
/// </list>
/// </summary>
public static class NotificationConfigurationWriteRules
{
    public const string OnlyRootTenantCanManageBuiltIn = "Only_Root_Tenant_Can_Manage_Built_In_Notification_Configuration";
    public const string BuiltInIsReadOnly = "Built_In_Notification_Configuration_Is_Read_Only";
    public const string CannotBeDisabled = "Notification_Configuration_Cannot_Be_Disabled";

    /// <summary>
    /// True when the caller's tenant (<c>BlocksContext.TenantId</c>) is the root tenant. No context, an empty
    /// tenant id or an unknown tenant is never root.
    /// </summary>
    public static bool IsCallerRootTenant(ITenants tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        var tenantId = BlocksContext.GetContext()?.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        return tenants.GetTenantByID(tenantId) is { IsRootTenant: true };
    }

    /// <summary>
    /// Applies <paramref name="request"/> to <paramref name="target"/> (the stored configuration, or a new one when
    /// <paramref name="isNew"/>). Returns <c>null</c> when the save is allowed, else the refusal as
    /// (field, error key). On a refusal <paramref name="target"/> is left unchanged.
    /// </summary>
    public static (string Field, string Error)? Apply(NotificationConfiguration target, bool isNew, NotificationConfigurationWrite request, bool callerIsRoot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);

        var storedIsBuiltIn = !isNew && target.IsBuiltIn;

        // Rules 1 and 5: only the root tenant marks a configuration as built-in.
        if (!callerIsRoot && !storedIsBuiltIn && request.IsBuiltIn == true)
        {
            return (nameof(NotificationConfiguration.IsBuiltIn), OnlyRootTenantCanManageBuiltIn);
        }

        // Rule 3: another tenant may switch only IsDisabled on a built-in configuration.
        if (!callerIsRoot && storedIsBuiltIn)
        {
            var changed = FirstChangedFieldOtherThanIsDisabled(target, request);
            if (changed is not null)
            {
                return (changed, BuiltInIsReadOnly);
            }
        }

        var isBuiltIn = callerIsRoot ? request.IsBuiltIn ?? storedIsBuiltIn : storedIsBuiltIn;
        var kind = request.Kind ?? (isNew ? NotificationKind.None : target.Kind);

        // Rule 4: a job result or progress stream that a page waits for cannot be switched off.
        if (request.IsDisabled == true && !new NotificationConfiguration { IsBuiltIn = isBuiltIn, Kind = kind }.CanBeDisabled())
        {
            return (nameof(NotificationConfiguration.IsDisabled), CannotBeDisabled);
        }

        target.Name = request.Name;
        target.ChannelToNotify = request.ChannelToNotify;
        target.NotificationType = request.NotificationType;
        target.NotifyMethod = request.NotifyMethod;
        target.EnablePersistence = request.EnablePersistence;
        target.IsBuiltIn = isBuiltIn;
        target.Kind = kind;
        if (request.IsDisabled.HasValue) target.IsDisabled = request.IsDisabled.Value;
        if (request.Description is not null) target.Description = request.Description;
        if (request.Importance.HasValue) target.Importance = request.Importance.Value;
        if (request.SourceService is not null) target.SourceService = request.SourceService;
        if (request.ConsumerAppRecommendation is not null) target.ConsumerAppRecommendation = request.ConsumerAppRecommendation;
        if (request.ConsumerAppNote is not null) target.ConsumerAppNote = request.ConsumerAppNote;

        return null;
    }

    /// <summary>
    /// Rule 6 for delete: returns the refusal when <paramref name="stored"/> is built-in and the caller is not the
    /// root tenant; <c>null</c> otherwise (an unknown id deletes nothing, as before).
    /// </summary>
    public static (string Field, string Error)? CheckDelete(NotificationConfiguration? stored, bool callerIsRoot) =>
        stored is { IsBuiltIn: true } && !callerIsRoot
            ? (nameof(NotificationConfiguration.ItemId), OnlyRootTenantCanManageBuiltIn)
            : null;

    // The portal re-sends unchanged fields, so equal values are fine; optional fields not sent are unchanged.
    private static string? FirstChangedFieldOtherThanIsDisabled(NotificationConfiguration stored, NotificationConfigurationWrite request)
    {
        if (!string.Equals(stored.Name, request.Name, StringComparison.Ordinal)) return nameof(NotificationConfiguration.Name);
        if (stored.ChannelToNotify != request.ChannelToNotify) return nameof(NotificationConfiguration.ChannelToNotify);
        if (stored.NotificationType != request.NotificationType) return nameof(NotificationConfiguration.NotificationType);
        if (!string.Equals(stored.NotifyMethod, request.NotifyMethod, StringComparison.Ordinal)) return nameof(NotificationConfiguration.NotifyMethod);
        if (stored.EnablePersistence != request.EnablePersistence) return nameof(NotificationConfiguration.EnablePersistence);
        if (request.IsBuiltIn.HasValue && request.IsBuiltIn.Value != stored.IsBuiltIn) return nameof(NotificationConfiguration.IsBuiltIn);
        if (request.Description is not null && !string.Equals(request.Description, stored.Description, StringComparison.Ordinal)) return nameof(NotificationConfiguration.Description);
        if (request.Importance.HasValue && request.Importance.Value != stored.Importance) return nameof(NotificationConfiguration.Importance);
        if (request.Kind.HasValue && request.Kind.Value != stored.Kind) return nameof(NotificationConfiguration.Kind);
        if (request.SourceService is not null && !string.Equals(request.SourceService, stored.SourceService, StringComparison.Ordinal)) return nameof(NotificationConfiguration.SourceService);
        if (request.ConsumerAppRecommendation is not null && !string.Equals(request.ConsumerAppRecommendation, stored.ConsumerAppRecommendation, StringComparison.Ordinal)) return nameof(NotificationConfiguration.ConsumerAppRecommendation);
        if (request.ConsumerAppNote is not null && !string.Equals(request.ConsumerAppNote, stored.ConsumerAppNote, StringComparison.Ordinal)) return nameof(NotificationConfiguration.ConsumerAppNote);
        return null;
    }
}
