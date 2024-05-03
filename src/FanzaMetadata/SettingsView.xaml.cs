using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;

namespace FanzaMetadata;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void AddTagFilter_Click(object sender, RoutedEventArgs e)
    {
        var settings = DataContext as Settings;
        settings?.TagFilter.Add(new Regex(TxtNewInput.Text));
        TagFilter.Items.Refresh();
    }

    private void RemoveTagFilter_Click(object sender, RoutedEventArgs e)
    {
        var settings = DataContext as Settings;
        if (TagFilter.SelectedItem is not Regex selected) return;

        settings?.TagFilter.Remove(selected);
        TagFilter.Items.Refresh();
    }
}
