using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace RevoHelper.App.Presentation;

/// <summary>
/// The circled question mark beside a setting; hovering it or reaching it with Tab shows the
/// explanation. A plain Border has no automation peer, so a screen reader found only the "?" inside.
/// This one is a single item named for its setting (AutomationProperties.Name, the row title), read
/// as "说明" / "Help", with the explanation as its help text: AutomationProperties.HelpText when set,
/// else every line of the tooltip.
/// </summary>
public sealed class HelpMark : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new HelpMarkAutomationPeer(this);

    /// <summary>
    /// The words of a tooltip, one line per text block. Text bound to the window's data through the
    /// tooltip's placement target is empty until the tooltip first opens, so such marks set
    /// AutomationProperties.HelpText themselves.
    /// </summary>
    public static string TooltipText(object? tooltip) => tooltip switch
    {
        string text => text,
        DependencyObject element => string.Join("\n", Lines(element)),
        _ => "",
    };

    private static IEnumerable<string> Lines(DependencyObject element)
    {
        if (element is TextBlock block)
        {
            if (!string.IsNullOrWhiteSpace(block.Text)) yield return block.Text;
            yield break;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            foreach (var line in Lines(child))
                yield return line;
    }

    private sealed class HelpMarkAutomationPeer(HelpMark owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(HelpMark);

        protected override string GetLocalizedControlTypeCore() => UiLanguageService.T("说明");

        protected override string GetHelpTextCore()
        {
            var help = AutomationProperties.GetHelpText(Owner);
            return string.IsNullOrEmpty(help) ? TooltipText(((HelpMark)Owner).ToolTip) : help;
        }

        // The "?" says nothing on its own; the name and the help text carry it.
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
