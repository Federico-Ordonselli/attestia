using System.Xml;
using System.Xml.Schema;

namespace Attestia.Core.Xml;

/// <summary>XSD incorporati nell'assembly (copie di /contracts), compilati una sola volta.</summary>
public static class XmlSchemas
{
    public const string ImportNamespace = "urn:attestia:import:v1";
    public const string ExportNamespace = "urn:attestia:export:v1";

    private static readonly Lazy<XmlSchemaSet> ImportSet = new(() => Load("attestia-import-v1.xsd"));
    private static readonly Lazy<XmlSchemaSet> ExportSet = new(() => Load("attestia-export-v1.xsd"));

    public static XmlSchemaSet Import => ImportSet.Value;

    public static XmlSchemaSet Export => ExportSet.Value;

    private static XmlSchemaSet Load(string fileName)
    {
        var assembly = typeof(XmlSchemas).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;

        // Anche lo schema e' letto in modo sicuro: niente DTD, niente risoluzione di risorse esterne.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var set = new XmlSchemaSet { XmlResolver = null };
        set.Add(XmlSchema.Read(reader, (_, e) => throw new XmlSchemaException(e.Message))!);
        set.Compile();
        return set;
    }
}
