using System.Windows;
using System.Windows.Input;

namespace Hatch.Views;

public partial class TextEditorDialog : Window
{
    public bool Saved { get; private set; }
    public string ResultText { get; private set; } = "";

    public TextEditorDialog(string content)
    {
        InitializeComponent();
        EditorTextBox.Text = content;
        Loaded += (_, _) =>
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = 0;
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Saved = true;
        ResultText = EditorTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
