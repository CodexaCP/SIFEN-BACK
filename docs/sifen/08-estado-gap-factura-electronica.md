# Estado y Brecha del Modulo de Factura Electronica

Fecha: 2026-04-28
Alcance: solo Factura Electronica SIFEN (`TipoDoc 01`)
Fuera de alcance por ahora: notas de credito, notas de debito, remision, exportacion, eventos, lotes, recepcion de terceros, compras.

## Resumen ejecutivo

- El proyecto ya tiene una base tecnica seria para FE: multi-tenant, persistencia, CDC, firma XML, envio SOAP inicial, parseo de respuesta y endpoints.
- El proyecto todavia no esta listo para venta abierta.
- El proyecto si esta cerca de un piloto tecnico controlado, pero no de una comercializacion masiva.

## Estado global

- Avance tecnico del modulo FE: `52%`
- Preparacion para piloto pagado con 1 cliente: `30%`
- Preparacion para venta abierta tipo SaaS FE: `18%`

## Estado por submodulo

| Submodulo FE | Estado | Avance | Observacion |
|---|---:|---:|---|
| Aislamiento multi-tenant | Implementado | 80% | TenantId, segregacion y persistencia base listos |
| Onboarding tecnico por tenant | Parcial | 60% | falta cierre operativo de certificados reales, CSC y readiness comercial |
| CDC | Implementado | 100% | generacion y validacion ya resueltas |
| Generacion XML FE | Parcial | 35% | existe builder, pero todavia no hay evidencia de cumplimiento FE completo contra XSD oficial real de punta a punta |
| Firma digital XMLDSIG | Implementado | 80% | perfil de firma ya esta, falta validacion con certificados reales homologados en flujo real |
| Envio a SIFEN | Parcial | 45% | existe gateway SOAP inicial, falta prueba real contra ambiente test con acceso valido |
| Parseo de respuesta | Parcial | 55% | existe parser, falta cubrir variantes reales de respuesta de SIFEN |
| Persistencia del ciclo de vida | Implementado | 75% | XML, XML firmado, estado y respuesta se guardan |
| API FE | Parcial | 55% | endpoints base listos, falta endurecimiento funcional y operativo |
| Panel visual FE | Inicial | 20% | hay UI minima, no hay panel operativo real |
| Observabilidad y soporte FE | Inicial | 15% | faltan metricas, alertas, retry UX, diagnostico y trazabilidad usable por soporte |
| Homologacion FE real | Pendiente | 10% | no esta demostrado aun un DE aprobado real en ambiente test |

## Lo que ya tenemos

### Base tecnica

- solucion .NET 8 en capas
- SQL Server
- auditoria estructurada
- health checks
- separacion por tenant

### Nucleo FE ya construido

- `CDC` generado
- builder FE inicial
- firma XML por tenant
- `POST /invoice`
- `GET /invoice/{id}`
- `GET /invoice/status/{cdc}`
- persistencia en `Documents` y `Logs`
- envio SOAP inicial con `rEnviDe`
- parseo inicial de `rRetEnviDe/rProtDe`

### Ventaja real frente a un prototipo improvisado

- no esta montado como script aislado
- ya hay persistencia de documentos
- ya existe separacion por tenant
- ya existe manejo de certificado por tenant
- ya existe trazabilidad minima del ciclo de vida

## Comparacion interna contra proveedores visibles

Referencias publicas consultadas:

- FactPy: [factpy.com](https://factpy.com/) y [docs.factpy.com](https://docs.factpy.com/index.php)
- FacturaSend: [facturasend.com.py](https://facturasend.com.py/)
- BillPy: [billpy.com.py](https://billpy.com.py/) y [funcionalidades](https://billpy.com.py/funcionalidades.html)
- FactAPI: [factapipy.com](https://factapipy.com/)

### Lo que esos proveedores ya muestran hacia mercado

- API JSON/REST simple para integradores
- panel web usable para emitir y consultar
- aprobadas/rechazadas visibles
- XML firmado y KuDE descargables
- onboarding mas rapido para cliente final
- soporte y operacion visibles
- posicionamiento comercial claro

### Lo que nosotros ya tenemos mejor encaminado

- arquitectura mas controlada y extensible
- tenant isolation mejor pensada desde inicio
- base mas limpia para crecer a SaaS + API
- persistencia y auditoria tecnica mas ordenadas para un producto serio

### Lo que nos falta para estar al nivel comercial minimo en FE

- una FE real aprobada en ambiente test
- panel visual real de operacion FE
- KuDE PDF + QR usable
- errores y rechazos entendibles para usuario
- flujo de reintento y reproceso
- onboarding comercial mas corto
- visibilidad operativa para soporte

## Brecha exacta para poder vender FE

### Brecha critica

1. XML FE real contra XSD oficial completo
2. prueba real de envio y aprobacion en SIFEN test
3. KuDE PDF + QR
4. panel visual usable
5. manejo de errores y soporte operativo

### Brecha alta

1. numeracion y control operativo de establecimiento/punto/numero
2. validaciones de negocio FE mas duras antes del envio
3. reintentos controlados
4. expiracion y estado de certificados
5. mensajes de estado entendibles para cliente y soporte

### Brecha media

1. filtros de consulta
2. exportacion XML/PDF
3. dashboard basico
4. documentacion comercial y tecnica de integracion FE

## Riesgos actuales

### Riesgo 1: creer que FE ya esta homologada

No hay evidencia en el codigo de una FE aprobada real en ambiente test. Sin eso no hay venta segura.

### Riesgo 2: el builder FE todavia puede no estar completo

Hay generacion XML, pero todavia falta cerrar con evidencia fuerte el cumplimiento exacto de la estructura FE oficial real en todos los campos obligatorios del caso minimo.

### Riesgo 3: gateway SOAP sin validacion real de acceso

El endpoint SIFEN de recepcion no esta validado aun en una ejecucion real completa dentro del producto.

### Riesgo 4: falta panel de soporte

Sin panel operativo, cualquier rechazo o error tecnico vuelve lento el soporte y dificulta vender a varios clientes.

### Riesgo 5: falta KuDE

Sin KuDE PDF + QR, el producto FE queda incompleto para uso real de cliente final.

## Que se puede mejorar ya mismo

### Alta prioridad

- endurecer el XML FE hasta validacion real contra XSD oficial
- cerrar envio real a SIFEN test
- agregar KuDE
- construir panel FE de listado, detalle, estado y error

### Prioridad media

- mostrar errores de rechazo por CDC y documento
- agregar filtros y busqueda
- agregar diagnostico de certificado por tenant

### Prioridad baja por ahora

- pulido visual
- automatizaciones secundarias
- reportes no esenciales

## Proximos pasos propuestos

### Fase 1: cierre tecnico minimo vendible FE

1. validar el builder FE contra XSD oficial real
2. ajustar campos FE faltantes o incorrectos
3. ejecutar FE real firmada contra ambiente test
4. obtener primera aprobacion real

### Fase 2: cierre funcional FE

1. generar KuDE PDF + QR
2. agregar detalle visual de factura
3. agregar listado por estado
4. agregar consulta por CDC y reintento controlado

### Fase 3: cierre comercial FE

1. panel FE usable para operador
2. mensajes de error entendibles
3. onboarding de tenant mas corto
4. checklist de salida a piloto

## Secuencia recomendada

Orden de trabajo estricto:

1. cerrar XML FE real
2. cerrar aprobacion real en SIFEN test
3. cerrar KuDE
4. cerrar panel FE
5. cerrar operacion y soporte FE
6. salir a piloto

## Conclusión interna

Hoy el proyecto no esta en estado de venta abierta.

Hoy el proyecto si esta en estado de base seria para cerrar rapido un piloto FE, siempre que el siguiente bloque se concentre en:

- aprobacion real en SIFEN test
- KuDE
- panel operativo minimo

Sin esos tres cierres, todavia no conviene venderlo como servicio FE estable.
