namespace BetterWindows.Core.Desktop;

/// <summary>A cell of the desktop icon grid (0-based column and row).</summary>
public readonly record struct GridCell(int Column, int Row);

/// <summary>
/// Pure geometry of the desktop icon grid for ONE monitor. All lengths are DIPs of the full-monitor desktop surface. A cell is
/// <c>(iconSize + 2*PaddingX) x (iconSize + labelHeight + 2*PaddingY)</c>; the grid starts <see cref="Inset"/> inside the work area (so the menu bar / dock AppBars are
/// respected) and holds as many whole cells as fit. Icon positions on the wire are (column,row); the UI renders cell (c,r) with its top-left at <see cref="CellOrigin"/>.
/// Free-cell search fills COLUMNS top-to-bottom, then moves right (the Explorer/Finder default).
/// </summary>
public sealed class DesktopGrid
{
    public const int DefaultLabelHeight = 36;
    public const double PaddingX = 20;
    public const double PaddingY = 6;
    public const double Inset = 8;

    private readonly double _originX, _originY;

    /// <param name="iconSize">Icon edge in DIPs (settings.desktop.iconSize, 32..128; out-of-range values are clamped).</param>
    /// <param name="area">The area the grid may occupy (work area, in surface DIPs).</param>
    /// <param name="labelHeight">Height reserved for the (up to two line) label under the icon.</param>
    public DesktopGrid(int iconSize, DipRect area, int labelHeight = DefaultLabelHeight)
    {
        IconSize = Math.Clamp(iconSize, 16, 256);
        LabelHeight = Math.Clamp(labelHeight, 0, 128);
        CellWidth = IconSize + 2 * PaddingX;
        CellHeight = IconSize + LabelHeight + 2 * PaddingY;
        _originX = area.X + Inset;
        _originY = area.Y + Inset;
        var usableW = double.IsFinite(area.Width) ? area.Width - 2 * Inset : 0;
        var usableH = double.IsFinite(area.Height) ? area.Height - 2 * Inset : 0;
        Columns = (int)Math.Max(1, Math.Floor(usableW / CellWidth));
        Rows = (int)Math.Max(1, Math.Floor(usableH / CellHeight));
    }

    public int IconSize { get; }
    public int LabelHeight { get; }
    public double CellWidth { get; }
    public double CellHeight { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int CellCount => Columns * Rows;

    /// <summary>Top-left corner of a cell in surface DIPs.</summary>
    public (double X, double Y) CellOrigin(GridCell c) => (_originX + c.Column * CellWidth, _originY + c.Row * CellHeight);

    public bool Contains(GridCell c) => c.Column >= 0 && c.Column < Columns && c.Row >= 0 && c.Row < Rows;

    /// <summary>The nearest in-grid cell (an icon dragged outside the work area lands on the edge cell).</summary>
    public GridCell Clamp(GridCell c) => new(Math.Clamp(c.Column, 0, Columns - 1), Math.Clamp(c.Row, 0, Rows - 1));

    /// <summary>
    /// Converts the top-left pixel of a dragged icon to the nearest cell (snapping) and clamps it into the grid. Non-finite input maps to the first cell.
    /// </summary>
    public GridCell PixelToCell(double x, double y)
    {
        var c = double.IsFinite(x) ? (int)Math.Clamp(Math.Round((x - _originX) / CellWidth, MidpointRounding.AwayFromZero), -1_000_000, 1_000_000) : 0;
        var r = double.IsFinite(y) ? (int)Math.Clamp(Math.Round((y - _originY) / CellHeight, MidpointRounding.AwayFromZero), -1_000_000, 1_000_000) : 0;
        return Clamp(new GridCell(c, r));
    }

    /// <summary>The first cell that is not in <paramref name="occupied"/>, scanning columns top-to-bottom then left-to-right; null when the grid is full.</summary>
    public GridCell? FirstFree(ISet<GridCell> occupied)
    {
        for (var col = 0; col < Columns; col++)
            for (var row = 0; row < Rows; row++)
            {
                var c = new GridCell(col, row);
                if (!occupied.Contains(c)) return c;
            }
        return null;
    }

    /// <summary>The free cell closest to <paramref name="wanted"/> (Euclidean in cell units; ties: lower column, then lower row); null when the grid is full.</summary>
    public GridCell? NearestFree(GridCell wanted, ISet<GridCell> occupied)
    {
        var w = Clamp(wanted);
        if (!occupied.Contains(w)) return w;
        GridCell? best = null;
        long bestDist = long.MaxValue;
        for (var col = 0; col < Columns; col++)
            for (var row = 0; row < Rows; row++)
            {
                var c = new GridCell(col, row);
                if (occupied.Contains(c)) continue;
                long dc = col - w.Column, dr = row - w.Row;
                var d = dc * dc + dr * dr;
                if (d < bestDist) { bestDist = d; best = c; }
            }
        return best;
    }
}
