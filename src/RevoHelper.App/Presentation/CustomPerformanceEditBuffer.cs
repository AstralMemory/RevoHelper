using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

/// <summary>Retains only the latest unsent value for each setting. Used on the UI dispatcher.</summary>
public sealed class CustomPerformanceEditBuffer
{
    private readonly Dictionary<CustomPerformanceSetting, int> edits = [];
    private readonly Queue<CustomPerformanceSetting> order = [];
    public int Count => edits.Count;
    public void Set(CustomPerformanceSetting setting, int value)
    {
        if (!edits.ContainsKey(setting)) order.Enqueue(setting);
        edits[setting] = value;
    }
    public void Clear() { edits.Clear(); order.Clear(); }

    public bool TryTakeNext(out KeyValuePair<CustomPerformanceSetting, int> edit)
    {
        if (edits.Count == 0) { edit = default; return false; }
        var setting = order.Dequeue();
        edit = new(setting, edits[setting]);
        edits.Remove(setting);
        return true;
    }
}
