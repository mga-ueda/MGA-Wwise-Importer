namespace MgaWwiseImporter.UI;

/// <summary>
/// ProcessCmdKey を親フォームに先渡しする RichTextBox。
/// 既定の RTF は Ctrl+Home/End などを自己消費するため、ショートカットがフォームまで届かない。
/// </summary>
internal sealed class ShortcutForwardingRichTextBox : RichTextBox
{
    public Func<Keys, bool>? ShortcutHandler { get; set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ShortcutHandler?.Invoke(keyData) == true)
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
