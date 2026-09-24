namespace Zypora;

// 撤销分组策略:连续输入合并为一步,命令各成一步。
// 计时/按键边界由 MainWindow 负责调用 Commit()。
public sealed class UndoCoordinator
{
    private readonly UndoHistory _history = new();
    private bool _pending;
    private string _text = "";
    private int _caret;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public void Reset(string text, int caret)
    {
        _history.Reset(text, caret);
        _text = text;
        _caret = caret;
        _pending = false;
    }

    // 连续输入:标记待提交(不立即入栈)
    public void Change(string text, int caret)
    {
        _text = text;
        _caret = caret;
        _pending = true;
    }

    // 组边界(停顿 / 空格 / 回车):提交当前输入组
    public void Commit()
    {
        if (!_pending) return;
        _history.Push(_text, _caret);
        _pending = false;
    }

    // 命令:先收尾输入组,命令结果自成一步
    public void Command(string text, int caret)
    {
        Commit();
        _history.Push(text, caret);
        _text = text;
        _caret = caret;
        _pending = false;
    }

    public (string Text, int Caret)? Undo(string liveText, int liveCaret)
    {
        _text = liveText;
        _caret = liveCaret;
        _pending = true;
        Commit();
        return _history.Undo();
    }

    public (string Text, int Caret)? Redo()
    {
        Commit();
        return _history.Redo();
    }
}
