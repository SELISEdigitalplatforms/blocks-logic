namespace SeliseBlocks.ConfigurationDriver;

public class MongoDbConfigurationOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string CollectionName { get; set; } = "Secrets";
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, a missing document or unreachable database will not throw — it will silently yield no values.
    /// </summary>
    public bool Optional { get; set; } = false;
}
