namespace Zypora;

public sealed class UndoHistory
{
    private readonly List<(string Text, int Caret)> _states = new();
    private int _pos = -1;

    public bool CanUndo => _pos > 0;
    public bool CanRedo => _pos >= 0 && _pos < _states.Count - 1;

    public void Reset(string text, int caret)
    {
        _states.Clear();
        _states.Add((text, caret));
        _pos = 0;
    }

    public void Push(string text, int caret)
    {
        if (_pos >= 0 && _states[_pos].Text == text)
        {
            _states[_pos] = (text, caret);
            return;
        }

        if (_pos < _states.Count - 1)
        {
            _states.RemoveRange(_pos + 1, _states.Count - _pos - 1);
        }

        _states.Add((text, caret));
        _pos = _states.Count - 1;

        if (_states.Count > 200)
        {
            _states.RemoveAt(0);
            _pos--;
        }
    }

    public (string Text, int Caret)? Undo()
    {
        if (!CanUndo) return null;
        _pos--;
        return _states[_pos];
    }

    public (string Text, int Caret)? Redo()
    {
        if (!CanRedo) return null;
        _pos++;
        return _states[_pos];
    }
}
