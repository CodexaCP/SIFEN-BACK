using System.Xml.Linq;
using SifenInvoicing.Application.XmlDe;

namespace SifenInvoicing.Tests.XmlDe;

/// <summary>
/// Fase 4.2 - XML de referencia del DE01 minimo generado por el builder (sin Signature ni gCamFuFD), versionado para
/// inspeccion y regresion. Regenerar SOLO a proposito: SIFEN_UPDATE_GOLDEN=1 dotnet test --filter SifenDeGoldenTests
/// </summary>
public sealed class SifenDeGoldenTests
{
    private static string GoldenPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "XmlDe", "Samples", name);

    private static string SourcePath(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "XmlDe", "Samples", name));

    private static void Check(string name, SifenDeBuilderOptions options)
    {
        var generated = XDocument.Parse(new SifenDeXmlBuilder(options).Build(SifenDeBuilderFixtures.Input()).Xml);
        if (Environment.GetEnvironmentVariable("SIFEN_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(SourcePath(name), generated.ToString(SaveOptions.None) + Environment.NewLine);
        }

        var expected = XDocument.Load(File.Exists(SourcePath(name)) && Environment.GetEnvironmentVariable("SIFEN_UPDATE_GOLDEN") == "1" ? SourcePath(name) : GoldenPath(name));
        Assert.True(XNode.DeepEquals(expected.Root!, generated.Root!), $"El XML generado difiere de {name}.");
    }

    [Fact]
    public void Default_Options_MatchGolden() =>
        Check("de01-builder-reference.xml", SifenDeBuilderOptions.Default);

    [Fact]
    public void EmitDSisFact_Options_MatchGolden() =>
        Check("de01-builder-reference-dsisfact.xml", new SifenDeBuilderOptions(DSisFact: DSisFactMode.EmitContributorSystem));
}
