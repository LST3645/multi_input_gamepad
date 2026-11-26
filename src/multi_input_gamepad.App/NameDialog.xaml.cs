using System.IO;
using System.Windows;

namespace MultiInputGamepad.App;

/// <summary>配置重命名输入框：校验空名、非法字符与重名。</summary>
public partial class NameDialog : Window
{
    private readonly string _original;
    private readonly IReadOnlyCollection<string> _existing;

    /// <summary>确定后的新名称（取消时为 null）。</summary>
    public string? ResultName { get; private set; }

    public NameDialog(string initial, IReadOnlyCollection<string> existing)
    {
        InitializeComponent();
        _original = initial;
        _existing = existing;

        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            Fail("名称不能为空。");
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Fail("名称包含非法字符（\\ / : * ? \" < > |）。");
            return;
        }

        bool duplicate = _existing.Any(existing =>
            !string.Equals(existing, _original, StringComparison.CurrentCultureIgnoreCase) &&
            string.Equals(existing, name, StringComparison.CurrentCultureIgnoreCase));

        if (duplicate)
        {
            Fail("已存在同名配置。");
            return;
        }

        ResultName = name;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
