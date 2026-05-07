# Firma XML SIFEN v150 + NT 16

Fecha: 2026-04-23

## Fuente de decision

Se implemento el perfil indicado por la aclaracion recibida para:

- Manual Tecnico SIFEN v150.
- Ajuste vigente de firma segun `NT_E_KUATIA_016_MT_V150`.

## Alcance implementado

Este bloque firma XML ya construido. No genera todavia:

- XML completo del DE.
- CDC real.
- Validacion XSD.
- Validacion de RUC dentro del certificado.
- Validacion de cadena PCSC/CRL/OCSP.

Es una capa aislada para firmar el nodo con `Id="<CDC>"`.

## Perfil XMLDSig implementado

### Formato

XML Digital Signature enveloped.

La firma se inserta como `Signature` en el mismo documento XML, despues del nodo firmado.

### Nodo firmado

El signer busca el elemento con:

```xml
Id="<CDC>"
```

Y crea:

```xml
<Reference URI="#<CDC>">
```

### CanonicalizationMethod

```text
http://www.w3.org/TR/2001/REC-xml-c14n-20010315
```

### SignatureMethod

```text
http://www.w3.org/2001/04/xmldsig-more#rsa-sha256
```

### Transform

Solo un transform:

```text
http://www.w3.org/2000/09/xmldsig#enveloped-signature
```

No se agrega XPath.

No se agrega transform extra de exclusive canonicalization.

### DigestMethod

```text
http://www.w3.org/2001/04/xmlenc#sha256
```

### KeyInfo

Se incluye:

```xml
<KeyInfo>
  <X509Data>
    <X509Certificate>...</X509Certificate>
  </X509Data>
</KeyInfo>
```

No se incluye:

- `X509SubjectName`
- `X509IssuerSerial`
- `X509IssuerName`
- `X509SKI`
- `KeyValue`
- `RSAKeyValue`
- `Modulus`
- `Exponent`

## Clases agregadas

Application:

- `IXmlDocumentSigner`
- `SignXmlDocumentCommand`
- `SignedXmlDocumentResult`

Infrastructure:

- `SifenXmlDocumentSigner`
- `SifenSignedXml`
- `SifenXmlSignatureAlgorithms`

API:

- `POST /internal/xml-signing/sign`

## Endpoint interno

```http
POST /internal/xml-signing/sign
```

Body:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "environment": "Test",
  "documentId": "12345678901234567890123456789012345678901234",
  "xml": "<rDE xmlns=\"http://ekuatia.set.gov.py/sifen/xsd\"><dVerFor>150</dVerFor><DE Id=\"12345678901234567890123456789012345678901234\"></DE></rDE>"
}
```

Requisitos:

- El tenant debe tener metadata de certificado activa con `Purpose = XmlSignature`.
- `CertificateSecretReference` debe resolver a PFX/P12.
- `CertificatePasswordSecretReference` debe resolver a password.
- El certificado debe contener private key RSA.

## Seguridad

El signer:

- Carga el PFX en memoria con `X509KeyStorageFlags.EphemeralKeySet`.
- No escribe certificados temporales.
- No loguea password, CSC, PFX ni private key.
- Audita `tenantId`, ambiente, documento firmado, alias y fingerprint del certificado.

## Tests

Se agrego prueba real de firma:

- Genera certificado RSA temporal.
- Exporta PFX temporal fuera del repo.
- Registra metadata en EF InMemory.
- Firma XML con nodo `Id`.
- Verifica algoritmos y estructura XMLDSig.
- Verifica que `KeyInfo` no incluya elementos prohibidos.
- Valida criptograficamente la firma.

Resultado actual:

```text
Tests correctos: 10/10
Build correcto: 0 warnings, 0 errores
```

## Pendientes antes de enviar a SIFEN

1. Generar XML DE completo desde XSD oficial.
2. Generar CDC con algoritmo oficial y vectores de prueba.
3. Validar XML contra XSD oficial.
4. Validar RUC del certificado contra contribuyente.
5. Validar cadena, revocacion y autoridad PCSC segun DNIT.
6. Implementar cliente SOAP mTLS.

