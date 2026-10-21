namespace Zypora;

// 清空文档前是否需要确认:空白内容或新建时的默认(欢迎)内容不打扰用户
public static class ClearPolicy
{
    public static bool NeedsConfirm(string text, string defaultText)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text != defaultText;
    }
}
