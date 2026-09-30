using System.Windows;
using System.Windows.Input;

namespace Hatch.Views;

public enum SwitchConfirmChoice
{
    Cancel,
    ApplyAndSwitch,
    DiscardAndSwitch,
}

/// <summary>
/// 未保存の変更があるときにプリセットを切り替える前の確認（3 択）。
/// MessageBox はボタン名を変えられないため専用ダイアログにしている。
/// </summary>
public partial class ConfirmSwitchDialog : Window
{
    public SwitchConfirmChoice Choice { get; private set; } = SwitchConfirmChoice.Cancel;

    public ConfirmSwitchDialog(string targetDisplayName)
    {
        InitializeComponent();
        MessageText.Text =
            $"「{targetDisplayName}」に切り替える前に、一覧の未保存の変更をどうしますか？\n\n" +
            "適用して切替: 変更を hosts に保存してから切り替えます\n" +
            "破棄して切替: 変更を捨て、hosts を読み直してから切り替えます";
    }

    private void ApplyAndSwitch_Click(object sender, RoutedEventArgs e) => Finish(SwitchConfirmChoice.ApplyAndSwitch);

    private void DiscardAndSwitch_Click(object sender, RoutedEventArgs e) => Finish(SwitchConfirmChoice.DiscardAndSwitch);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(SwitchConfirmChoice.Cancel);

    private void Finish(SwitchConfirmChoice choice)
    {
        Choice = choice;
        DialogResult = choice != SwitchConfirmChoice.Cancel;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
