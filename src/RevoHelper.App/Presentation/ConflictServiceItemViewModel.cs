using System.ComponentModel;
using System.Runtime.CompilerServices;
using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Presentation;

public sealed class ConflictServiceItemViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public ConflictServiceItemViewModel(ConflictServiceStatus status)
    {
        ServiceName = status.ServiceName;
        DisplayName = status.DisplayName;
        IsRunning = status.IsRunning;
        IsInstalled = status.IsInstalled;
        StatusText = status.StatusText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ServiceName { get; }
    public string DisplayName { get; }
    public bool IsRunning { get; }
    public bool IsInstalled { get; }
    public string StatusText { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
