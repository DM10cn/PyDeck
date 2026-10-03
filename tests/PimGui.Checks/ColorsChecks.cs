using PimGui.Core;

internal static class ColorsChecks
{
    public static void Run(Action<string, Action> check, string scratch)
    {
        static void Require(bool condition, string message) { if (!condition) throw new IOException(message); }
        check("RGBA native conversion matches managed fallback, preserves alpha and feeds the same seeds", () =>
        {
            byte[] golden = [0x12,0x34,0x56,0x78, 255,0,0,255, 0,255,0,127, 0,0,255,0, 1,2,3,254];
            Require(MonetColors.RgbaToArgb(golden).SequenceEqual(new uint[] { 0x78123456,0xFFFF0000,0x7F00FF00,0x000000FF,0xFE010203 }), "RGBA channel order or alpha changed");
            var random = new Random(42688);
            foreach (var count in Enumerable.Range(0, 34).Concat(new[] { 255,256,257,MonetColors.MaximumPixelCount-3,MonetColors.MaximumPixelCount-2,MonetColors.MaximumPixelCount-1,MonetColors.MaximumPixelCount }))
            {
                var rgba = new byte[count * 4]; random.NextBytes(rgba);
                var original = rgba.ToArray();
                var expected = new uint[count];
                MonetColors.ConvertRgbaToArgbManaged(rgba, expected);
                Require(MonetColors.RgbaToArgb(rgba).SequenceEqual(expected), $"Native/managed pixel mismatch at count {count}");
                Require(rgba.SequenceEqual(original), "RGBA source mutated");
            }
            var argb = MonetColors.RgbaToArgb(golden);
            Require(MonetColors.SeedsFromPixels(argb).SequenceEqual(new uint[] { 0xFFFF0000 }), "Converted transparent pixels changed the wallpaper seed");
        });
        check("RGBA adapter rejects null, partial pixels and oversized input", () =>
        {
            try { MonetColors.RgbaToArgb(null!); throw new IOException("Null RGBA accepted"); }
            catch (ArgumentNullException) { }
            foreach (var bytes in new[] { 1,2,3,5,MonetColors.MaximumPixelCount * 4 + 4 })
            {
                try { MonetColors.RgbaToArgb(new byte[bytes]); throw new IOException("Invalid RGBA length accepted"); }
                catch (ArgumentException) { }
            }
            Require(MonetColors.RgbaToArgb([]).Length == 0, "Empty RGBA conversion failed");
        });
        check("Monet settings migrate, normalize and persist separately from the active design", () =>
        {
            var store = new SettingsStore(Path.Combine(scratch, "monet-settings"));
            Directory.CreateDirectory(store.DirectoryPath);
            File.WriteAllText(store.FilePath, "{\"Design\":\"Fluent\",\"Theme\":\"Light\"}");
            var legacy = store.Load();
            Require(legacy.Design == "Fluent" && legacy.MaterialColorSource == "Wallpaper" && legacy.MaterialSeed == 0xFF1B6EF3u && legacy.MaterialSecondSeed is null && legacy.MaterialColorStyle == "TonalSpot", "Legacy colors changed");
            var custom = legacy with { MaterialColorSource = "Custom", MaterialSeed = 0x00A85028u, MaterialColorStyle = "Expressive" };
            store.Save(custom);
            var restored = store.Load();
            Require(restored.MaterialColorSource == "Custom" && restored.MaterialSeed == 0xFFA85028u && restored.MaterialColorStyle == "Expressive" && restored.Design == "Fluent", "Custom colors did not round trip");
            store.Save(custom with { MaterialColorStyle = "DualSource", MaterialSecondSeed = 0x00287C60u });
            var dual = store.Load();
            Require(dual.MaterialColorStyle == "DualSource" && dual.MaterialSeed == 0xFFA85028u && dual.MaterialSecondSeed == 0xFF287C60u && dual.Design == "Fluent",
                "Second seed or dual-source selection did not normalize and round trip independently");
            store.Save(dual with { MaterialSecondSeed = null });
            Require(store.Load().MaterialSecondSeed is null && store.Load().MaterialColorStyle == "DualSource", "Missing optional second seed did not remain null");
            var invalid = (custom with { MaterialColorSource = "Other", MaterialColorStyle = "Other" }).Normalize();
            Require(invalid.MaterialColorSource == "Wallpaper" && invalid.MaterialColorStyle == "TonalSpot", "Invalid source or variant persisted");
        });
        check("AOSP wallpaper candidates retain score order, transparent filtering and single-seed fallback", () =>
        {
            var pixels = Enumerable.Repeat(0xFF0000FFu, 90).Concat(Enumerable.Repeat(0xFFFF0000u, 10)).ToArray();
            var candidates = MonetColors.SeedsFromPixels(pixels);
            Require(candidates.SequenceEqual(new uint[] { 0xFF0000FFu, 0xFFFF0000u }), "Dominant blue and distinct red were not returned in AOSP score order");
            Require(MonetColors.SeedFromPixels(pixels) == candidates[0], "Legacy wallpaper seed changed when multiple candidates became available");
            Require(MonetColors.SeedsFromPixels(pixels.Reverse().ToArray()).SequenceEqual(candidates), "Candidate ordering depended on pixel traversal order");
            Require(MonetColors.SeedsFromPixels(pixels.Concat(Enumerable.Repeat(0x0000FF00u, 100)).ToArray()).SequenceEqual(candidates), "Transparent pixels introduced a second wallpaper color");
            Require(MonetColors.SeedsFromPixels(Enumerable.Repeat(0xFFA85028u, 100).ToArray()).SequenceEqual(new uint[] { 0xFFA85028u }), "Monochrome wallpaper did not retain a single source");
            foreach (var input in new uint[][] { [], [0x00000000u, 0x00FFFFFFu], [0xFF808080u, 0xFF000000u, 0xFFFFFFFFu] })
                Require(MonetColors.SeedsFromPixels(input).SequenceEqual(new uint[] { 0xFF1B6EF3u }), "Empty/transparent/neutral wallpaper did not return the genuine AOSP fallback");
            var colorful = new uint[] { 0xFFFF0000u, 0xFF00FF00u, 0xFF0000FFu, 0xFFFFFF00u, 0xFFFF00FFu, 0xFF00FFFFu };
            var bounded = MonetColors.SeedsFromPixels(Enumerable.Range(0, 1200).Select(index => colorful[index % colorful.Length]).ToArray());
            Require(bounded.Length is >= 1 and <= 4 && bounded.Distinct().Count() == bounded.Length && bounded.All(color => color >> 24 == 255), "Wallpaper candidate list is unbounded, duplicated or transparent");
            try { MonetColors.SeedsFromPixels(new uint[MonetColors.MaximumPixelCount + 1]); throw new IOException("Oversize candidate input accepted"); }
            catch (ArgumentException) { }
        });
        check("Dual-source color roles isolate the second palette without changing single-source schemes", () =>
        {
            foreach (var dark in new[] { false, true })
            {
                var golden = MonetColors.Create(0xFF6750A4u, dark);
                Require(golden.Primary == (dark ? 0xFFCFBDFEu : 0xFF65558Fu) &&
                    golden.Secondary == (dark ? 0xFFCBC2DBu : 0xFF625B71u) && golden.Tertiary == (dark ? 0xFFEFB8C8u : 0xFF7E5260u),
                    "Existing pinned Tonal Spot output changed");
                var expressive = MonetColors.Create(0xFF0000FFu, dark, "Expressive");
                Require(expressive.Primary == (dark ? 0xFF87D7ABu : 0xFF146C48u), "Existing pinned Expressive output changed");
                foreach (var variant in new[] { "TonalSpot", "Expressive" })
                {
                    var original = MonetColors.Create(0xFF6750A4u, dark, variant);
                    var extra = MonetColors.Create(0xFF6750A4u, dark, variant, 0xFF287C60u);
                    Require(original == extra && extra.SecondSeed is null, "Unused second seed changed a single-source variant or its cache metadata");
                }
                foreach (var seed in new uint[] { 0xFF6750A4u, 0xFFFF0000u, 0xFF000000u, 0xFFFFFFFFu })
                {
                    var single = MonetColors.Create(seed, dark);
                    foreach (var secondary in new uint?[] { null, seed, seed & 0x00FFFFFFu })
                    {
                        var same = MonetColors.Create(seed, dark, "DualSource", secondary);
                        Require(AllRoles(single).SequenceEqual(AllRoles(same)) && same.SecondSeed == seed,
                            "Missing or identical second color did not degenerate exactly to Tonal Spot");
                    }
                }
                var combined = MonetColors.Create(0xFF6750A4u, dark, "DualSource", 0x00287C60u);
                var second = MonetColors.Create(0xFF287C60u, dark);
                Require(combined.Seed == 0xFF6750A4u && combined.SecondSeed == 0xFF287C60u && combined.Variant == "DualSource", "Dual-source metadata was not normalized");
                Require(PrimaryAndNeutralRoles(combined).SequenceEqual(PrimaryAndNeutralRoles(golden)), "Second color changed primary, neutral or error roles");
                Require(SecondaryAndTertiaryRoles(combined).SequenceEqual(SecondaryAndTertiaryRoles(second)), "Second source was not assigned to secondary and tertiary families");
                Require(combined.Secondary != golden.Secondary && combined.Tertiary != golden.Tertiary, "Distinct second color did not affect both accent families");
            }
        });
        check("Native Monet ABI returns opaque roles and excludes transparent wallpaper pixels", () =>
        {
            Require(MonetColors.EngineVersion.Contains("2021", StringComparison.Ordinal), "Missing pinned color specification");
            Require(MonetColors.SeedFromPixels([0xFFFF0000u, 0x000000FFu]) == 0xFFFF0000u, "Transparent sample influenced seed");
            Require(MonetColors.SeedFromPixels([0xFF808080u, 0xFFFFFFFFu, 0xFF000000u]) == 0xFF1B6EF3u, "AOSP neutral fallback changed");
            try { MonetColors.SeedFromPixels(new uint[12545]); throw new IOException("Oversize pixel array accepted"); }
            catch (ArgumentException) { }
            try { MonetColors.Create(0xFFFF0000u, false, "Unknown"); throw new IOException("Unknown variant accepted"); }
            catch (ArgumentException) { }
        });
        check("HCT dynamic schemes retain text contrast for saturated and neutral seeds in both variants", () =>
        {
            foreach (var seed in new uint[] { 0xFF1B6EF3u, 0xFFFF0000u, 0xFF00FF00u, 0xFFFFFF00u, 0xFFFF00FFu, 0xFF000000u, 0xFFFFFFFFu, 0xFF777777u })
            foreach (var dark in new[] { false, true })
            foreach (var variant in new[] { "TonalSpot", "Expressive" })
            {
                var scheme = MonetColors.Create(seed, dark, variant);
                foreach (var pair in TextPairs(scheme))
                {
                    Require(pair.Item1 >> 24 == 255 && pair.Item2 >> 24 == 255, "A semantic role is transparent");
                    Require(Contrast(pair.Item1, pair.Item2) >= 4.45, $"Unreadable scheme: {seed:X8}/{dark}/{variant}");
                }
                Require(scheme.Primary != scheme.Secondary && scheme.Primary != scheme.Tertiary, "Accent families collapsed");
            }
        });
        check("Dual-source schemes preserve all semantic text/container contrast pairs in light and dark", () =>
        {
            foreach (var seed in new uint[] { 0xFF6750A4u, 0xFFFF0000u, 0xFF000000u, 0xFFFFFFFFu })
            foreach (var second in new uint[] { 0xFF287C60u, 0xFF0000FFu, 0xFFFFFF00u, 0xFFFF00FFu, 0xFF777777u })
            foreach (var dark in new[] { false, true })
            {
                var scheme = MonetColors.Create(seed, dark, "DualSource", second);
                Require(AllRoles(scheme).Count() == 49 && AllRoles(scheme).All(color => color >> 24 == 255), "Dual-source scheme returned incomplete or transparent roles");
                foreach (var pair in TextPairs(scheme))
                    Require(Contrast(pair.Item1, pair.Item2) >= 4.45, $"Unreadable dual-source pair: {seed:X8}/{second:X8}/{dark}: {pair.Item1:X8}/{pair.Item2:X8}");
            }
        });
        check("Dynamic scheme cache is bounded and safe across wallpaper/UI calls", () =>
        {
            MonetColors.ClearCache();
            Parallel.For(0, 100, index =>
            {
                var seed = 0xFF000000u | (uint)(index * 123451);
                var first = MonetColors.Create(seed, index % 2 == 0);
                var second = MonetColors.Create(seed, index % 2 == 0);
                Require(first == second, "Same input produced different schemes");
            });
            Require(MonetColors.CachedSchemeCount <= MonetColors.CacheCapacity, "Unbounded scheme cache");
            Require(MonetColors.Create(0x001B6EF3u, false) == MonetColors.Create(0xFF1B6EF3u, false), "Seed alpha affected palette");
            var firstDual = MonetColors.Create(0xFF6750A4u, false, "DualSource", 0xFF287C60u);
            var otherDual = MonetColors.Create(0xFF6750A4u, false, "DualSource", 0xFFFF0000u);
            Require(firstDual.Secondary != otherDual.Secondary && !ReferenceEquals(firstDual, otherDual), "Cache key omitted the second color");
            Require(ReferenceEquals(firstDual, MonetColors.Create(0x006750A4u, false, "DualSource", 0x00287C60u)), "Cache did not normalize both seed alpha channels");
            Parallel.For(0, 100, index =>
            {
                var second = 0xFF000000u | (uint)(index * 98731);
                var first = MonetColors.Create(0xFF6750A4u, index % 2 == 0, "DualSource", second);
                Require(first == MonetColors.Create(0xFF6750A4u, index % 2 == 0, "DualSource", second), "Concurrent dual-source cache returned another seed's scheme");
            });
            Require(MonetColors.CachedSchemeCount <= MonetColors.CacheCapacity, "Dual-source scheme cache is unbounded");
        });
    }

    private static uint[] PrimaryAndNeutralRoles(MonetScheme s) =>
    [
        s.Primary, s.OnPrimary, s.PrimaryContainer, s.OnPrimaryContainer,
        s.Surface, s.OnSurface, s.SurfaceVariant, s.OnSurfaceVariant, s.SurfaceDim, s.SurfaceBright,
        s.SurfaceContainerLowest, s.SurfaceContainerLow, s.SurfaceContainer, s.SurfaceContainerHigh, s.SurfaceContainerHighest,
        s.Outline, s.OutlineVariant, s.Error, s.OnError, s.ErrorContainer, s.OnErrorContainer,
        s.InverseSurface, s.InverseOnSurface, s.InversePrimary, s.SurfaceTint, s.Background, s.OnBackground, s.Shadow, s.Scrim,
        s.PrimaryFixed, s.PrimaryFixedDim, s.OnPrimaryFixed, s.OnPrimaryFixedVariant
    ];

    private static uint[] SecondaryAndTertiaryRoles(MonetScheme s) =>
    [
        s.Secondary, s.OnSecondary, s.SecondaryContainer, s.OnSecondaryContainer,
        s.Tertiary, s.OnTertiary, s.TertiaryContainer, s.OnTertiaryContainer,
        s.SecondaryFixed, s.SecondaryFixedDim, s.OnSecondaryFixed, s.OnSecondaryFixedVariant,
        s.TertiaryFixed, s.TertiaryFixedDim, s.OnTertiaryFixed, s.OnTertiaryFixedVariant
    ];

    private static IEnumerable<uint> AllRoles(MonetScheme scheme) => PrimaryAndNeutralRoles(scheme).Concat(SecondaryAndTertiaryRoles(scheme));

    private static (uint, uint)[] TextPairs(MonetScheme s) =>
    [
        (s.Primary, s.OnPrimary), (s.PrimaryContainer, s.OnPrimaryContainer),
        (s.Secondary, s.OnSecondary), (s.SecondaryContainer, s.OnSecondaryContainer),
        (s.Tertiary, s.OnTertiary), (s.TertiaryContainer, s.OnTertiaryContainer),
        (s.Error, s.OnError), (s.ErrorContainer, s.OnErrorContainer),
        (s.Surface, s.OnSurface), (s.SurfaceVariant, s.OnSurfaceVariant), (s.SurfaceDim, s.OnSurface), (s.SurfaceBright, s.OnSurface),
        (s.SurfaceContainerLowest, s.OnSurface), (s.SurfaceContainerLow, s.OnSurface), (s.SurfaceContainer, s.OnSurface),
        (s.SurfaceContainerHigh, s.OnSurface), (s.SurfaceContainerHighest, s.OnSurface),
        (s.InverseSurface, s.InverseOnSurface), (s.InverseSurface, s.InversePrimary), (s.Background, s.OnBackground),
        (s.PrimaryFixed, s.OnPrimaryFixed), (s.PrimaryFixedDim, s.OnPrimaryFixed),
        (s.PrimaryFixed, s.OnPrimaryFixedVariant), (s.PrimaryFixedDim, s.OnPrimaryFixedVariant),
        (s.SecondaryFixed, s.OnSecondaryFixed), (s.SecondaryFixedDim, s.OnSecondaryFixed),
        (s.SecondaryFixed, s.OnSecondaryFixedVariant), (s.SecondaryFixedDim, s.OnSecondaryFixedVariant),
        (s.TertiaryFixed, s.OnTertiaryFixed), (s.TertiaryFixedDim, s.OnTertiaryFixed),
        (s.TertiaryFixed, s.OnTertiaryFixedVariant), (s.TertiaryFixedDim, s.OnTertiaryFixedVariant)
    ];

    private static double Contrast(uint first, uint second)
    {
        static double Linear(uint channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        static double Luminance(uint argb) => .2126 * Linear((argb >> 16) & 255) + .7152 * Linear((argb >> 8) & 255) + .0722 * Linear(argb & 255);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
