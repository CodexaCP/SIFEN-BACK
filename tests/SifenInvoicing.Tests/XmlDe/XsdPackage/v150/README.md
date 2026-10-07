# Paquete XSD oficial SIFEN v150 (PENDIENTE DE DESCARGA)

Vacío a propósito: el entorno de desarrollo no alcanza `ekuatia.set.gov.py`. Para poblarlo (desde la VPS, con red):

    tools/sifen-xsd/fetch-xsd.sh        # descarga recursiva + MANIFEST.tsv (SHA-256 calculado localmente)

Luego ejecutar `dotnet test --filter FullyQualifiedName~XmlDe`. Los tests `Xsd*` se omiten mientras no exista
`siRecepDE_v150.xsd` aquí (o `SIFEN_XSD_DIR` apuntando a otra carpeta). No mezclar con XSD de versiones anteriores.
