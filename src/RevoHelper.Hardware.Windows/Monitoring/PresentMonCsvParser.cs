using System.Globalization;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.Hardware.Windows.Monitoring;

/// <summary>Parses the explicitly requested PresentMon --v1_metrics CSV without allocating a field array per frame.</summary>
public sealed class PresentMonCsvParser
{
    private int processColumn = -1;
    private int swapChainColumn = -1;
    private int timeColumn = -1;
    private int intervalColumn = -1;
    private int columns;

    public bool HasHeader => processColumn >= 0 && swapChainColumn >= 0 && timeColumn >= 0 && intervalColumn >= 0;

    public bool TryRead(string line, out PresentedFrame frame)
    {
        frame = default;
        if (line.Length is 0 or > 8192)
        {
            return false;
        }
        if (line.AsSpan().TrimStart('\uFEFF').StartsWith("Application,", StringComparison.OrdinalIgnoreCase) ||
            line.AsSpan().TrimStart('\uFEFF').StartsWith("\"Application\",", StringComparison.OrdinalIgnoreCase))
        {
            ReadHeader(line.TrimStart('\uFEFF'));
            return false;
        }
        if (!HasHeader)
        {
            return false;
        }

        int processId = 0;
        ulong swapChain = 0;
        double seconds = double.NaN, milliseconds = double.NaN;
        var cursor = new CsvCursor(line);
        var index = 0;
        while (cursor.TryNext(out var field))
        {
            if (index == processColumn && !int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out processId))
            {
                return false;
            }
            if (index == swapChainColumn)
            {
                var hex = field.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? field[2..] : field;
                if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out swapChain))
                {
                    return false;
                }
            }
            if (index == timeColumn && !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            {
                return false;
            }
            if (index == intervalColumn && !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out milliseconds))
            {
                return false;
            }
            index++;
        }
        if (!cursor.Valid || index != columns || processId <= 0 || !double.IsFinite(seconds) || seconds < 0 ||
            !double.IsFinite(milliseconds) || milliseconds <= 0)
        {
            return false;
        }
        frame = new(processId, swapChain, seconds, milliseconds);
        return true;
    }

    private void ReadHeader(string line)
    {
        processColumn = swapChainColumn = timeColumn = intervalColumn = -1;
        var cursor = new CsvCursor(line);
        var index = 0;
        while (cursor.TryNext(out var field))
        {
            if (field.Equals("ProcessID", StringComparison.OrdinalIgnoreCase)) processColumn = index;
            if (field.Equals("SwapChainAddress", StringComparison.OrdinalIgnoreCase)) swapChainColumn = index;
            if (field.Equals("TimeInSeconds", StringComparison.OrdinalIgnoreCase)) timeColumn = index;
            if (field.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase)) intervalColumn = index;
            index++;
        }
        columns = index;
        if (!cursor.Valid)
        {
            processColumn = -1;
        }
    }

    private ref struct CsvCursor(ReadOnlySpan<char> line)
    {
        private readonly ReadOnlySpan<char> line = line;
        private int offset;
        private bool done;
        public bool Valid { get; private set; } = true;

        public bool TryNext(out ReadOnlySpan<char> field)
        {
            field = default;
            if (done) return false;
            var start = offset;
            if (offset < line.Length && line[offset] == '"')
            {
                start = ++offset;
                while (offset < line.Length)
                {
                    if (line[offset++] != '"') continue;
                    if (offset < line.Length && line[offset] == '"')
                    {
                        offset++;
                        continue;
                    }
                    field = line[start..(offset - 1)];
                    if (offset == line.Length) done = true;
                    else if (line[offset++] != ',') done = !(Valid = false);
                    return true;
                }
                done = true;
                Valid = false;
                return false;
            }
            while (offset < line.Length && line[offset] != ',') offset++;
            field = line[start..offset].Trim();
            if (offset == line.Length) done = true;
            else offset++;
            return true;
        }
    }
}
