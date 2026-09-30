using System.Windows;
using System.Windows.Input;

namespace Hatch.Views;

/// <summary>
/// ボタン名を指定できる 2 択の確認ダイアログ（MessageBox はボタン名を変えられないため）。
/// 既定ボタンは安全側のキャンセル。
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string message, string okText, string cancelText = "キャンセル")
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Content = cancelText;
    }

    /// <summary>確認を表示し、OK 側が押されたら true。</summary>
    public static bool Ask(Window owner, string message, string okText, string cancelText = "キャンセル")
        => new ConfirmDialog(message, okText, cancelText) { Owner = owner }.ShowDialog() == true;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
