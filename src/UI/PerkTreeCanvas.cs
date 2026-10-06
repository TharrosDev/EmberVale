using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The grid one perk branch is drawn on: a node per perk at its (tier, column), a gate label per tier in the
/// left gutter, and a connector from each prerequisite down to the perk it opens. A <see cref="Container"/>
/// rather than a <see cref="GridContainer"/> because the connectors need each node's rectangle, and a
/// container draws under its children, so the lines run behind the nodes they join.
/// </summary>
public sealed partial class PerkTreeCanvas : Container
{
    public const float GutterWidth = 64f;
    public const float CompactGutterWidth = 46f;
    public const float NodeHeight = 54f;
    public const float RowGap = 14f;
    public const float ColumnGap = 10f;
    public const float MinNodeWidth = 100f;
    public const float MaxNodeWidth = 260f;

    private readonly int _columns;
    private readonly int _rows;
    private readonly float _gutter;
    private readonly Dictionary<string, (Control Node, int Tier, int Column)> _nodes = new();
    private readonly Dictionary<string, Rect2> _rects = new();
    private readonly List<(Control Label, int Tier)> _gates = new();
    private readonly List<(string From, string To, bool Lit)> _edges = new();
    private readonly HashSet<(string From, string To)> _path = new();

    public PerkTreeCanvas(int columns, int rows, float gutter = GutterWidth)
    {
        _columns = columns;
        _rows = rows;
        _gutter = gutter;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    /// <summary>Places <paramref name="node"/> at a tier (1 is the top row) and column (0 is the left).</summary>
    public void AddNode(string id, Control node, int tier, int column)
    {
        _nodes[id] = (node, tier, column);
        AddChild(node);
    }

    /// <summary>Puts a requirement label for a tier row in the gutter.</summary>
    public void AddGate(Control label, int tier)
    {
        _gates.Add((label, tier));
        AddChild(label);
    }

    /// <summary>Joins a prerequisite to the perk it opens; <paramref name="lit"/> when the prerequisite is learned.</summary>
    public void AddEdge(string from, string to, bool lit) => _edges.Add((from, to, lit));

    /// <summary>The lines to draw hot: the route from owned perks to the focused one
    /// (<see cref="PerkTreeRules.PathTo"/>). Redraws only when the route changed.</summary>
    public void SetPath(IReadOnlyCollection<(string From, string To)> path)
    {
        if (_path.Count == path.Count && _path.IsSupersetOf(path))
        {
            return;
        }

        _path.Clear();
        _path.UnionWith(path);
        QueueRedraw();
    }

    public override Vector2 _GetMinimumSize() => new(
        _gutter + (_columns * (MinNodeWidth + ColumnGap)),
        (_rows * NodeHeight) + (System.Math.Max(0, _rows - 1) * RowGap));

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren)
        {
            return;
        }

        float pitch = Mathf.Min((Size.X - _gutter) / _columns, MaxNodeWidth + ColumnGap);
        float width = pitch - ColumnGap;
        _rects.Clear();

        foreach ((string id, (Control node, int tier, int column)) in _nodes)
        {
            var rect = new Rect2(
                _gutter + (column * pitch), RowTop(tier), width, NodeHeight);
            FitChildInRect(node, rect);
            _rects[id] = rect;
        }

        foreach ((Control label, int tier) in _gates)
        {
            FitChildInRect(label, new Rect2(0f, RowTop(tier), _gutter - ColumnGap, NodeHeight));
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        // Unlit first so a lit line that shares a segment with one is never painted over.
        foreach (bool lit in new[] { false, true })
        {
            foreach ((string from, string to, bool edgeLit) in _edges)
            {
                if (edgeLit == lit && _rects.TryGetValue(from, out Rect2 top) && _rects.TryGetValue(to, out Rect2 bottom))
                {
                    DrawConnector(top, bottom, lit ? UiTheme.BrassLit : UiTheme.Iron, lit ? 2f : 1.5f);
                }
            }
        }

        // The focused perk's route last and widest, with the heat under it: it is the one line the eye
        // should be able to follow from the top of the tree.
        foreach ((string from, string to, bool _) in _edges)
        {
            if (_path.Contains((from, to)) && _rects.TryGetValue(from, out Rect2 top) && _rects.TryGetValue(to, out Rect2 bottom))
            {
                DrawConnector(top, bottom, UiTheme.EmberGlow, 6f);
                DrawConnector(top, bottom, UiTheme.Accent, 3f);
            }
        }
    }

    private void DrawConnector(Rect2 from, Rect2 to, Color color, float width)
    {
        Vector2 start = new(from.GetCenter().X, from.End.Y);
        Vector2 end = new(to.GetCenter().X, to.Position.Y);
        if (end.Y <= start.Y)
        {
            return; // a prerequisite on the same row or below has no downward line to draw
        }

        float bend = end.Y - (RowGap * 0.5f);
        Vector2[] points;
        if (end.Y - start.Y > RowGap + 1f)
        {
            // The line skips a row: run it down the gap beside the prerequisite's column rather than through
            // the nodes of the rows it passes, where it would read as a link to the perk it hides behind.
            float lane = from.End.X + (ColumnGap * 0.5f);
            float step = start.Y + (RowGap * 0.5f);
            points = new[] { start, new Vector2(start.X, step), new Vector2(lane, step), new Vector2(lane, bend), new Vector2(end.X, bend), end };
        }
        else
        {
            points = new[] { start, new Vector2(start.X, bend), new Vector2(end.X, bend), end };
        }

        DrawPolyline(points, color, width);
    }

    private static float RowTop(int tier) => (Mathf.Max(1, tier) - 1) * (NodeHeight + RowGap);
}
