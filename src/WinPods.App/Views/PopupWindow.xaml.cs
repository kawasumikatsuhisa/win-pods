using System.Windows;
using System.Windows.Input;

namespace WinPods.App.Views;

/// <summary>
/// タスクトレイのアイコンから開くバッテリー表示ポップアップ。
/// </summary>
public partial class PopupWindow : Window
{
    private const double ScreenEdgeMargin = 12;

    public PopupWindow()
    {
        InitializeComponent();
    }

    /// <summary>画面の作業領域の右下 (通知領域の上あたり) に表示する。</summary>
    public void ShowNearTray()
    {
        // Show する前は ActualWidth / ActualHeight が確定しないため、
        // いったん透明のまま表示してレイアウトを確定させてから位置を決める。
        // (Show 前に Measure(無限大) を呼ぶとウィンドウ幅が内容に合わせて縮んでしまう)
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        UpdateLayout();
        MoveToTrayCorner();

        Opacity = 1;
        Activate();
    }

    private void MoveToTrayCorner()
    {
        Rect workArea = SystemParameters.WorkArea;

        Left = workArea.Right - ActualWidth - ScreenEdgeMargin;
        Top = workArea.Bottom - ActualHeight - ScreenEdgeMargin;
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
