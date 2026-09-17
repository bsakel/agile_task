using Microsoft.Extensions.DependencyInjection;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddRelationalOutbox(this IServiceCollection services) =>
        services.AddSingleton<IRelationalOutbox, RelationalOutbox>();
}
