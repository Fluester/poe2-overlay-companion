using System.Windows;
using System.Windows.Input;
using Poe2OverlayCompanion.Localization;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion;

public partial class PassiveTreeWindow : Window
{
    public PassiveTreeWindow(string title, PassiveDiffResult? diff, int? weaponSetFilter, string? activeClassName = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        Title = $"{Loc.T("Дерево пасивок", "Passive Tree", "Дерево пассивок")} — {title}";
        BtnFit.Content = Loc.T("⌖ Центр", "⌖ Fit", "⌖ Центр");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            FullTreeCanvas.SetWeaponSetFilter(weaponSetFilter);
            FullTreeCanvas.SetTreeData(diff, preserveViewport: false, activeClassName: activeClassName);
        };
        Closed += (_, _) => FullTreeCanvas.ReleaseCachedTexture();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => FullTreeCanvas.ZoomBy(1.3);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => FullTreeCanvas.ZoomBy(1.0 / 1.3);
    private void Fit_Click(object sender, RoutedEventArgs e) => FullTreeCanvas.FitToActive();
}
