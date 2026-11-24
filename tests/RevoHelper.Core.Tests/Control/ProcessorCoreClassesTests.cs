using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class ProcessorCoreClassesTests
{
    [Fact]
    public void TwoCoreTypes_HighestClassHoldsThePCores()
    {
        var classes = ProcessorCoreClasses.FromCoreEfficiencyClasses(
            Enumerable.Repeat(1, 8).Concat(Enumerable.Repeat(0, 16)));

        Assert.Equal(new ProcessorCoreClasses(1, 8, 16, EfficiencyClassMask: 0b1), classes);
        Assert.True(classes!.IsHybrid);
        Assert.True(classes.CanLimitSeparately);
        Assert.Equal(new[] { 0 }, classes.EfficiencyClasses);
    }

    [Fact]
    public void ThreeCoreTypes_EveryLowerClassHoldsECores()
    {
        var classes = ProcessorCoreClasses.FromCoreEfficiencyClasses([0, 0, 1, 1, 1, 1, 2, 2]);

        Assert.Equal(new ProcessorCoreClasses(2, 2, 6, EfficiencyClassMask: 0b11), classes);
        Assert.True(classes!.CanLimitSeparately);
        Assert.Equal(new[] { 0, 1 }, classes.EfficiencyClasses);
    }

    [Fact]
    public void ClassesWithoutClass0_KeepTheirActualNumbers()
    {
        var classes = ProcessorCoreClasses.FromCoreEfficiencyClasses([1, 1, 1, 2, 2]);

        Assert.Equal(new ProcessorCoreClasses(2, 2, 3, EfficiencyClassMask: 0b10), classes);
        Assert.True(classes!.CanLimitSeparately);
        Assert.Equal(new[] { 1 }, classes.EfficiencyClasses);
    }

    [Fact]
    public void AClassWithoutAWindowsSetting_CannotBeLimitedSeparately()
    {
        var classes = ProcessorCoreClasses.FromCoreEfficiencyClasses([0, 0, 0, 3, 3]);

        Assert.Equal(3, classes!.PerformanceClass);
        Assert.True(classes.IsHybrid);
        Assert.False(classes.CanLimitSeparately);
    }

    [Fact]
    public void OneCoreType_IsNotHybrid()
    {
        var classes = ProcessorCoreClasses.FromCoreEfficiencyClasses(Enumerable.Repeat(0, 8));

        Assert.Equal(new ProcessorCoreClasses(0, 8, 0, EfficiencyClassMask: 0), classes);
        Assert.False(classes!.IsHybrid);
        Assert.False(classes.CanLimitSeparately);
    }

    [Fact]
    public void NoCores_GivesNoAnswer() =>
        Assert.Null(ProcessorCoreClasses.FromCoreEfficiencyClasses([]));

    [Fact]
    public void ForCoreTypes_WithPCoresInClass1_CapsClass0AsECores() =>
        Assert.Equal(
            new ProcessorFrequencyLimitPolicy(3000, 3000, 4000, 4000, 4000, 4000),
            ProcessorFrequencyLimitPolicy.ForCoreTypes(4000, 3000, performanceClass: 1));

    [Fact]
    public void ForCoreTypes_WithPCoresInClass2_CapsClass0And1AsECores() =>
        Assert.Equal(
            new ProcessorFrequencyLimitPolicy(3000, 3000, 3000, 3000, 4000, 4000),
            ProcessorFrequencyLimitPolicy.ForCoreTypes(4000, 3000, performanceClass: 2));

    [Fact]
    public void ForCoreTypes_WithOneClass_IsUniform() =>
        Assert.Equal(
            ProcessorFrequencyLimitPolicy.Uniform(4000),
            ProcessorFrequencyLimitPolicy.ForCoreTypes(4000, 3000, performanceClass: 0));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ForCoreTypes_RejectsAClassWithoutAWindowsSetting(int performanceClass) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProcessorFrequencyLimitPolicy.ForCoreTypes(4000, 3000, performanceClass));

    [Theory]
    [InlineData(0, 1000u)]
    [InlineData(1, 3000u)]
    [InlineData(2, 5000u)]
    public void AcMaximumMhzForClass_ReadsTheMatchingSetting(int processorClass, uint expected) =>
        Assert.Equal(expected, new ProcessorFrequencyLimitPolicy(1000, 2000, 3000, 4000, 5000, 6000)
            .AcMaximumMhzForClass(processorClass));

    [Fact]
    public void AcMaximumMhzForClass_RejectsAClassWithoutAWindowsSetting() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProcessorFrequencyLimitPolicy.Uniform(3000).AcMaximumMhzForClass(3));

    [Fact]
    public void AcMatchesDc_ComparesEveryClass()
    {
        Assert.True(ProcessorFrequencyLimitPolicy.ForCoreTypes(4000, 3000, performanceClass: 1).AcMatchesDc());
        Assert.False(new ProcessorFrequencyLimitPolicy(3000, 3000, 3000, 3000, 3000, 2000).AcMatchesDc());
    }
}
