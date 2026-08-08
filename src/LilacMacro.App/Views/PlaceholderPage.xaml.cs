using System.Windows.Controls;

namespace LilacMacro.App.Views;

public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage(string pageName)
    {
        InitializeComponent();
        PageNameText.Text = pageName;
    }
}
