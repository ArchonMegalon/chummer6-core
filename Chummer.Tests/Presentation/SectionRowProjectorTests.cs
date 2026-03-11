using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Chummer.Tests.Presentation;

[TestClass]
public class SectionRowProjectorTests
{
    [TestMethod]
    public void BuildRows_flattens_nested_objects_and_arrays()
    {
        JsonObject payload = new()
        {
            ["name"] = "Apex",
            ["stats"] = new JsonObject
            {
                ["body"] = 4,
                ["limits"] = new JsonArray(5, 6)
            }
        };

        IReadOnlyList<SectionRowState> rows = SectionRowProjector.BuildRows(payload);

        Assert.IsTrue(rows.Any(row => row.Path == "name" && row.Value == "\"Apex\""));
        Assert.IsTrue(rows.Any(row => row.Path == "stats.body" && row.Value == "4"));
        Assert.IsTrue(rows.Any(row => row.Path == "stats.limits" && row.Value.Contains('5')));
    }

    [TestMethod]
    public void BuildRows_respects_max_row_limit()
    {
        JsonObject payload = new();
        for (int index = 0; index < 8; index++)
        {
            payload[$"field{index}"] = index;
        }

        IReadOnlyList<SectionRowState> rows = SectionRowProjector.BuildRows(payload, maxRows: 3);

        Assert.HasCount(3, rows);
    }

    [TestMethod]
    public void BuildRows_returns_empty_for_null_node()
    {
        IReadOnlyList<SectionRowState> rows = SectionRowProjector.BuildRows(node: null);
        Assert.IsEmpty(rows);
    }
}
