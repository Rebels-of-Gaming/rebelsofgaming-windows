namespace RoG.Core.Features.Control;

/// <summary>
/// Befehls-Historie der Konsole wie in einer Shell: ↑ geht zurück, ↓ vor, ganz unten ist die leere Eingabe.
/// Gleiche Befehle direkt hintereinander werden nur einmal gemerkt.
/// </summary>
public sealed class CommandHistory(int capacity = 100)
{
    private readonly List<string> _items = [];
    private int _position;

    public IReadOnlyList<string> Items => _items;

    public void Add(string command)
    {
        var line = command.Trim();
        if (line.Length > 0 && (_items.Count == 0 || _items[^1] != line))
        {
            _items.Add(line);
            if (_items.Count > capacity)
            {
                _items.RemoveAt(0);
            }
        }

        _position = _items.Count;
    }

    /// <summary>Vorheriger Befehl (↑); am Anfang bleibt es beim ältesten.</summary>
    public string? Previous()
    {
        if (_items.Count == 0)
        {
            return null;
        }

        _position = Math.Max(0, _position - 1);
        return _items[_position];
    }

    /// <summary>Nächster Befehl (↓); hinter dem neuesten kommt die leere Eingabe.</summary>
    public string Next()
    {
        _position = Math.Min(_items.Count, _position + 1);
        return _position < _items.Count ? _items[_position] : string.Empty;
    }
}
