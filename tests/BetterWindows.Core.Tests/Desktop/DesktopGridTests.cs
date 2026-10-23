using BetterWindows.Core.Desktop;

namespace BetterWindows.Core.Tests.Desktop;

public class DesktopGridTests
{
    private static readonly DipRect Hd = new(0, 0, 1920, 1040);

    [Fact]
    public void A_1080p_work_area_with_64px_icons_holds_18_columns_and_9_rows()
    {
        var g = new DesktopGrid(64, Hd);
        g.CellWidth.Should().Be(104);   // icon + 2 x 20 padding
        g.CellHeight.Should().Be(112);  // icon + 36 label + 2 x 6 padding
        g.Columns.Should().Be(18);
        g.Rows.Should().Be(9);
        g.CellCount.Should().Be(162);
    }

    [Fact]
    public void Bigger_icons_mean_fewer_cells()
    {
        var g = new DesktopGrid(128, Hd);
        g.Columns.Should().Be(11);
        g.Rows.Should().Be(5);
    }

    [Fact]
    public void Smaller_icons_mean_more_cells()
    {
        var small = new DesktopGrid(32, Hd);
        var big = new DesktopGrid(64, Hd);
        small.CellCount.Should().BeGreaterThan(big.CellCount);
    }

    [Fact]
    public void The_grid_starts_inset_inside_the_work_area_so_app_bars_are_respected()
    {
        var g = new DesktopGrid(64, new DipRect(0, 28, 1920, 1000));
        g.CellOrigin(new GridCell(0, 0)).Should().Be((8.0, 36.0));
        g.CellOrigin(new GridCell(2, 1)).Should().Be((8.0 + 2 * 104, 36.0 + 112));
    }

    [Fact]
    public void Pixel_to_cell_is_the_inverse_of_cell_origin_for_every_cell()
    {
        var g = new DesktopGrid(64, Hd);
        for (var c = 0; c < g.Columns; c++)
            for (var r = 0; r < g.Rows; r++)
            {
                var (x, y) = g.CellOrigin(new GridCell(c, r));
                g.PixelToCell(x, y).Should().Be(new GridCell(c, r));
            }
    }

    [Fact]
    public void Pixel_to_cell_snaps_to_the_nearest_cell()
    {
        var g = new DesktopGrid(64, Hd);
        var (x, y) = g.CellOrigin(new GridCell(3, 2));
        g.PixelToCell(x + 30, y + 20).Should().Be(new GridCell(3, 2));
        g.PixelToCell(x + 60, y + 70).Should().Be(new GridCell(4, 3));
    }

    [Theory]
    [InlineData(-5000, -5000, 0, 0)]
    [InlineData(99999, 99999, 17, 8)]
    [InlineData(-5000, 99999, 0, 8)]
    public void Pixel_to_cell_clamps_into_the_grid(double x, double y, int col, int row)
    {
        new DesktopGrid(64, Hd).PixelToCell(x, y).Should().Be(new GridCell(col, row));
    }

    [Fact]
    public void Non_finite_pixels_map_to_the_first_cell()
    {
        var g = new DesktopGrid(64, Hd);
        g.PixelToCell(double.NaN, double.PositiveInfinity).Should().Be(new GridCell(0, 0));
    }

    [Fact]
    public void Clamp_and_contains_agree()
    {
        var g = new DesktopGrid(64, Hd);
        g.Contains(new GridCell(17, 8)).Should().BeTrue();
        g.Contains(new GridCell(18, 0)).Should().BeFalse();
        g.Contains(new GridCell(0, -1)).Should().BeFalse();
        g.Clamp(new GridCell(99, -3)).Should().Be(new GridCell(17, 0));
    }

    [Fact]
    public void The_first_free_cell_of_an_empty_grid_is_the_top_left()
    {
        new DesktopGrid(64, Hd).FirstFree(new HashSet<GridCell>()).Should().Be(new GridCell(0, 0));
    }

    [Fact]
    public void Free_cells_are_scanned_down_a_column_before_moving_right()
    {
        var g = new DesktopGrid(64, Hd);
        var occupied = new HashSet<GridCell> { new(0, 0), new(0, 1) };
        g.FirstFree(occupied).Should().Be(new GridCell(0, 2));

        for (var r = 0; r < g.Rows; r++) occupied.Add(new GridCell(0, r));
        g.FirstFree(occupied).Should().Be(new GridCell(1, 0));
    }

    [Fact]
    public void A_hole_left_by_a_removed_icon_is_reused_first()
    {
        var g = new DesktopGrid(64, Hd);
        g.FirstFree(new HashSet<GridCell> { new(0, 0), new(0, 2), new(0, 3) }).Should().Be(new GridCell(0, 1));
    }

    [Fact]
    public void A_full_grid_has_no_free_cell()
    {
        var g = new DesktopGrid(64, new DipRect(0, 0, 200, 200));
        var all = new HashSet<GridCell>();
        for (var c = 0; c < g.Columns; c++) for (var r = 0; r < g.Rows; r++) all.Add(new GridCell(c, r));
        g.FirstFree(all).Should().BeNull();
        g.NearestFree(new GridCell(0, 0), all).Should().BeNull();
    }

    [Fact]
    public void Nearest_free_returns_the_wanted_cell_when_it_is_free()
    {
        new DesktopGrid(64, Hd).NearestFree(new GridCell(5, 4), new HashSet<GridCell>()).Should().Be(new GridCell(5, 4));
    }

    [Fact]
    public void Nearest_free_picks_the_closest_free_cell_with_a_stable_tie_break()
    {
        var g = new DesktopGrid(64, Hd);
        var occupied = new HashSet<GridCell> { new(5, 4) };
        // distance 1: (4,4) (5,3) (5,5) (6,4): lower column first, then lower row
        g.NearestFree(new GridCell(5, 4), occupied).Should().Be(new GridCell(4, 4));
        occupied.Add(new GridCell(4, 4));
        g.NearestFree(new GridCell(5, 4), occupied).Should().Be(new GridCell(5, 3));
    }

    [Fact]
    public void A_tiny_area_still_has_one_cell()
    {
        var g = new DesktopGrid(64, new DipRect(0, 0, 10, 10));
        g.Columns.Should().Be(1);
        g.Rows.Should().Be(1);
        g.FirstFree(new HashSet<GridCell>()).Should().Be(new GridCell(0, 0));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(5000, 256)]
    [InlineData(64, 64)]
    public void Icon_size_is_clamped(int requested, int expected)
    {
        new DesktopGrid(requested, Hd).IconSize.Should().Be(expected);
    }
}
