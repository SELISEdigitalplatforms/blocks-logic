namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Raw <c>{ key, value }</c> pair as submitted by the console on Create / Update. The value is stored
    /// verbatim; a <c>{{$VAR.name}}</c> token in it is resolved on the fly by the forwarder, never at save
    /// time.
    /// </summary>
    public sealed class ProxyKeyValueInputDto
    {
        public string? Key { get; set; }

        public string? Value { get; set; }
    }
}
