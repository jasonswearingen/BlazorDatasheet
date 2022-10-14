using System.Linq;
using BlazorDatasheet.Data;
using BlazorDatasheet.Formats;
using BlazorDatasheet.Render;
using NUnit.Framework;

namespace BlazorDatasheet.Test;

public class ConditionalFormatTests
{
    private ConditionalFormatManager cm;

    /// <summary>
    /// Sheet of size 2 x 2
    /// </summary>
    private Sheet sheet;

    private ConditionalFormat greaterThanEqualToZeroRedBgCf;
    private string redBgColor = "#ff0000";

    [SetUp]
    public void Setup()
    {
        sheet = new Sheet(2, 2);
        cm = new ConditionalFormatManager(sheet);
        greaterThanEqualToZeroRedBgCf = new ConditionalFormat(
            cell => cell.GetValue<int>() >= 0,
            (cell => new Format
            {
                BackgroundColor = redBgColor
            }));
    }

    [Test]
    public void Set_Cf_To_Whole_Sheet_Applies_Correctly()
    {
        cm.Register("g0", greaterThanEqualToZeroRedBgCf);
        cm.Apply("g0");
        sheet.TrySetCellValue(0, 0, -1);
        var format = cm.CalculateFormat(0, 0);
        Assert.IsNull(format);
        sheet.TrySetCellValue(0, 0, 1);
        format = cm.CalculateFormat(0, 0);
        Assert.AreEqual(format.BackgroundColor, redBgColor);
    }

    [Test]
    public void Cf_Correctly_Passes_All_Cells_To_Func()
    {
        // Create a conditional format that sets the background color to
        // a string which is equal to the number of cells that have the conditional
        // format registered
        var cf = new ConditionalFormat(
            c => true, (cell, cells) => new Format() { BackgroundColor = cells.Count().ToString() });
        cm.Register("cf", cf);
        cm.Apply("cf");
        var formatApplied = cm.CalculateFormat(0, 0);
        Assert.NotNull(formatApplied);
        Assert.AreEqual(sheet.Range.Area.ToString(), formatApplied!.BackgroundColor);
    }
}