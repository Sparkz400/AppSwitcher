namespace AppSwitcher;

public partial class OpenWindowPicker : Window
{
    private readonly OpenWindowPickerViewModel _viewModel;
    internal OpenWindowPick? Result { get; private set; }

    internal OpenWindowPicker(OpenWindowPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        viewModel.Completed += OnCompleted;
        Loaded += async (_, _) => { SearchBox.Focus(); await viewModel.RefreshCommand.ExecuteAsync(null); };
        Closed += (_, _) => { viewModel.Completed -= OnCompleted; viewModel.Dispose(); };
    }

    private void OnCompleted(OpenWindowPick result) { Result = result; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
