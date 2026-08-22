namespace Common.InternalService.Notification.Enums
{
    public enum NotifierTypes
    {
        SignalR,
        Firebase
    }

    public enum NotificationReceiverTypes
    {
        NoReceiverType,
        BroadcastReceiverType,
        UserSpecificReceiverType,
        FilterSpecificReceiverType
    }
}
