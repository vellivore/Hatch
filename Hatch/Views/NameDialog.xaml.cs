using System.Windows;

namespace Hatch.Views;

public partial class NameDialog : Window
{
    public string ResultName { get; private set; } = "";

    public NameDialog(string defaultName = "")
    {
        InitializeComponent();
        NameTextBox.Text = defaultName;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("名前を入力してください。", "Hatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ResultName = name;
        DialogResult = true;
        Close();
    }
}
