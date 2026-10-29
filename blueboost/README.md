# BlueBoost 🚀

**Otimizador de PC fraco com foco em configurar o BlueStacks 5 — sem instalar nada.**

Ferramenta em PowerShell + WinForms que roda em qualquer Windows 10/11 (usa só o que
já vem no sistema). Reúne em um só lugar as otimizações e os tutoriais clássicos de
"PC fraco" aplicados ao BlueStacks 5.

![abas](https://img.shields.io/badge/Windows-10%20%7C%2011-blue) ![deps](https://img.shields.io/badge/depend%C3%AAncias-zero-green)

## Como usar

1. Baixe/copie a pasta `blueboost/` para o PC.
2. **Clique duplo em `BlueBoost.bat`** (ele pede elevação de administrador — necessária
   para gravar o `bluestacks.conf` em `C:\ProgramData`).
3. Feche o BlueStacks, escolha a instância e o preset, clique em **Aplicar preset**.

> A ferramenta detecta sua RAM/CPU e já marca o preset recomendado.

## O que ela faz

### Aba **BlueStacks 5**
Edita o `bluestacks.conf` (com **backup automático** `bluestacks.conf.bak-DATA` antes
de qualquer alteração) usando apenas chaves conhecidas e seguras:

| Preset | CPU | RAM | Resolução / DPI | FPS | Renderizador | Indicado para |
|---|---|---|---|---|---|---|
| **Ultra Leve** | 2 | 1,5–2 GB | 960×540 / 160 | 30 | OpenGL | 4 GB RAM / dual-core |
| **Equilibrado** | 2–4 | 2 GB | 1280×720 / 240 | 60 | OpenGL | 6–8 GB RAM |
| **Desempenho** | 4 | 4 GB | 1280×720 / 240 | 60 | DirectX | 8 GB+ |

Também: fecha os processos do BlueStacks antes de aplicar, mostra a configuração
atual da instância, verifica se a **virtualização (VT)** está ativa e se o Hyper-V
está atrapalhando.

### Aba **Windows**
Otimizações leves e **reversíveis** (o log diz como desfazer cada uma):

- Plano de energia **Alto Desempenho**
- Desativar **Game DVR / captura da Game Bar** + ativar Modo de Jogo
- Efeitos visuais em **"Melhor desempenho"**
- Desativar **transparência**
- **Limpar temporários** (TEMP do usuário e do Windows)
- Abrir o gerenciador de **programas de inicialização** (desativação manual, nada automático)

### Aba **Tutorial**
Guia passo a passo embutido com o que não dá para automatizar:

1. **Ativar VT no BIOS** (a otimização que mais faz diferença) e desativar Hyper-V
2. Atualizar o driver de vídeo
3. Entender o que cada chave do preset muda e por quê
4. Configurações internas do BlueStacks (modo de desempenho, motor gráfico, uma instância só)
5. Hábitos: fechar Chrome/Discord, exceção no antivírus, SSD, Windows Update
6. Solução dos problemas comuns ("Engine won't start", tela preta, restaurar backup)

## Segurança

- **Nunca** altera o `bluestacks.conf` sem criar backup com data/hora ao lado do original.
- Só escreve chaves documentadas da instância (`cpus`, `ram`, `fb_width`, `fb_height`,
  `dpi`, `graphics_renderer`, `max_fps`, `enable_high_fps`, `enable_vsync`,
  `show_sidebar`, `enable_fps_display`).
- As mudanças no Windows são todas por usuário (HKCU) ou triviais de reverter
  (plano de energia), e **nenhum** programa de inicialização é desativado automaticamente.

**Restaurar o BlueStacks ao estado anterior:** feche o BlueStacks, apague
`bluestacks.conf` e renomeie o `bluestacks.conf.bak-DATA` mais recente de volta
para `bluestacks.conf`.

## Estrutura

```
blueboost/
├── BlueBoost.bat   # inicia a ferramenta (clique duplo)
├── BlueBoost.ps1   # aplicativo completo (GUI, presets, tweaks, tutorial)
└── README.md       # este arquivo
```
