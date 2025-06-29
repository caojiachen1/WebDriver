using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WebDriver.ViewModels;

namespace WebDriver.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Find the ListBox and add double-click handler
        var listBox = this.FindControl<ListBox>("FileListBox");
        if (listBox != null)
        {
            listBox.DoubleTapped += OnFileListDoubleClick;
        }
    }

    private void OnFileListDoubleClick(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.SelectedItem != null)
        {
            viewModel.NavigateToItemCommand.Execute(null);
        }
    }
}