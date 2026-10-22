using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ADM.Desktop.Presentation;

namespace ADM.Desktop.Views;

public partial class ChatView : UserControl
{
    private DemoSessionViewModel? _subscribed;
    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }
    private void Subscribe()
    {
        Unsubscribe();
        _subscribed = DataContext as DemoSessionViewModel;
        if (_subscribed is not null) _subscribed.Messages.CollectionChanged += MessagesChanged;
    }
    private void Unsubscribe()
    {
        if (_subscribed is not null) _subscribed.Messages.CollectionChanged -= MessagesChanged;
        _subscribed = null;
    }
    private void MessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ChatScroll.ScrollToEnd));
    private void Composer_OnPreviewKeyDown(object sender, KeyEventArgs e) => e.Handled = HandleComposerKey(e.Key, Keyboard.Modifiers);
    internal bool HandleComposerKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Enter || (modifiers & ModifierKeys.Shift) != 0) return false;
        if (DataContext is DemoSessionViewModel demo && demo.SendCommand.CanExecute(null)) demo.SendCommand.Execute(null);
        return true;
    }
}
