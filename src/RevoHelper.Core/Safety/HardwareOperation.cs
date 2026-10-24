namespace RevoHelper.Core.Safety;

public enum HardwareOperation
{
    ReadIdentity,
    ReadSensors,
    SwitchFirmwareProfile,
    WritePowerLimits,
    WriteFanCurve,
    SetDirectFanDuty,
    SetGpuMode,
    SetRgb,
    ControlLiquidCooling
}
