using System.Windows;
using System.Windows.Input;

namespace WinPods.App.Views;

/// <summary>
/// タスクトレイのアイコンから開くバッテリー表示ポップアップ。
/// </summary>
public partial class PopupWindow : Window
{
    public PopupWindow()
    {
        InitializeComponent();
    }

    /// <summary>画面の作業領域の右下 (通知領域の上あたり) に表示する。</summary>
    public void ShowNearTray()
    {
        // 実測サイズを得るために一度レイアウトを確定させる。
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        const double margin = 12;
        Rect workArea = SystemParameters.WorkArea;

        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - (ActualHeight > 0 ? ActualHeight : DesiredSize.Height) - margin;

        Show();
        Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // フォーカスが外れたら閉じる (Windows の通知領域のポップアップと同じ挙動)。
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }
}
