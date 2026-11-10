# HL OPTIMIZER PRO
### Windows Performance & System Control

**Estado: incremento funcional de desenvolvimento 0.1, não produto completo nem Release validado.**
Aplicação desktop WPF/.NET 8 para Windows 10/11 x64. Não é um website ou uma simulação com dados fixos.
O PrecisionCurve original mantém-se em `../app`, com o driver, serviço e testes originais.
O HL não instala, carrega nem comunica com esse driver.

## Executar e compilar

Pré-requisitos: Windows 10/11 x64 e SDK .NET 8 com Windows Desktop targeting pack.

```powershell
dotnet run --project HL.Optimizer.Pro/HL.Optimizer.Pro.csproj
# Testes do núcleo (também podem correr em Linux com SDK .NET 8):
dotnet run --project HL.Optimizer.Pro/Tests/HL.Optimizer.Tests.csproj -c Release
# Validar testes, compilar WPF e publicar executável self-contained:
./scripts/build-hl.ps1 -Publish
```

A publicação gera `artifacts/hl-win-x64/HL.Optimizer.Pro.exe` **apenas após execução bem-sucedida**.
O script não assina digitalmente o binário e não cria instalador. Não foi gerado um executável neste ambiente.
Não é necessária elevação para abrir o HL. Ferramentas externas do Windows podem solicitar UAC.
Não usar em máquinas de produção antes da checklist de VM.

## O que existe realmente

| Área | Implementação atual | Limites declarados na interface |
|---|---|---|
| Shell | Sidebar com 16 destinos, tema escuro, layout redimensionável, comandos assíncronos, confirmação com «Não» por defeito, atividade e progresso | Sem temas alternativos, SVGs por página ou animações |
| Dashboard / Hardware | CPU por deltas `GetSystemTimes`, RAM `GlobalMemoryStatusEx`, uptime, versão do Windows, utilização do volume do sistema | GPU, temperaturas, BIOS, SMART e gráficos históricos não implementados; N/D |
| PC Health | Diagnóstico por limiares documentados: RAM/CPU ≥85%, volume ≥90% ocupado; resultados explicados, sem pontuação | Apenas amostras; sem Windows Update, drivers, SFC ou scans Full/Gaming |
| Cleaner | Análise de `%TEMP%` do utilizador, ficheiros com mais de 7 dias, seleção individual inicialmente vazia, confirmação, revalidação, limpeza e relatório | Não limpa navegadores, cache de shaders, Windows Update, reciclagem ou Windows Temp |
| Booster | Inventário real de working set de processos | Sem perfis automáticos, encerramentos ou purgas de RAM |
| Gaming | Correspondência de nomes de processos de launchers/emuladores em execução | Não prova que um jogo esteja instalado nem identifica Free Fire dentro do emulador; sem suspensão, sessões ou medições FPS |
| Tweaks | Desativar transparência e captura de jogo via dois valores HKCU conhecidos; backup do tipo/valor ou ausência; Apply, verificação e Undo | Não altera serviços, rato, energia, telemetria ou memória virtual; não promete FPS |
| Debloat / Applications | Inventário desktop por chaves Uninstall HKLM/HKCU, vistas 32/64 bits | Sem Appx, tamanhos verificados ou desinstalação direta; ferramenta Windows disponível |
| Startup | Inventário Run e pastas Startup | Estado efetivamente ativo, impacto, serviços e tarefas agendadas não verificados; sem alteração |
| Process Manager | Nome, PID, working set, atualização e ordenação pela tabela | Sem CPU por processo, GPU, rede, publicador ou finalização; Task Manager disponível |
| Network | Adaptadores, estado, velocidade reportada, IPv4, gateway e DNS | Sem testes DNS, comparação, alteração, renovação de IP ou resets |
| Storage | Volumes disponíveis e não prontos, capacidade, livre, formato, tipo | Sem DriveMap físico, gráfico circular, análise de ficheiros grandes ou eventos USB |
| Windows Tools | 16 destinos allowlisted para definições e ferramentas Windows | Abertura externa, sem alegar sucesso da operação. SFC/DISM/CHKDSK ainda não integrados |
| Restore Center | Histórico local persistente de operações; registos Prepared recuperáveis via Undo em Tweaks; exportação JSON | Pontos de restauro geridos manualmente nas ferramentas Windows, não criados pelo HL |
| Settings | Intervalo de amostragem 1/2/5/10 s com persistência local | Só inglês/tema escuro, sem updater, autostart ou gestão de permissões |

`Refresh / Analyze` volta a obter os dados da página. `Scan PC` executa apenas o diagnóstico
implementado. `Export report` exporta os resultados da página, amostras atuais e histórico;
pode incluir caminhos, nomes e endereços privados. Não partilhar sem rever.
As ferramentas de sistema são abertas na janela nativa respetiva: «Launched» **não significa concluído**.

## Organização e decisões

- `Core/`: .NET 8 sem WPF, interfaces, modelos, Cleaner, Health, journal e motor de reversão.
- `Infrastructure/`: APIs Windows, inventários, catálogo de ferramentas e valores de registo allowlisted.
- `ViewModels/`: estado, comandos assíncronos e orquestração; nenhum comando PowerShell nos botões.
- `Views/` e `Resources/`: janela, estilos reutilizáveis, ícone HL original SVG/ICO.
- `Tests/`: executável de testes sem dependências NuGet, com fakes apenas nos testes.

Foi escolhido JSON local em vez de SQLite nesta etapa: registos individuais atómicos, legíveis e
sem necessidade de migração de base de dados. CommunityToolkit, WMI, PerformanceCounter e
LibreHardwareMonitor **ainda não foram integrados**. As leituras implementadas não os exigem;
monitorização de sensores deverá ser um provider adicional com testes de compatibilidade e licença.
A preferência por essas bibliotecas não justifica adicioná-las sem utilização real.

## Segurança e recuperação

- Dados em `%LOCALAPPDATA%\HL.Optimizer.Pro`. Bloqueio exclusivo do ficheiro `instance.lock`
  impede duas instâncias HL de escreverem simultaneamente para a mesma pasta de utilizador.
- Journal separado por operação, backup escrito antes da mutação, aplicação serializada e
  verificação após escrita. Falha tenta compensação e regista `RecoveryRequired` se não conseguir.
- O Undo recupera o primeiro estado anterior, incluindo ausência do valor. Aplicações repetidas
  são bloqueadas até restaurar a anterior. Alteração externa divergente bloqueia Undo automático.
- Na interrupção entre backup e conclusão, o registo `Prepared` permite Undo após reabrir.
  Não apagar History antes de resolver pendências. JSON não é backup criptográfico ou proteção
  contra adulteração; Atomic rename não garante sobrevivência a perda súbita de energia.
- Dois tweaks de baixo risco, apenas HKCU. Não existem operações administrativas avançadas
  nem um «Apply all»; proteção por ponto de restauro será requisito antes de adicioná-las.
- Cleaner valida raiz, todos os ancestrais/reparse points, tamanho, data e idade antes de apagar.
  Não segue links de diretórios e não apaga diretórios. Erros de acesso são ignorados/reportados.
  A análise limita-se a 10 000 candidatos por execução para conter consumo de memória; informa quando esse limite é atingido.
  Existe uma janela TOCTOU entre validação e remoção: **não usar em pastas com modificações
  hostis concorrentes**. Proteção por handles Windows contra essa corrida é trabalho pendente.
- Limpeza é destrutiva e não reversível; o aviso distingue isso dos tweaks reversíveis. Guardam-se
  manifesto dos selecionados e resultado no journal. O espaço libertado é soma dos tamanhos
  lógicos de ficheiros apagados, não uma garantia de aumento idêntico do espaço físico livre.
- Cancelamento do núcleo interrompe análise ou para a limpeza entre ficheiros. O fecho da
  janela cancela trabalho pendente. A UI não expõe ainda botão de cancelamento dedicado.
- Inventários são read-only. Não se encerram processos, desinstalam apps ou desativam serviços.

## Testes e validação obrigatória

`Tests/Program.cs` cobre: allowlist, idade, ficheiros modificados após scan, rejeição de candidatos
forjados, links, cancelamento, consentimento, write-ahead, Apply/Undo, alterações externas,
compensação, recuperação após reinício, concorrência do journal e critérios de diagnóstico.
O teste de links requer Developer Mode/elevação no Windows; imprime SKIP se faltar permissão.
**Não confundir testes de núcleo com testes de integração Windows.**

Neste ambiente Linux sem SDK .NET:
- Testes legados do repositório: executados com sucesso (math, 2 506 parity checks, 114 driver checks).
- Novos C#: análise sintática com tree-sitter; XAML/csproj/manifest: XML bem formado.
- Novos testes de núcleo: escritos, **não executados**.
- Compilação WPF, execução visual, sensores e Release: **não verificados**.

### Checklist Windows / VM (bloqueia distribuição comercial)

1. Compilar Release e executar testes do núcleo em Windows 10 e 11 x64.
2. Validar arranque normal e segunda instância, redimensionamento e escala 100/150/200%.
3. Confirmar CPU/RAM/volumes contra Task Manager/Explorer; remover e ligar unidades durante Refresh.
4. Testar acessos negados, processos a terminar, ausência de adaptador e journal corrompido.
5. Aplicar/Undo cada tweak com valor existente e ausente; verificar Settings após novo login.
6. Matar o processo após Prepared; reabrir e restaurar. Testar falha de escrita e mudança externa.
7. Usar apenas temporários artificiais na VM; verificar seleção vazia, idade, links/junctions,
   ficheiros bloqueados, alterações durante o scan e resultado de limpeza parcial.
8. Validar todas as ferramentas Windows e UAC. Não executar reparações destrutivas no host.
9. Medir CPU/RAM em repouso em 4/8 GB; confirmar que não há amostragem fora de Dashboard/Hardware.
10. Completar testes de inventário Startup/processos, DNS, USB e hardware antes de ampliar essas funções.
11. Rever licenças, acessibilidade/localização, Threat Model, assinatura Authenticode e instalador.

Ver [plano de migração](../docs/HL_MIGRATION.md) para os próximos incrementos.
