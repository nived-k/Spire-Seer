using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpireSeer.Core;
using Xunit;

namespace SpireSeer.Tests;

/// <summary>The C# coach must give the same answers as the Python coach it was ported from.
/// tests/golden/golden.json is produced by the Python tool (make_golden.py) on seeded synthetic runs.</summary>
public class GoldenTests
{
    private const double Tol = 0.0011;

    private static readonly string Root = FindRoot();
    private static Stream? Open(string name)
    {
        var p = Path.Combine(Root, "data", name + ".json");
        return File.Exists(p) ? File.OpenRead(p) : null;
    }

    // one store per community data version (each golden file was produced from one version)
    private static readonly Dictionary<string, DataStore> Stores = new();

    private static DataStore StoreFor(string version)
    {
        if (!Stores.TryGetValue(version, out var s))
        {
            s = new DataStore(Open) { Version = version };
            Stores[version] = s;
        }
        return s;
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "SpireSeer.json"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root (SpireSeer.json) not found");
    }

    public static IEnumerable<object[]> Cases(string type)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "tests", "golden"), "golden-*.json").OrderBy(f => f))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var version = doc.RootElement.GetProperty("version").GetString()!;
            int i = 0;
            foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                if (c.GetProperty("type").GetString() == type) yield return new object[] { $"{version} {type}#{i}", version, c.GetRawText() };
                i++;
            }
        }
    }

    public static IEnumerable<object[]> CardCases() => Cases("card");
    public static IEnumerable<object[]> ShopCases() => Cases("shop");
    public static IEnumerable<object[]> RelicCases() => Cases("relic");
    public static IEnumerable<object[]> EventCases() => Cases("event");

    private static RunSnapshot Run(JsonElement c)
    {
        var r = c.GetProperty("run");
        return new RunSnapshot(
            r.GetProperty("character").GetString()!,
            r.GetProperty("act").GetInt32(), 0,
            r.GetProperty("hp").GetInt32(), r.GetProperty("max_hp").GetInt32(), r.GetProperty("gold").GetInt32(),
            r.GetProperty("deck").EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToList(),
            Array.Empty<string>(),
            r.GetProperty("removals_used").GetInt32());
    }

    private static double? D(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetDouble();
    private static int? I(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private static void Lifts(JsonElement expected, IReadOnlyList<double?> actual, string name)
    {
        var exp = expected.EnumerateArray().Select(D).ToList();
        Assert.True(exp.Count == actual.Count, $"{name}: {exp.Count} options expected, got {actual.Count}");
        for (int i = 0; i < exp.Count; i++)
        {
            if (exp[i] == null) { Assert.True(actual[i] == null, $"{name}: option {i} expected no data, got {actual[i]}"); continue; }
            Assert.True(actual[i] != null, $"{name}: option {i} expected {exp[i]}, got no data");
            Assert.True(Math.Abs(exp[i]!.Value - actual[i]!.Value) < Tol, $"{name}: option {i} expected {exp[i]}, got {actual[i]}");
        }
    }

    [Theory]
    [MemberData(nameof(CardCases))]
    public void CardReward(string name, string version, string json)
    {
        var c = JsonDocument.Parse(json).RootElement;
        var run = Run(c);
        var store = StoreFor(version);
        var lib = store.For(run.Character)!;
        var offers = c.GetProperty("offers").EnumerateArray()
            .Select(o => new Offer(o.GetProperty("id").GetString()!, o.GetProperty("upgraded").GetBoolean())).ToList();
        var a = CardCoach.Advise(lib, store.Common, run, offers);
        var e = c.GetProperty("expect");

        var p = e.GetProperty("profile");
        Assert.Equal(p.GetProperty("lead").GetString(), a.Profile.Lead);
        Assert.Equal(p.GetProperty("archetype").GetString(), a.Profile.Archetype);
        Assert.Equal(p.GetProperty("size").GetInt32(), a.Profile.Size);
        Assert.Equal(p.GetProperty("themes").EnumerateObject().Select(x => $"{x.Name}={x.Value.GetInt32()}"),
            a.Profile.Themes.Select(kv => $"{kv.Key}={kv.Value}"));
        Assert.Equal(p.GetProperty("roles").EnumerateObject().Select(x => $"{x.Name}={x.Value.GetInt32()}").OrderBy(x => x),
            a.Profile.Roles.Select(kv => $"{kv.Key}={kv.Value}").OrderBy(x => x));

        Lifts(e.GetProperty("lifts"), a.Options.Select(o => o.Lift).ToList(), name);
        Assert.Equal(I(e.GetProperty("pick")), a.Pick);
        Assert.Equal(e.GetProperty("close").GetBoolean(), a.CloseCall != null);
        Assert.Equal(e.GetProperty("nudges").EnumerateArray().Select(n => $"{n.GetProperty("kind").GetString()}/{n.GetProperty("level").GetString()}"),
            a.Nudges.Select(n => $"{n.Kind}/{n.Level}"));
    }

    [Theory]
    [MemberData(nameof(ShopCases))]
    public void Shop(string name, string version, string json)
    {
        var c = JsonDocument.Parse(json).RootElement;
        var run = Run(c) with { Gold = c.GetProperty("gold").GetInt32() };
        var store = StoreFor(version);
        var lib = store.For(run.Character)!;
        var items = c.GetProperty("items").EnumerateArray().Select(x =>
        {
            var id = x.GetProperty("id").GetString()!;
            bool up = id.EndsWith('+');
            return new ShopOffer(x.GetProperty("kind").GetString()!, id.TrimEnd('+'), x.GetProperty("price").GetInt32(), up);
        }).ToList();
        var a = ShopCoach.Advise(lib, store.Common, run, items);
        var e = c.GetProperty("expect");
        Lifts(e.GetProperty("lifts"), a.Options.Select(o => o.Lift).ToList(), name);
        Assert.Equal(e.GetProperty("buy").EnumerateArray().Select(x => x.GetInt32()), a.Plan.Buy.OrderBy(x => x));
        Assert.Equal(e.GetProperty("cost").GetInt32(), a.Plan.Cost);
    }

    [Theory]
    [MemberData(nameof(RelicCases))]
    public void Relic(string name, string version, string json)
    {
        var c = JsonDocument.Parse(json).RootElement;
        var run = Run(c);
        var store = StoreFor(version);
        var lib = store.For(run.Character)!;
        var ids = c.GetProperty("relics").EnumerateArray().Select(x => x.GetString()!).ToList();
        var a = ChoiceCoach.Relics(lib, run, ids, "ancient");
        var e = c.GetProperty("expect");
        Lifts(e.GetProperty("lifts"), a.Options.Select(o => o.Lift).ToList(), name);
        Assert.Equal(I(e.GetProperty("pick")) ?? -1, a.Pick);
    }

    [Theory]
    [MemberData(nameof(EventCases))]
    public void Event(string name, string version, string json)
    {
        var c = JsonDocument.Parse(json).RootElement;
        var run = Run(c);
        var store = StoreFor(version);
        var lib = store.For(run.Character)!;
        var a = ChoiceCoach.Event(lib, store.Common, run, c.GetProperty("event").GetString()!);
        Assert.NotNull(a);
        var e = c.GetProperty("expect");
        Assert.Equal(e.GetProperty("option_ids").EnumerateArray().Select(x => x.GetString()), a!.Options.Select(o => o.Id));
        Lifts(e.GetProperty("lifts"), a.Options.Select(o => o.Lift).ToList(), name);
        Assert.Equal(I(e.GetProperty("pick")) ?? -1, a.Pick);
        Assert.Equal(e.GetProperty("band").GetString(), a.HpBand);
    }

    [Fact]
    public void UnknownCharacterHasNoLibrary() => Assert.Null(new DataStore(Open).For("fool"));

    [Fact]
    public void EveryShippedVersionHasAllFiveCharacters()
    {
        var store = new DataStore(Open);
        Assert.NotEmpty(store.Common.Versions);
        foreach (var v in store.Common.Versions)
        {
            store.Version = v;
            foreach (var ch in new[] { "ironclad", "silent", "defect", "regent", "necrobinder" })
                Assert.True(store.For(ch)?.Version == v, $"{ch} {v}");
        }
    }

    [Theory]
    [InlineData("v0.107.1", "0.107.1")]
    [InlineData("v0.107.1", "v0.107.1")]
    [InlineData("v0.107.1", "0.109.0")]   // between: newest not newer than the game
    [InlineData("v0.111.0", "0.111.0")]
    [InlineData("v0.111.0", "0.113.2")]   // newer game: newest data
    [InlineData("v0.107.1", "0.99.0")]    // older game: oldest data
    [InlineData("v0.111.0", null)]        // unknown: newest
    [InlineData("v0.111.0", "garbage")]
    public void PicksDataForGameVersion(string expected, string? game) =>
        Assert.Equal(expected, DataStore.PickVersion(new[] { "v0.111.0", "v0.107.1" }, game));
}
