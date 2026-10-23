using System.Globalization;
using System.Windows.Data;
using ADM.Conversation;

namespace ADM.Desktop.Presentation;

public sealed class QuickChatLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string command ? QuickChat.Label(command) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
