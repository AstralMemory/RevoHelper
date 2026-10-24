namespace RevoHelper.Hardware.Windows.Discovery;

public interface IWmiQuery
{
    IReadOnlyDictionary<string, string> ReadFirst(string className);

    IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className);
}
