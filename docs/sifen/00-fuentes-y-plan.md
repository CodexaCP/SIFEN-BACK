# SIFEN Paraguay - Fuentes, secciones y plan incremental

Fecha de preparacion: 2026-04-23

Este documento es el mapa inicial de trabajo para construir un sistema profesional de facturacion electronica Paraguay SIFEN. Su objetivo es evitar supuestos: toda regla fiscal, estructura XML, firma, web service, evento o validacion debe salir de fuentes oficiales vigentes o quedar marcada como pendiente de aclaracion.

## Regla de trabajo

Si falta una regla SIFEN, hay ambiguedad o la fuente vigente no esta confirmada, se detiene la implementacion de esa parte y se formula:

`GPT, I need clarification about: <specific issue>`

No se deben inventar estructuras XML, reglas tributarias, codigos, eventos, campos obligatorios, calculos de CDC, calculos de QR ni validaciones.

## Fuentes oficiales base

1. Portal e-Kuatia DNIT:
   https://www.dnit.gov.py/web/e-kuatia

2. Documentacion tecnica DNIT:
   https://www.dnit.gov.py/web/e-kuatia/documentacion-tecnica

3. Manual Tecnico SIFEN Version 150:
   https://ekuatia.set.gov.py/documents/20123/420592/Manual%2BT%C3%A9cnico%2BVersi%C3%B3n%2B150.pdf/e706f7c7-6d93-21d4-b45b-5d22d07b2d22?t=1687362295907.pdf

4. Guia de Pruebas para e-kuatia:
   https://www.dnit.gov.py/documents/20123/424160/Guia%2Bde%2BPruebas%2Bpara%2Be-kuatia.pdf/715a15bf-d866-afe3-49e2-c10e05242c95?t=1770659877488

5. Recomendaciones y mejores practicas para SIFEN, servicio asincrono:
   https://www.dnit.gov.py/documents/20123/420592/Gu%C3%ADa%2Bde%2BMejores%2BPr%C3%A1cticas%2Bpara%2Bla%2BGesti%C3%B3n%2Bdel%2BEnv%C3%ADo%2Bde%2BDE.pdf/38fe5830-98c0-2241-9895-671f86f1225f?t=1729866823709

6. XSD oficiales SIFEN:
   https://ekuatia.set.gov.py/sifen/xsd/

7. Tablas y codificaciones:
   https://www.dnit.gov.py/web/e-kuatia/tablas-y-codificaciones

8. Certificado Cualificado de Firma Electronica:
   https://www.dnit.gov.py/web/e-kuatia/firma-digital

9. Normativas:
   https://www.dnit.gov.py/web/e-kuatia/normativas

## Fuentes locales revisadas

Archivo local:

`C:\Users\Tony\Desktop\factura electronicxa\Manual Tecnico Version 150 (3).pdf`

Datos detectados:

- Titulo: Manual Tecnico de Sistema de Facturacion Electronica Nacional.
- Version: 150.
- Fecha del documento: 2019-09-10.
- Paginas: 217.
- Advertencia del propio documento: puede sufrir modificaciones hasta la implementacion total del proyecto SIFEN.

## Hallazgos oficiales relevantes

1. La pagina de documentacion tecnica DNIT indica que alli estan los documentos necesarios para desarrollar el sistema/software de facturacion electronica y librerias de codigo abierto.

2. La documentacion vigente no es solo el Manual Tecnico v150. La DNIT publica tambien:
   - Guia de Pruebas.
   - Recomendaciones del servicio asincrono.
   - Estructuras XML.
   - XSD.
   - Notas tecnicas de ajuste al Manual Tecnico.

3. La nota tecnica mas reciente vista en la web oficial es `NT_E_KUATIA_027_MT_V150`, con fecha 2026-03-09, disponible para ambiente de test y produccion desde 2026-03-09. Esto confirma que el manual 2019 debe tratarse como base mas notas tecnicas vigentes.

4. El portal oficial de desarrollo indica que contribuyentes medianos y grandes deben desarrollar o adquirir un sistema/software conforme al Manual Tecnico para operar correctamente y transmitir comprobantes al SIFEN.

5. La Guia de Pruebas exige probar la cadena completa:
   - Comunicacion y autenticacion mutua.
   - Transmision de DE sincrona y asincrona.
   - Validaciones XML, certificado, firma y negocio.
   - Registro de eventos.
   - Consulta de DTE y eventos.
   - Generacion de KuDE.
   - Validacion/consulta por QR.

## Secciones tecnicas del Manual v150

### 1. Marco y alcance

Manual:

- Introduccion.
- Objetivos.
- Alcance.
- Estructura SIFEN.
- Fundamento legal.
- Validez juridica e incidencia tributaria.

Implementacion:

- No codificar reglas legales desde memoria.
- Mantener referencias normativas como configuracion/documentacion.
- Separar ambiente test y produccion desde el inicio.

### 2. Documentos tributarios electronicos

Manual:

- Factura Electronica.
- Autofactura Electronica.
- Nota de Credito Electronica.
- Nota de Debito Electronica.
- Nota de Remision Electronica.

Implementacion:

- Modelar tipos de documento como enumeraciones fuertes.
- Cada tipo debe tener builder/validador propio basado en XSD y reglas vigentes.
- Evitar un unico objeto gigante sin restricciones por tipo.

### 3. Ciclo operativo DE/DTE

Concepto:

- DE: documento emitido y firmado que aun no fue aprobado por SIFEN.
- DTE: documento aprobado por SIFEN con efectos tributarios.

Implementacion:

- Estados separados: Draft, Generated, Signed, Submitted, Processing, Approved, ApprovedWithObservation, Rejected, Cancelled, Disabled.
- Guardar XML generado, XML firmado, request, response, codigos y timestamps.
- Idempotencia por CDC.

### 4. XML y XSD

Manual y guia:

- XML es el lenguaje de intercambio.
- Los schemas oficiales estan publicados por DNIT.
- No incluir espacios innecesarios, comentarios, tabs, prefijos no esperados, etiquetas vacias no obligatorias ni valores negativos en campos numericos.
- Los nombres de campos son sensibles a mayusculas/minusculas.

Implementacion:

- Descargar/versionar XSD oficiales en una carpeta controlada.
- Generar clases o validadores desde XSD cuando sea viable.
- Validar XML localmente antes de enviar al SIFEN.
- Registrar la version del formato, por ejemplo 150.

### 5. CDC

Manual:

- El CDC identifica de forma unica cada DE.
- El CDC se usa como atributo `Id` del DE para firma.
- El CDC visible en KuDE debe exponerse en grupos de cuatro caracteres.
- El digito verificador del CDC tiene reglas propias en el manual.

Implementacion:

- Crear Value Object `Cdc`.
- Crear servicio especifico `CdcGenerator`.
- No implementar el algoritmo hasta revisar con detalle la seccion 10.1 y 10.2 mas notas tecnicas aplicables.
- Agregar pruebas unitarias con vectores oficiales antes de usar en produccion.

### 6. Firma digital y certificado

Manual y guia:

- Firma XML Digital Signature, formato enveloped W3C.
- Certificado emitido por PSC/PCSC habilitado en Paraguay.
- RSA 2048 para software.
- Digest SHA-2/SHA256.
- Base64.
- Transformaciones: enveloped signature y canonicalizacion.
- Para conexion con SIFEN hay autenticacion mutua TLS 1.2.
- El certificado debe contener el RUC del contribuyente emisor y, para conexion, permiso clientAuth.

Implementacion:

- Separar `XmlSigner` de `SifenTransportClient`.
- Soportar PFX/P12 desde configuracion segura.
- Validar vencimiento, RUC, cadena y uso del certificado.
- No guardar claves privadas en base de datos sin estrategia de cifrado/secret manager.

### 7. Web Services SIFEN

Manual:

- Recepcion de DE.
- Recepcion lote DE.
- Consulta resultado lote.
- Recepcion evento.
- Consulta DE.
- Consulta RUC.
- Consulta DE para organismos externos, futuro segun manual.

Endpoints test/produccion detectados:

- Produccion: `https://sifen.set.gov.py`
- Test: `https://sifen-test.set.gov.py`

Implementacion:

- Cliente SOAP separado por operacion.
- Configuracion por ambiente.
- Logging por transaccion y CDC.
- Timeouts, retries controlados y circuit breaker.
- Respuestas normalizadas a estados internos.

### 8. Servicio asincrono y lotes

Guia de mejores practicas:

- Enviar lotes de hasta 50 DE.
- Enviar documentos de un solo RUC emisor por lote.
- Enviar documentos de un solo tipo por lote.
- El mensaje de entrada del WS no debe superar 1000 KB.
- Si el lote responde 0300, fue recibido y se debe consultar despues.
- Si responde 0301, no sera procesado.
- Se recomienda empezar a consultar despues de 10 minutos y luego a intervalos no menores a 10 minutos.
- No reenviar un mismo CDC sin respuesta definitiva.
- Duplicados pueden bloquear temporalmente al RUC emisor.

Implementacion:

- Cola interna de envios.
- Tabla de lotes.
- Polling programado.
- Dead-letter para fallos definitivos.
- Bloqueo/idempotencia por CDC y lote.

### 9. Eventos

Manual:

- Evento: ocurrencia registrada en SIFEN que marca, modifica o afecta estado de DE/DTE.
- Eventos emisor: cancelacion, inutilizacion y otros futuros.
- Eventos receptor: notificacion/recepcion, conformidad, disconformidad, desconocimiento, ajustes.
- La cancelacion de DTE se menciona con plazo de hasta 48 horas posteriores a aprobacion.
- La inutilizacion permite rango de hasta 1000 numeros secuenciales si no existe ningun numero utilizado en el rango.
- Eventos toman como base el CDC, excepto inutilizacion.

Implementacion:

- Subdominio `Events`.
- Validadores por tipo de evento.
- No activar reglas de plazo sin revisar notas tecnicas vigentes.
- Registrar relacion evento -> CDC o timbrado/rango.

### 10. Validaciones y codigos

Manual:

- SIFEN valida conexion, mensajes WS, certificado, firma, XML, reglas genericas y reglas de negocio.
- Los codigos de incumplimiento tienen 4 digitos.
- Estados: aprobado, aprobado con observacion, rechazado.

Implementacion:

- Catalogo versionado de codigos.
- Parser de respuestas SOAP.
- Mapeo a errores operativos y errores corregibles.
- Monitor de rechazos frecuentes.

### 11. KuDE y QR

Manual:

- KuDE es representacion grafica.
- QR impreso debe cumplir ISO/IEC 18004.
- Ancho minimo 25 mm, con zona segura.
- Requiere CSC entregado por SIFEN.
- CSC se usa para generar hash del QR.
- En test la guia publica CSC genericos.

Implementacion:

- Generador de KuDE PDF por tipo de documento.
- Generador de QR separado.
- Validar URL test/produccion.
- No enviar al SIFEN informacion libre del KuDE que no pertenece al XML firmado.

### 12. Ambiente de pruebas

Guia de pruebas:

- Los documentos de test no tienen valor juridico.
- El sistema debe probar comunicacion, autenticacion, transmision, validacion, eventos, consulta, KuDE y QR.
- La guia define cantidades minimas por escenario.

Implementacion:

- Crear profile `SifenTest`.
- Seed seguro de datos de prueba configurables.
- Pruebas automatizadas hasta XML/XSD.
- Pruebas de integracion reales solo cuando exista certificado habilitado y autorizacion.

## Arquitectura propuesta

Stack sugerido por el plan inicial:

- .NET 8 Web API.
- Clean Architecture.
- SQL Server.
- Entity Framework Core.
- Serilog.
- Health checks.

Capas:

- Domain: entidades, value objects, reglas puras, estados.
- Application: casos de uso, comandos, orquestacion, validaciones de aplicacion.
- Infrastructure: EF Core, SOAP, firma, certificados, PDF, QR, storage.
- API: endpoints, autenticacion, DTOs, versionado.
- Tests: unit, integration, contract/XSD, end-to-end test cuando haya ambiente DNIT habilitado.

## Roadmap incremental

### Fase 0 - Preparacion y verdad documental

Objetivo:

- Mantener fuentes, manual, XSD, notas tecnicas y guia de pruebas organizadas.

Entregables:

- Este documento.
- Carpeta futura `docs/sifen/sources`.
- Registro de decisiones tecnicas.

Pruebas:

- Revision manual de fuentes.
- Verificacion de links oficiales.

### Fase 1 - Proyecto base

Objetivo:

- Crear solucion .NET 8 limpia y compilable.

Entregables:

- `src/Domain`
- `src/Application`
- `src/Infrastructure`
- `src/API`
- `Tests`
- Health check.
- Serilog.
- Configuracion por ambiente.

Pruebas:

- `dotnet build`
- `dotnet test`
- Health check local.

### Fase 2 - Dominio minimo

Objetivo:

- Modelar DE/DTE, RUC, CDC, Timbrado, Cliente, Documento, Evento, estados.

Entregables:

- Entidades y Value Objects.
- Validaciones basicas no tributarias.
- Tests unitarios.

Pruebas:

- Construccion valida/invalida de Value Objects.
- Estados permitidos.

### Fase 3 - XSD y XML

Objetivo:

- Generar y validar XML local contra XSD.

Entregables:

- Paquete de XSD versionado.
- Servicio de validacion XSD.
- Builders iniciales por tipo.

Pruebas:

- XML valido contra XSD.
- XML invalido falla con error claro.

### Fase 4 - CDC y firma

Objetivo:

- Generar CDC con algoritmo oficial y firmar XML.

Entregables:

- `CdcGenerator`.
- `XmlSignatureService`.
- Validacion local de firma.

Pruebas:

- Vectores oficiales de CDC.
- XML firmado con `Signature`.
- Validacion local de certificado/firma.

### Fase 5 - Cliente SIFEN test

Objetivo:

- Integrar SOAP con ambiente de test.

Entregables:

- Recepcion DE.
- Recepcion lote.
- Consulta lote.
- Consulta DE.
- Consulta RUC.
- Recepcion evento.

Pruebas:

- Contract tests con envelopes.
- Integracion real solo con certificado habilitado.

### Fase 6 - Persistencia y operacion

Objetivo:

- Guardar documentos, lotes, respuestas, eventos y auditoria.

Entregables:

- DbContext.
- Migraciones.
- Indices por CDC, RUC, estado, lote.

Pruebas:

- Tests de repositorio.
- Idempotencia por CDC.

### Fase 7 - Eventos

Objetivo:

- Emitir eventos emisor/receptor con reglas verificadas.

Entregables:

- Servicios de cancelacion, inutilizacion, conformidad, disconformidad, desconocimiento, recepcion.

Pruebas:

- Reglas por evento.
- Rechazo local de estados imposibles.

### Fase 8 - KuDE y QR

Objetivo:

- Generar PDF KuDE y QR consultable.

Entregables:

- Plantillas PDF.
- QR por ambiente.
- Hash QR con CSC.

Pruebas:

- PDF por tipo.
- QR escaneable.
- Validacion URL test.

### Fase 9 - Automatizacion operativa

Objetivo:

- Reintentos, colas, monitoreo, soporte.

Entregables:

- Worker de lotes.
- Worker de consultas.
- Dead-letter.
- Endpoints operativos.

Pruebas:

- Reintentos.
- No duplicar CDC.
- Recuperacion ante timeout.

## Bloqueos conocidos antes de codificar reglas SIFEN

1. Confirmar todas las Notas Tecnicas aplicables, no solo NT 027.
2. Descargar y versionar XSD oficiales.
3. Extraer vectores oficiales para CDC, QR y firma.
4. Confirmar si el proyecto tendra multi-tenant desde el primer dia.
5. Confirmar estrategia de almacenamiento de certificados.
6. Confirmar si el primer objetivo es solo ambiente test DNIT o producto SaaS completo.

## Primer paso recomendado

Crear el proyecto base .NET 8 con Clean Architecture, sin implementar todavia XML SIFEN. El primer incremento debe compilar, tener health check y dejar lista la configuracion para ambientes `Sifen:Test` y `Sifen:Production`.

