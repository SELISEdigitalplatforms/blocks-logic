using System;
using System.Collections.Generic;
using DataAccess.InternalService;
using FluentAssertions;
using MongoDB.Bson;
using Xunit;

namespace XUnitTest.LogicResolver;

/// <summary>ColumnMask.HiddenSetPaths: which $set entries of an edit would erase a value hidden from the caller.</summary>
public class KeepHiddenSetTests
{
    public sealed class Inner
    {
        public string? Secret { get; set; }
        public string? Shown { get; set; }
    }

    public sealed class Sample
    {
        public string Id { get; set; } = "s1";
        public string? Name { get; set; }
        public List<string> Variables { get; set; } = [];
        public int Count { get; set; }
        public Inner? Inner { get; set; }
    }

    private static readonly Sample Stored = new() { Name = "fn", Variables = ["A=1"], Count = 3, Inner = new Inner { Secret = "s", Shown = "x" } };

    /// <summary>Hides Variables, Count and Inner.Secret, as Genesis does for hidden columns.</summary>
    private static Dictionary<string, object> Mask(Dictionary<string, object> row)
    {
        row["Variables"] = null!;
        row["Count"] = null!;
        if (row["Inner"] is IDictionary<string, object> inner) inner["Secret"] = null!;
        return row;
    }

    [Fact]
    public void WhatTheMaskedReadShowed_IsDropped_AndEverythingElseIsWritten()
    {
        var set = new BsonDocument
        {
            { "Name", "renamed" }, { "Variables", new BsonArray() }, { "Count", 0 }, { "Inner.Secret", BsonNull.Value }, { "Inner.Shown", "y" },
        };

        ColumnMask.HiddenSetPaths(set, Stored, Mask).Should().BeEquivalentTo(["Variables", "Count", "Inner.Secret"]);
    }

    [Fact]
    public void AValueTheCallerSetThemselves_IsWritten()
    {
        var set = new BsonDocument { { "Variables", new BsonArray { "B=2" } }, { "Count", 9 } };

        ColumnMask.HiddenSetPaths(set, Stored, Mask).Should().BeNull();
    }

    [Fact]
    public void NothingHidden_KeepsTheWholeUpdate()
    {
        ColumnMask.HiddenSetPaths(new BsonDocument("Variables", new BsonArray()), Stored, row => row).Should().BeNull();
    }
}
