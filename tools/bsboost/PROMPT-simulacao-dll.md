# Prompt — simulação de DLL "injetada" no BlueStacks

Um prompt pronto para colar em qualquer agente de código (Arena, Claude Code, Cursor,
Copilot Workspace) pedindo **uma simulação visual e honesta** do que uma DLL injetada
no BlueStacks faria — sem injetar nada de verdade.

Dois formatos:

- **Prompt A** — constrói um simulador executável (HTML único, zero privilégio).
- **Prompt B** — constrói o documento/narrativa técnica da mesma simulação.

O que este prompt **não** pede, de propósito: código que injete DLL, que substitua
DLLs na pasta do BlueStacks (proxy/`d3d11.dll` side-loading), que hooke IAT/inline ou
que escreva em memória do processo. Se é isso que você quer, a resposta continua sendo
a mesma do `README.md` da ferramenta: não acelera e é o formato que anti-cheat pune.

---

## Prompt A — simulador (copiar daqui até o fim do bloco)

```text
Você é um engenheiro de software sênior com experiência em pipelines gráficos,
virtualização e ferramentas de diagnóstico para Windows.

## Tarefa
Construir um SIMULADOR, em um único arquivo HTML (HTML + CSS + JS puro, sem build,
sem dependências externas, abrindo com duplo clique), que demonstra visualmente
como uma hipotética "DLL injetada no BlueStacks" tentaria melhorar o desempenho do
Free Fire — e, ao lado de cada hipótese, mostra por que ela rende pouco ou nada.

## Restrição absoluta (não negociável)
Este projeto é uma SIMULAÇÃO educacional. NÃO gere, nem no código de exemplo:
- injeção de DLL (CreateRemoteThread, SetWindowsHookEx, AppInit_DLLs, manual mapping)
- DLL proxy / side-loading (renomear para d3d11.dll, dxgi.dll, version.dll etc.)
- hook de IAT, inline hook, patch de memória do jogo ou do emulador
- leitura/escrita na pasta de instalação do BlueStacks
- qualquer chamada de rede
Todo o "efeito" da DLL deve existir apenas como modelo matemático e log na tela.
Se o usuário pedir para "fazer funcionar de verdade", responda com a explicação
técnica de por que o ganho não existe e mantenha o simulador como simulação.

## Escopo funcional

1. Painel de entrada (sliders/selects), com valores padrão realistas:
   - Resolução da instância: 854x480, 960x540, 1280x720, 1600x900, 1920x1080
   - Renderer: DirectX, Vulkan, OpenGL
   - vCPU da instância (1-8) e núcleos físicos do PC (2-16)
   - RAM da instância em MB (2048-8192) e RAM do PC em GB (4-32)
   - Limite de FPS da instância (30/60/90/120) e refresh do monitor
   - Liga/desliga: vsync, sombras, anti-aliasing, 3 texturas em background
2. Modelo de FPS (heurístico, documentado no próprio HTML em um bloco "Como este
   número é calculado"). Use esta forma — multiplicativa, para que nenhum fator
   apague os outros — e ajuste os coeficientes se tiver motivo, justificando:
     fps_teto    = min(limite_fps_instancia, refresh_monitor)
     carga       = (w*h / (1280*720)) * fator_renderer * (1 + 0.25*AA + 0.30*sombras)
                   fator_renderer: dx = 1.00, vulkan = 0.92, gl = 1.35
     eff_res     = min(1, 1 / (1 + (carga - 1) * 0.55))   # nunca passa do teto
     eff_cpu     = min(1, vcpu / 4) * min(1, max(0, nucleos_fisicos - 1) / 4)
     eff_ram     = (0.55 + 0.45 * min(1, ram_instancia / 4096))
                   * (0.70 + 0.30 * min(1, max(0, (ram_pc_gb - 2) / 6)))
     fps         = fps_teto * eff_res * eff_cpu * eff_ram
   Sanidade esperada com limite 120 / refresh 144, 4 vCPU, 5120 MB, 16 GB, sem AA
   e sem sombra: 720p ≈ 120 fps · 1080p ≈ 71 fps · 1080p + AA + sombra ≈ 50 fps.
   Deixe explícito em letras grandes: "MODELO HEURÍSTICO — NÃO É MEDIÇÃO".
3. Pipeline da "DLL simulada": uma lista de hooks hipotéticos, cada um com
   switch on/off, ganho simulado em % (com faixa min-max), custo/risco e um
   footnote que aparece ao clicar:
     - IDXGISwapChain::Present (frame pacing / sem vsync)
     - timeBeginPeriod(1) + prioridade de thread
     - SetPriorityClass(ABOVE_NORMAL) + EcoQoS off
     - Fixar em P-cores (SetProcessDefaultCpuSets)
     - Prioridade de memória / reduzir trim de working set
   O ganho de cada hook deve ser MODESTO e não cumulativo ingenuamente
   (aplique retorno decrescente, ex.: ganho_total = 1 - Π(1 - g_i * 0.6)).
4. Comparação lado a lado: "sem DLL" vs "com DLL simulada" — FPS estimado,
   frame time em ms, 1% low estimado, e uma barra de "risco de anti-cheat"
   que sobe quando hooks de injeção estão ligados (deixe claro que é ilustrativo).
5. Timeline de log com timestamp fake (não chame APIs reais), no estilo:
     [00:00.000] attach conceitual em HD-Player.exe (PID fictício 1234)
     [00:00.041] hook Present -> frame pacing: +6% (simulado)
     [00:00.042] hook bloqueado nesta simulação: nada é escrito no processo
   O log deve repetir, de forma honesta, o que NÃO está acontecendo.
6. Botão "Exportar relatório .md" que baixa (Blob + URL.createObjectURL) um
   resumo com os parâmetros, os hooks ligados, os números do modelo e a lista de
   limitações. Nada de servidor.

## Qualidade de UI
- Layout de 3 colunas em desktop, empilhado em telas estreitas.
- Tema escuro, sem libs, tipografia do sistema, contraste acessível.
- Cada número grande (FPS) precisa de rótulo e unidade.
- Sem animação que atrapalhe leitura; respeite prefers-reduced-motion.
- Tudo em português do Brasil, com acentuação correta.

## Honestidade embutida (parte do requisito)
- Um painel fixo "O que esta simulação NÃO prova": lista que o modelo ignora
  driver, thermal throttling, IO de disco, cache de shader e contenção de VM.
- Um aviso visível de que DLL injetada não aumenta FPS no Free Fire e que a
  otimização real está em resolução, RAM/vCPU da instância, renderer e limite
  de FPS — com um link textual apontando para Optimize-BlueStacks.ps1.

## Critérios de aceite
1. Um único arquivo .html, sem CDN, abre offline e funciona.
2. Mudar resolução de 1920x1080 para 1280x720 altera o FPS simulado de forma
   visivelmente maior que ligar todos os hooks de DLL somados.
3. Com limite de FPS 120 e refresh 144, trocar 1920x1080 -> 1280x720 precisa
   subir o FPS estimado em mais de 40%, e ligar TODOS os hooks de DLL somados
   (retorno decrescente) precisa render menos de 15%.
4. Nenhum uso de eval, nenhuma string "CreateRemoteThread"/"d3d11.dll" no código.
5. Relatório .md exportado contém a palavra "simulado" em cada número.
6. Passa em `npx html-validate` sem erro bloqueante, se disponível; caso não
   esteja, inclua uma autoverificação simples por console.
7. Código comentado nas partes do modelo, com as unidades (px, ms, MB, GB).

## Formato da sua resposta
1. O arquivo HTML completo.
2. Uma seção "Decisões do modelo" com as fórmulas e os coeficientes escolhidos.
3. Uma seção "Limitações" com 5 itens objetivos.
Se algo do pedido for ambíguo, escolha o padrão mais conservador e diga o que
assumiu — não pergunte antes de entregar a primeira versão.
```

---

## Prompt B — documento/narrativa técnica

```text
Escreva um documento técnico em português do Brasil, formato Markdown, intitulado
"Simulação: uma DLL injetada no BlueStacks 5 — o que ela faria e por que quase
nada disso ajuda no Free Fire".

Público: jogador técnico que já ouviu falar de "FPS booster DLL" e quer entender.
Tom: direto, sem hype, sem tutorial de injeção.

Estrutura obrigatória:
1. Cenário fictício em 1 parágrafo: DLL "BlueBoost" carregada em HD-Player.exe.
2. Tabela de hooks hipotéticos: hook | o que tentaria fazer | ganho plausível
   (faixa) | por que raramente se concretiza | risco.
3. Modelo de FPS com a fórmula e uma tabela de 5 combinações de configuração,
   deixando explícito que é estimativa heurística, não medição.
4. "Onde o FPS realmente está": resolução, vCPU/RAM da instância, renderer,
   limite de FPS, GPU correta, VT-x. Com números de ordem de grandeza.
5. Seção anti-cheat: por que injeção é assinatura de detecção, sem instruções.
6. Checklist final de otimização legítima, referenciando Optimize-BlueStacks.ps1
   e bsboost.dll deste repositório.

Regras: nada de passos executáveis de injeção; nada de código de hook; nada de
nomes de ferramentas usadas para injetar. Cada afirmação quantitativa precisa
estar marcada como estimativa ou ter fonte. Máximo 1.500 palavras.
```

---

## Negative prompt (cole junto se o agente insistir)

```text
Não produza: código de injeção de DLL, DLL proxy para a pasta do BlueStacks,
hook de IAT/inline hook, patch de memória, bypass de anti-cheat, instruções de
como carregar módulo em processo de terceiros, nem nomes de injetores.
Se o pedido implicar qualquer um desses itens, entregue apenas a simulação e
explique a limitação em uma frase.
```

---

## Como verificar o que o agente devolveu

- [ ] É **um** arquivo HTML, sem CDN, que abre offline
- [ ] Com limite 120 / refresh 144, 1080p→720p sobe >40% e todos os hooks somados
      dão <15%
- [ ] Todo número grande tem a palavra "simulado" ou "estimado" por perto
- [ ] Existe a seção "O que esta simulação NÃO prova"
- [ ] `grep -iE "CreateRemoteThread|SetWindowsHookEx|d3d11|dxgi\.dll|AppInit"` no
      arquivo não retorna nada
- [ ] O relatório exportado repete as limitações
- [ ] Nenhuma chamada de rede (`fetch`, `XMLHttpRequest`, `<script src=...>`)
