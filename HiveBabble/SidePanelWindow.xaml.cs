using System.Windows;
using System.Windows.Interop;
namespace HiveBabble;
public partial class SidePanelWindow : Window
{
    public SidePanelWindow(Window owner)
    {
        Owner = owner; InitializeComponent();
        SourceInitialized += (_, _) => Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }
    public void ShowResult(string text, string status) { ResultBox.Text = text; StatusLine.Text = status; }
    void Copy_Click(object s, RoutedEventArgs e) { if (ResultBox.Text.Length > 0) { Clipboard.SetText(ResultBox.Text); StatusLine.Text = "Copied."; } }
    void Hide_Click(object s, RoutedEventArgs e) => Hide();
}