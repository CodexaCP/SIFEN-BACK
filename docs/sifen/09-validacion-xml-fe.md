<!-- scope: sifen-fe | relevant for: xml, xsd, factura-electronica, tipodoc-01 -->

# Validacion previa de XML FE TipoDoc 01

## Purpose

Cerrar una validacion obligatoria antes de firmar y enviar Factura Electronica TipoDoc 01.

## Data model

- `FacturaXmlGenerator` produce el XML base.
- `IFacturaXmlPreSubmissionValidator` valida estructura minima y luego delega a `IXmlSchemaValidator`.
- `Sifen:XmlSchemas:Invoice01RootPath` define la ruta del XSD raiz oficial.

## API / endpoints

- Impacta indirectamente `POST /invoice/`.
- Si no existe XSD configurado o el XML no valida, la emision se corta antes de firma y envio.

## Business rules

- El XML debe contener `rDE`, `dVerFor`, `DE` y `DE@Id = CDC`.
- El XML debe declarar `dCodTipoDoc = 01`.
- El flujo valida `tenant` activo antes de generar FE.
- El flujo valida que exista certificado de firma XML listo para el tenant y ambiente antes de generar FE.
- El builder exige establecimiento, punto de expedicion, numero, seguridad, emisor, receptor, fecha, moneda e items validos.
- El builder y el servicio verifican consistencia de `CDC` y de totales calculados antes de firma y envio.
- Si no hay XSD oficial configurado, el sistema falla con guard clause explicito.
- Si hay XSD configurado, se ejecuta validacion XSD antes de firmar/enviar.

## Constraints

- No se inventa estructura oficial faltante.
- El XSD oficial no se almacena aun en el repo.

## Edge cases

- XML mal formado.
- `DE@Id` distinto al CDC.
- `dVerFor` ausente.
- `tenant` inexistente o suspendido.
- certificado no listo para firma.
- totales del XML inconsistentes con los totales calculados.
- Ruta de XSD vacia o archivo inexistente.
