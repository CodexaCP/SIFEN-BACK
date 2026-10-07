#!/usr/bin/env bash
# Descarga RECURSIVA del paquete XSD oficial SIFEN v150 (xs:import / xs:include / xs:redefine) y genera MANIFEST.tsv.
# Uso: tools/sifen-xsd/fetch-xsd.sh [dir_destino] [url_base] [archivo_raiz ...]
# Requiere red hacia ekuatia.set.gov.py (curl, sha256sum, grep -P). No inventa checksums: DNIT no publica ninguno
# (verificar en la fuente); el SHA-256 del manifiesto es el calculado localmente al descargar.
set -euo pipefail
DEST="${1:-tests/SifenInvoicing.Tests/XmlDe/XsdPackage/v150}"
BASE="${2:-https://ekuatia.set.gov.py/sifen/xsd}"
shift 2 2>/dev/null || true
ROOTS=("$@")
[ ${#ROOTS[@]} -eq 0 ] && ROOTS=(siRecepDE_v150.xsd DE_v150.xsd WS_SiRecepDE_v150.xsd protProcesDE_v150.xsd)
mkdir -p "$DEST"
MAN="$DEST/MANIFEST.tsv"
printf 'file\turl\tbytes\tsha256\tdownloaded_utc\treferenced_by\n' > "$MAN"
declare -A SEEN
queue=()
for r in "${ROOTS[@]}"; do queue+=("$r|(raiz solicitada)"); done
while [ ${#queue[@]} -gt 0 ]; do
  item="${queue[0]}"; queue=("${queue[@]:1}")
  f="${item%%|*}"; by="${item#*|}"
  [ -n "${SEEN[$f]:-}" ] && continue
  SEEN[$f]=1
  url="$BASE/$f"
  if ! curl -fsS --max-time 60 -o "$DEST/$f" "$url"; then
    echo "FALTA: $f (referenciado por: $by) <- $url" >&2
    printf '%s\t%s\tNO_DESCARGADO\t-\t-\t%s\n' "$f" "$url" "$by" >> "$MAN"
    rm -f "$DEST/$f"; continue
  fi
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$f" "$url" "$(stat -c%s "$DEST/$f")" "$(sha256sum "$DEST/$f" | cut -d' ' -f1)" "$(date -u +%FT%TZ)" "$by" >> "$MAN"
  # schemaLocation relativos (solo nombre de archivo; las URL absolutas externas se reportan)
  while IFS= read -r loc; do
    case "$loc" in
      http*) n="${loc##*/}"; echo "AVISO: $f referencia URL absoluta $loc (se intentara $n bajo $BASE)" >&2; queue+=("$n|$f") ;;
      *) queue+=("$loc|$f") ;;
    esac
  done < <(grep -oP '(?:schemaLocation)\s*=\s*"\K[^"]+' "$DEST/$f" || true)
done
echo "Manifiesto: $MAN"; cut -c1-220 "$MAN"
