using System.Windows.Controls;
using System.Windows.Input;
using ADM.Desktop.Presentation;

namespace ADM.Desktop.Views;

public partial class LiveChatView : UserControl
{
    public LiveChatView() => InitializeComponent();
    private void ConversationScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scroll && e.ExtentHeightChange > 0 && scroll.VerticalOffset >= scroll.ScrollableHeight - e.ExtentHeightChange - 40)
            scroll.ScrollToEnd();
    }
    private void ComposerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && DataContext is LiveOperationsViewModel vm)
        { e.Handled = true; vm.SendCommand.Execute(null); }
    }
}
