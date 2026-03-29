namespace Sl4n;

public interface ITransport
{
    void Log(IReadOnlyDictionary<string, object?> entry);
}
