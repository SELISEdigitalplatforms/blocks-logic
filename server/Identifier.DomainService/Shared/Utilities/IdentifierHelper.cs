namespace DomainService.Shared
{
    public static class IdentifierHelper
    {
        public static string EnvironmentMapper(string env) =>
        env switch
        {
            "dev" => "d",
            "test" => "t",
            "stg" => "s",
            "iat" => "i",
            "uat" => "u",
            "prod-shadow" => "h",
            "pre-prod" => "r",
            "prod" => "p",
            _ => "n"
        };

        /// <summary>True for an AMQP connection string (amqp:// or amqps://): the RabbitMQ case. Same rule as blocks-os (I02-WP7).</summary>
        public static bool IsRabbitMq(string connectionString)
        {
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                return uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                       uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
