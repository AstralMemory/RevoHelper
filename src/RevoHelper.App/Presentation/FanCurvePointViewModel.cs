using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevoHelper.App.Presentation;

public sealed class FanCurvePointViewModel : INotifyPropertyChanged
{
    private int dutyPercent;
    private readonly Action<FanCurvePointViewModel> changed;

    public FanCurvePointViewModel(
        int id,
        int upTemperatureC,
        int downTemperatureC,
        int dutyPercent,
        Action<FanCurvePointViewModel> changed)
    {
        Id = id;
        UpTemperatureC = upTemperatureC;
        DownTemperatureC = downTemperatureC;
        this.dutyPercent = dutyPercent;
        this.changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }

    public int UpTemperatureC { get; }

    public int DownTemperatureC { get; }

    public string TemperatureLabel => $"{UpTemperatureC}℃";

    public int DutyPercent
    {
        get => dutyPercent;
        set
        {
            if (dutyPercent == value)
            {
                return;
            }

            dutyPercent = value;
            PropertyChanged?.Invoke(this, new(nameof(DutyPercent)));
            changed(this);
        }
    }
}
