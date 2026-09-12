#!/usr/bin/env bash
# Nexus Optimizer — validação de uma mão (Linux/macOS: build cross-compile + testes).
# A app WPF NÃO corre fora do Windows; isto valida o build e as camadas testáveis.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "═══════════════════════════════════════════════════════"
echo " NEXUS OPTIMIZER — VERIFICAÇÃO (cross-compile + testes)"
echo "═══════════════════════════════════════════════════════"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERRO: .NET SDK não encontrado. Instale o .NET 8 SDK:"
  echo "  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0"
  exit 1
fi

echo
echo "[1/2] dotnet build (Release, todos os projetos)..."
dotnet build NexusOptimizer.sln -c Release --nologo
echo "Build OK."

echo
echo "[2/2] dotnet test (Nexus.Tests)..."
dotnet test tests/Nexus.Tests -c Release --nologo -v minimal

echo
echo "═══════════════════════════════════════════════════════"
echo " TUDO OK (build cross-compile + testes)."
echo " A app WPF corre apenas em Windows: dotnet run --project src/Nexus.Presentation"
echo "═══════════════════════════════════════════════════════"
