using DomainService.Shared;
using Blocks.Genesis.Entities.Os;

namespace DomainService.Notification
{
    public interface INotifierServiceFactory
    {
        INotifier GetNotifierServiceProvider(NotifierTypes notifierType);
    }
}
