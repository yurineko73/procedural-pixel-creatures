using System;
using System.IO;
using PixelCreatures.Core;
using PixelCreatures.Core.Equipment;
using PixelCreatures.Core.Export;
using PixelCreatures.Core.Genetics;

namespace PixelCreatures.Harness
{
    public static class Program
    {
        public static CreatureRegistry CreateRegistry()
        {
            var registry = CreatureRegistry.CreateDefault();
            registry.DiscoverExtensions(typeof(Program).Assembly);
            return registry;
        }

        public static int Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "help";
            string outDir = Path.GetFullPath(args.Length > 1 ? args[1] : "reports");
            if (command == "test") Directory.CreateDirectory(outDir);
            switch (command)
            {
                case "test":
                {
                    // test <outDir> [filter|all] [seedsPerFamily]  ("all" = no filter; empty arguments get lost in some shells)
                    string filter = args.Length > 2 && args[2] != "all" && args[2] != "*" ? args[2] : string.Empty;
                    var runner = new PixelCreatures.Tests.TestRunner { Log = Console.WriteLine, Filter = filter };
                    PixelCreatures.Tests.CoreTests.RunAll(runner, Program.CreateRegistry(), args.Length > 3 ? int.Parse(args[3]) : 100);
                    File.WriteAllText(Path.Combine(outDir, "core_test_report.json"), runner.ReportJson("console-harness " + Environment.OSVersion));
                    Console.WriteLine(runner.Summary());
                    return runner.Failed == 0 ? 0 : 1;
                }
                case "obj":
                {
                    // obj <outDir> [family=biped] [seed=42] [style=fantasy|modern|scifi|wasteland]
                    string familyId = args.Length > 2 ? args[2] : "biped";
                    ulong seed = args.Length > 3 ? ulong.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 42UL;
                    string style = args.Length > 4 ? args[4] : "fantasy";
                    var reg = Program.CreateRegistry();
                    var family = reg.GetFamily(familyId);
                    var genome = GenomeFactory.Sample(family, seed);
                    if (style != "fantasy")
                    {
                        if (familyId != "biped") throw new ArgumentException("Equipment preview currently requires biped family.");
                        genome = genome.With("gear.weapon", 0).With("gear.outfit", 0)
                            .With("gear.helmet", 0).With("gear.shield", 0).With("gear.pauldrons", 0);
                    }
                    var anatomy = family.BuildAnatomy(genome, reg);
                    var equipped = EquipmentGenerator.Apply(anatomy, style, seed);
                    var mesh = ObjMeshExporter.Export(equipped);
                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(Path.Combine(outDir, "creature.obj"), mesh.ObjText);
                    File.WriteAllText(Path.Combine(outDir, "creature.mtl"), mesh.MtlText);
                    Console.WriteLine($"OBJ ready: {mesh.Vertices} vertices, {mesh.Triangles} triangles; {style} / {familyId} / {seed}");
                    Console.WriteLine(Path.Combine(outDir, "creature.obj"));
                    return 0;
                }
                case "goldens":
                {
                    var rng = new Rng(42);
                    Console.WriteLine($"Pcg42 = {{ {rng.NextUInt()}u, {rng.NextUInt()}u, {rng.NextUInt()}u, {rng.NextUInt()}u }}");
                    Console.WriteLine($"Fnv = {StableHash.Fnv1a64("Procedural Pixel Creatures")}UL");
                    Console.WriteLine($"Derived = {StableHash.Derive(123456789UL, "stream.anatomy")}UL");
                    Console.WriteLine($"exp1 bits = 0x{BitConverter.DoubleToInt64Bits(DMath.Exp(1.0)):X16}L");
                    var reg = Program.CreateRegistry();
                    Console.WriteLine("Golden.Values:");
                    foreach (var (key, value) in PixelCreatures.Tests.CoreTests.GoldenValues(reg, new CreatureFactory(reg)))
                        Console.WriteLine($"            [\"{key}\"] = 0x{value:x16}UL,");
                    return 0;
                }
                default:
                    Console.WriteLine("CoreHarness test <outdir> [filter|all] [seedsPerFamily]");
                    Console.WriteLine("CoreHarness goldens  (print deterministic baseline values)");
                    Console.WriteLine("CoreHarness obj <outDir> [family=biped] [seed=42] [style=fantasy|modern|scifi|wasteland]");
                    return command == "help" ? 0 : 1;
            }
        }
    }
}
