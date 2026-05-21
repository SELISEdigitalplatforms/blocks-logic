using Microsoft.Extensions.Configuration;

namespace SeliseBlocks.ConfigurationDriver;

public static class MongoDbConfigurationExtensions
{
    public static IConfigurationBuilder AddMongoDbConfiguration(
        this IConfigurationBuilder builder,
        Action<MongoDbConfigurationOptions> configure)
    {
        var options = new MongoDbConfigurationOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("[SeliseBlocks.ConfigurationDriver] ConnectionString must not be empty.");
        if (string.IsNullOrWhiteSpace(options.DatabaseName))
            throw new ArgumentException("[SeliseBlocks.ConfigurationDriver] DatabaseName must not be empty.");
        if (string.IsNullOrWhiteSpace(options.SecretKey))
            throw new ArgumentException("[SeliseBlocks.ConfigurationDriver] SecretKey must not be empty.");

        return builder.Add(new MongoDbConfigurationSource(options));
    }
}
