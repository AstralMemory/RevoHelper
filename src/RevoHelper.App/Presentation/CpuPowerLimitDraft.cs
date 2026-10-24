using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

internal sealed class CpuPowerLimitDraft(CpuPowerLimitProfile? readback, int pl1Watts, int pl2Watts)
{
    public CpuPowerLimitProfile? Readback { get; } = readback;

    public int Pl1Watts { get; set; } = pl1Watts;

    public int Pl2Watts { get; set; } = pl2Watts;
}
