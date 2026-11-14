#!/usr/bin/env bash
# Cadastra jogos de teste pela API (POST /api/v1/games) para o teste de carga do k6.
#
# A listagem faz um COUNT(*) da tabela inteira a cada requisição; com volume, o banco vira
# o gargalo e a diferença do cache aparece. Os jogos passam pela validação de domínio da
# API (título e descrição com mais de 10 caracteres, lançamento no futuro).
#
# Uso (com a API no ar):
#   performance/seed-games.sh                                  # 10.000 jogos em http://localhost:5140
#   QTD=20000 PARALELO=16 performance/seed-games.sh            # mais jogos, mais requisições simultâneas
#   BASE_URL=http://localhost:5001 performance/seed-games.sh   # API do docker compose
#
# Precisa de: bash, curl, xargs, seq.
set -euo pipefail

QTD="${QTD:-10000}"
PARALELO="${PARALELO:-8}"
BASE_URL="${BASE_URL:-http://localhost:5140}"
LOTE=1000
ENDPOINT="$BASE_URL/api/v1/games"

# A API recusa lançamento no passado: um ano à frente (GNU date no Linux, BSD date no macOS).
RELEASE_DATE="$(date -d '+1 year' +%F 2>/dev/null || date -v+1y +%F)"
# Prefixo da execução: rodar o script de novo não repete títulos.
PREFIXO="$(date +%H%M%S)"

total_catalogo() {
  curl -fsS "$ENDPOINT?page=1&pageSize=1" | grep -o '"total":[0-9]*' | cut -d: -f2
}

if ! antes="$(total_catalogo)"; then
  echo "!! A API não respondeu em $ENDPOINT. Suba a API antes (ver o README, Teste de carga)." >&2
  exit 1
fi
echo "==> Cadastrando $QTD jogos em $ENDPOINT ($PARALELO por vez). Catálogo antes: $antes jogos."

# Uma requisição por número; imprime só o HTTP status (201 = criado).
cadastrar() {
  local i="$1"
  local generos=(Action RPG Strategy Sports Puzzle Other)
  local genero="${generos[$((i % ${#generos[@]}))]}"
  local preco="$((i % 300)).$((i % 100))"
  curl -s -o /dev/null -w '%{http_code}\n' -X POST "$ENDPOINT" \
    -H 'Content-Type: application/json' \
    -d "{\"title\":\"Jogo de carga $PREFIXO-$i\",\"description\":\"Jogo gerado para o teste de carga ($i)\",\"price\":$preco,\"genre\":\"$genero\",\"releaseDate\":\"$RELEASE_DATE\"}"
}
export -f cadastrar
export ENDPOINT RELEASE_DATE PREFIXO

erros=0
inicio=$SECONDS
for ((de = 1; de <= QTD; de += LOTE)); do
  ate=$((de + LOTE - 1 > QTD ? QTD : de + LOTE - 1))
  # Cada processo do xargs chama a função exportada (export -f) com o número do jogo.
  falhas="$(seq "$de" "$ate" |
    xargs -P "$PARALELO" -I{} bash -c 'cadastrar "$1"' _ {} |
    grep -vc '^201$' || true)"
  erros=$((erros + falhas))
  echo "    $ate/$QTD ($((SECONDS - inicio)) s, falhas: $erros)"
done

depois="$(total_catalogo)"
echo "==> Pronto em $((SECONDS - inicio)) s: $((depois - antes)) jogos novos, $depois no catálogo."
if [ "$erros" -gt 0 ]; then
  echo "!! $erros requisições não retornaram 201. Teste uma à mão para ver a mensagem de validação." >&2
  exit 1
fi
