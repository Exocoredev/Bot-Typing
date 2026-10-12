using System.Windows;

namespace AutoInputTester.Windows;

public partial class PreviewWindow : Window
{
    public string ResultText => TextBox.Text;

    public PreviewWindow(string text)
    {
        InitializeComponent();
        TextBox.Text = text;
        TextBox.CaretIndex = TextBox.Text.Length;
        UpdateCount();
    }

    private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        if (CountText is not null && TextBox is not null)
            CountText.Text = $"Erkannt: {TextBox.Text.Length} Zeichen";
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
