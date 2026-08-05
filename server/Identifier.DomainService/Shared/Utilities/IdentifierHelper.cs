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
    }
}
