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
    public void ShowResult(string text, string status) { ResultBox.Text = text; StatusLine.Text = status; CopyButton.IsEnabled = text.Length > 0; }
    public bool CopyResult()
    {
        if (ResultBox.Text.Length == 0) return false;
        try { Clipboard.SetText(ResultBox.Text); StatusLine.Text = "Copied to clipboard."; return true; }
        catch (System.Runtime.InteropServices.COMException) { StatusLine.Text = "Clipboard is busy. Try Copy again."; return false; }
    }
    void Copy_Click(object s, RoutedEventArgs e) => CopyResult();
    void Hide_Click(object s, RoutedEventArgs e) => Hide();
}
