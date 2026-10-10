using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgyCliDashboard.Views;

public class ModelPickerTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? RowTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return item switch
        {
            ModelPickerHeaderViewModel => HeaderTemplate,
            ModelPickerRowViewModel => RowTemplate,
            _ => base.SelectTemplateCore(item, container)
        };
    }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item switch
        {
            ModelPickerHeaderViewModel => HeaderTemplate,
            ModelPickerRowViewModel => RowTemplate,
            _ => base.SelectTemplateCore(item)
        };
    }
}
