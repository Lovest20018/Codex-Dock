using CodexConfig.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class HelpPage : UserControl
{
    private bool filtering = true;
    private bool navigationEnabled = true;

    public HelpPage()
    {
        InitializeComponent();
        HelpTopicsInput.ItemsSource = DockHelpCatalog.Topics;
        HelpTopicsInput.SelectedIndex = 0;
        filtering = false;
        RenderTopic();
    }

    internal event Action<string>? DestinationRequested;

    internal void SetNavigationEnabled(bool enabled)
    {
        navigationEnabled = enabled;
        HelpTopicActionButton.IsEnabled = enabled && HelpTopicsInput.SelectedItem is DockHelpTopic;
    }

    private void HelpSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (filtering) return;
        var current = HelpTopicsInput.SelectedItem as DockHelpTopic;
        var matches = DockHelpCatalog.Search(sender.Text);
        filtering = true;
        HelpTopicsInput.ItemsSource = matches;
        HelpTopicsInput.SelectedItem = matches.FirstOrDefault(topic => topic.Id == current?.Id) ?? matches.FirstOrDefault();
        filtering = false;
        RenderTopic();
    }

    private void HelpTopicChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!filtering) RenderTopic();
    }

    private void RenderTopic()
    {
        var topic = HelpTopicsInput.SelectedItem as DockHelpTopic;
        HelpEmptyText.Visibility = topic is null ? Visibility.Visible : Visibility.Collapsed;
        HelpTopicCard.Visibility = topic is null ? Visibility.Collapsed : Visibility.Visible;
        HelpTopicTitle.Text = topic?.Title ?? "";
        HelpTopicText.Text = topic?.Text ?? "";
        HelpTopicActionButton.Content = topic?.Action ?? "";
        HelpTopicActionButton.IsEnabled = navigationEnabled && topic is not null;
    }

    private void HelpTopicActionClicked(object sender, RoutedEventArgs args) => NavigateToTopic();

    internal void NavigateToTopic()
    {
        if (navigationEnabled && HelpTopicsInput.SelectedItem is DockHelpTopic topic)
            DestinationRequested?.Invoke(topic.Destination);
    }
}
