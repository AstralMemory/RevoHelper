using System.Buffers.Binary;
using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.PowerPolicy;

namespace RevoHelper.Hardware.Windows.Tests.PowerPolicy;

public sealed class WindowsProcessorCoreClassReaderTests
{
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;
    private const int RecordSize = 48;

    [Fact]
    public void Parse_CountsCoresPerEfficiencyClassAndSkipsOtherRecords()
    {
        var records = Enumerable.Repeat(Record(RelationProcessorCore, 1), 8)
            .Concat([Record(RelationCache, 7)])
            .Concat(Enumerable.Repeat(Record(RelationProcessorCore, 0), 16))
            .SelectMany(record => record)
            .ToArray();

        Assert.Equal(new ProcessorCoreClasses(1, 8, 16), WindowsProcessorCoreClassReader.Parse(records));
    }

    [Fact]
    public void Parse_WithARecordSizeBeyondTheBuffer_GivesNoAnswer()
    {
        var record = Record(RelationProcessorCore, 1);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), RecordSize * 2);

        Assert.Null(WindowsProcessorCoreClassReader.Parse(record));
    }

    [Fact]
    public void Parse_WithATruncatedHeader_GivesNoAnswer() =>
        Assert.Null(WindowsProcessorCoreClassReader.Parse(Record(RelationProcessorCore, 1).Concat(new byte[4]).ToArray()));

    [Fact]
    public void Parse_WithoutCoreRecords_GivesNoAnswer() =>
        Assert.Null(WindowsProcessorCoreClassReader.Parse(Record(RelationCache, 0)));

    [Fact]
    public void TryRead_OnThisMachine_EitherReportsCoresOrNothing()
    {
        var classes = WindowsProcessorCoreClassReader.TryRead();

        Assert.True(classes is null || classes.PerformanceCores > 0);
    }

    private static byte[] Record(int relationship, byte efficiencyClass)
    {
        var record = new byte[RecordSize];
        BinaryPrimitives.WriteInt32LittleEndian(record, relationship);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), RecordSize);
        record[9] = efficiencyClass;
        return record;
    }
}
