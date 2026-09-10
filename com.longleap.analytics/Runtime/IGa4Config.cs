namespace LongLeap.Analytics
{
public interface IGa4Config
{
    string MeasurementId { get; }
    string ApiSecret { get; }
    string ClientId { get; }
    string EndpointUrl { get; }
}
}
