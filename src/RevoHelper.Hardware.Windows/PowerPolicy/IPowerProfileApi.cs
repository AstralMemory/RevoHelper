namespace RevoHelper.Hardware.Windows.PowerPolicy;

public interface IPowerProfileApi
{
    Guid GetActiveScheme();
    uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting);
    uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting);
    void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value);
    void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value);
    void SetActiveScheme(Guid scheme);
}
