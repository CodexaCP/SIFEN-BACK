# Paquete XSD oficial SIFEN v150

Descargado de https://ekuatia.set.gov.py/sifen/xsd/ (listado del directorio + cierre recursivo de xs:import/xs:include)
con `tools/sifen-xsd/fetch-xsd.sh`. Hashes SHA-256 y fechas en `MANIFEST.tsv` (calculados al descargar; DNIT no publica checksum).
Los ficheros son copia exacta del servidor: NO editar. Para refrescar: `tools/sifen-xsd/fetch-xsd.sh <dir> <base> <raices>`.

- Cierre requerido para emitir/transmitir un DE: `siRecepDE_v150.xsd`, `DE_v150.xsd`, `WS_SiRecepDE_v150.xsd` (+ dependencias).
- `rde/150/*.xsd` (referenciados por `siRecepRDE*_v150.xsd`, Recibo de Dinero Electronico) responden 404 en el servidor: fuera de alcance.
- Los `schemaLocation` absolutos `https://ekuatia.set.gov.py/sifen/xsd/...` se resuelven en las pruebas contra esta carpeta (`LocalPackageResolver`).
