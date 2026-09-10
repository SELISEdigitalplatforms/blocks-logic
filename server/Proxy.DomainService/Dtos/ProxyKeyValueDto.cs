namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// A header / query / body-merge pair as returned to the console. The console derives the
    /// "uses a configuration variable" badge from <see cref="Value"/> itself (the same <c>{{$VAR.name}}</c>
    /// regex it uses for the hand-type affordance).
    /// </summary>
    public sealed class ProxyKeyValueDto
    {
        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}
