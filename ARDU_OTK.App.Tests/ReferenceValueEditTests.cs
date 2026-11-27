using System.IO;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Store;

namespace ARDU_OTK.App.Tests;

/// <summary>
/// Ручное задание значения в эталоне до записи в реестр.
/// </summary>
public sealed class ReferenceValueEditTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "otk-value-edit-" + Guid.NewGuid().ToString("N"));

    public ReferenceValueEditTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ReferenceParameters Load(string text)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".param");
        File.WriteAllText(path, text);
        return ReferenceParameters.Load(path);
    }

    [Fact]
    public void Mission_planner_line_keeps_separators_and_comments()
    {
        var lines = new[] { "# шапка", "COMPASS_OFS_X\t12.5", "RC1_MIN,1000" };

        var result = ReferenceParamFile.SetValue(lines, "COMPASS_OFS_X", -3.25);

        Assert.Equal(new[] { "# шапка", "COMPASS_OFS_X\t-3.25", "RC1_MIN,1000" }, result);
    }

    [Fact]
    public void Qgc_line_replaces_only_value_column()
    {
        var lines = new[] { "1\t1\tCOMPASS_OFS_X\t12.5\t9", "1\t1\tRC1_MIN\t1000\t4" };

        var result = ReferenceParamFile.SetValue(lines, "RC1_MIN", 1100);

        Assert.Equal("1\t1\tRC1_MIN\t1100\t4", result[1]);
        Assert.Equal(lines[0], result[0]);
    }

    [Fact]
    public void Qgc_integer_parameter_rejects_fraction()
    {
        var lines = new[] { "1\t1\tCOMPASS_OFS_X\t12.5\t9", "1\t1\tRC1_MIN\t1000\t4" };

        Assert.Throws<ArgumentException>(() => ReferenceParamFile.SetValue(lines, "RC1_MIN", 1000.5));
    }

    [Fact]
    public void Duplicates_are_all_replaced()
    {
        var lines = new[] { "COMPASS_OFS_X,1", "COMPASS_OFS_X,2" };

        var result = ReferenceParamFile.SetValue(lines, "COMPASS_OFS_X", 7);

        Assert.Equal(new[] { "COMPASS_OFS_X,7", "COMPASS_OFS_X,7" }, result);
    }

    [Fact]
    public void Unknown_name_is_rejected()
    {
        Assert.Throws<KeyNotFoundException>(
            () => ReferenceParamFile.SetValue(new[] { "COMPASS_OFS_X,1" }, "NOPE", 1));
    }

    [Fact]
    public void With_value_changes_hash_and_records_edit_in_text()
    {
        var source = Load("# прошивка образца: ArduCopter V4.5.7\nCOMPASS_OFS_X,12.5\nRC1_MIN,1000\n");

        var edited = source.WithValue("RC1_MIN", 1100, "Иванов");

        Assert.Equal(1100, edited.Set.Values["RC1_MIN"]);
        Assert.NotEqual(source.Hash, edited.Hash);
        Assert.Equal(source.Firmware, edited.Firmware);
        Assert.Contains(
            ReferenceParameters.SplitLines(edited.Text),
            l => l.StartsWith(ReferenceParameters.ManualEditMarker + "RC1_MIN: 1000 → 1100 (Иванов, ", StringComparison.Ordinal));
        Assert.Equal(1000, source.Set.Values["RC1_MIN"]);
    }

    [Fact]
    public void Same_value_returns_same_set()
    {
        var source = Load("COMPASS_OFS_X,12.5\n");

        Assert.Same(source, source.WithValue("COMPASS_OFS_X", 12.5, "Иванов"));
    }

    [Fact]
    public void Non_finite_value_is_rejected()
    {
        var source = Load("COMPASS_OFS_X,12.5\n");

        Assert.Throws<ArgumentException>(() => source.WithValue("COMPASS_OFS_X", double.NaN, "Иванов"));
    }
}
