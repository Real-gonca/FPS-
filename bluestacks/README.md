# BlueStacks 5 + Free Fire — 90 FPS e ping baixo

Pacote de otimização para o emulador **BlueStacks 5 (Pie64)** rodando **Free Fire**, com
perfil pronto para o **NVIDIA Profile Inspector (`.nip`)**.

Tudo aqui é reversível: o script de aplicação faz backup dos arquivos originais antes de
trocar qualquer coisa.

---

## 1. O que tem neste repositório

| Arquivo | O que é |
|---|---|
| `bluestacks/bluestacks.conf` | Configuração do BlueStacks já otimizada (substitui `C:\ProgramData\BlueStacks_nxt\bluestacks.conf`) |
| `bluestacks/bluestacks.conf.original` | Seu arquivo original, sem nenhuma alteração (referência/rollback) |
| `bluestacks/Pie64.vbox` | Máquina virtual com **VRAM 12 MB → 128 MB** (opcional, veja a seção 6) |
| `bluestacks/nvidia/BlueStacks-FreeFire-90FPS.nip` | Perfil NVIDIA para **NPI 2.3.0.13 ou mais novo (inclui o 3.x)** |
| `bluestacks/nvidia/BlueStacks-FreeFire-90FPS-NPI-2.1.nip` | Mesmo perfil no formato antigo (`ArrayOfProfile`), para NPI 2.1.x |
| `bluestacks/nvidia/gerar-nip.py` | Script que gera os `.nip` (documenta IDs e valores) |
| `bluestacks/scripts/Aplicar-Otimizacoes.ps1` | Aplica tudo com backup automático |
| `bluestacks/scripts/Restaurar-Backup.ps1` | Volta ao estado anterior |

---

## 2. Antes de começar (importante)

1. **Feche o BlueStacks por completo** — inclusive o ícone na bandeja do Windows.
   Se o emulador estiver aberto, ele regrava o `bluestacks.conf` ao sair e desfaz tudo.
2. Você precisa de **PowerShell como Administrador** (a pasta `C:\ProgramData` é protegida).
3. **Faça backup** — o script já faz, mas se preferir manualmente: copie
   `C:\ProgramData\BlueStacks_nxt\bluestacks.conf` para outro lugar.

---

## 3. Aplicar a configuração do BlueStacks

Abra o PowerShell como Administrador, entre na pasta `bluestacks\scripts` e rode:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\Aplicar-Otimizacoes.ps1
```

Opções extras:

```powershell
# também aplica o Pie64.vbox (VRAM 128 MB) e exclui o BlueStacks do antivírus
PowerShell -ExecutionPolicy Bypass -File .\Aplicar-Otimizacoes.ps1 -IncluirVBox -ExcluirDefender
```

### Ou faça manualmente (2 minutos)

1. Copie `bluestacks/bluestacks.conf` para `C:\ProgramData\BlueStacks_nxt\`
   (substituindo o existente) — **com o BlueStacks fechado**.
2. Abra o BlueStacks e confirme em **Configurações → Desempenho** que está:
   - **Modo de desempenho:** Alto desempenho
   - **Taxa de quadros:** 90
   - **Taxa de quadros alta:** ativada

### O que foi alterado no `bluestacks.conf`

| Chave | Antes | Depois | Por quê |
|---|---|---|---|
| `bst.instance.Pie64.max_fps` | `120` | `90` | Limite do emulador alinhado com a meta de 90 FPS (frame pacing mais estável que "tentar 120 e cair") |
| `bst.instance.Pie64.cpus` | `3` | `4` | O BlueStacks é pesado em CPU (tradução ARM + OpenGL). 4 núcleos = menos quedas em combate |
| `bst.instance.Pie64.dpi` | `320` | `160` | Menos pixels de interface por frame. Ganho direto de GPU com perda visual pequena |
| `bst.instance.Pie64.show_sidebar` | `1` | `0` | A barra lateral consome renderização e espaço de tela |
| `bst.dns_server2` | `10.0.2.3` | `1.0.0.1` | DNS secundário real (Cloudflare) em vez do resolver interno da VM |
| `bst.feature.programmatic_ads` | `1` | `0` | Anúncios/telemetria do launcher geram tráfego e uso de CPU em segundo plano |
| `bst.feature.app_install_stats` / `usage_stats` | `1` | `0` | Menos envio de dados em background (jitter de rede/CPU) |

Mantido como já estava (e está correto para FPS): `enable_high_fps=1`, `enable_vsync=0`,
`fb_width=1280`, `fb_height=720`, `astc_decoding_mode=disabled`, `graphics_engine=aga`,
`graphics_renderer=gl`, `prefer_dedicated_gpu=1`, `enable_mem_dedup=1`.

> **Se o seu PC tiver 4 threads ou menos**, volte `cpus` para `"3"` — usar todos os núcleos
> para o emulador engasga o Windows e piora o resultado.
> **Se achar a interface pequena**, troque `dpi` para `"240"` (meio-termo).

Depois de aplicar, **abra o BlueStacks** e confira em **Configurações → Desempenho** que a
taxa de quadros ficou em 90. O emulador só assume mudança de FPS depois de reiniciar.

---

## 4. Importar o perfil da NVIDIA (`.nip`) — o "tuning" da GPU

### 4.1 Baixe o NVIDIA Profile Inspector

- Link oficial: **https://github.com/Orbmu2k/nvidiaProfileInspector/releases**
- Baixe o `.zip` da versão mais recente, extraia e rode `nvidiaProfileInspector.exe`
  **como Administrador** (ele precisa escrever no banco de perfis do driver).

### 4.2 Qual arquivo usar

| Sua versão do NPI | Arquivo |
|---|---|
| **2.3.0.13 até 3.x** (o normal hoje) | `BlueStacks-FreeFire-90FPS.nip` |
| **2.1.x** (bem antigo) — ou se o import reclamar de XML | `BlueStacks-FreeFire-90FPS-NPI-2.1.nip` |

### 4.3 Como importar

1. Abra o NPI como Administrador.
2. Clique no botão **Import profile(s)** (ícone de pasta com seta, na barra de cima) —
   ou menu **File → Import**.
3. Selecione o `.nip`. O NPI vai criar o perfil **`BlueStacks 5 - Free Fire 90 FPS`**
   já vinculado ao executável **`HD-Player.exe`**.
   - Se ele perguntar entre *Merge* e *Replace*: como é um perfil novo, **Replace** está ok.
   - Se avisar que `HD-Player.exe` já pertence a outro perfil, use **Merge** (ou remova o
     app do outro perfil antes).
4. Clique em **Apply changes** (o botão de "check" verde). **Sem esse clique nada é gravado.**
5. Confira: na lista de perfis selecione o perfil criado e veja se os valores aparecem.

### 4.4 Importação silenciosa (opcional, via linha de comando)

Abra o CMD como Administrador na pasta do NPI:

```bat
nvidiaProfileInspector.exe -silentImport "C:\caminho\BlueStacks-FreeFire-90FPS.nip"
```

### 4.5 O que o perfil muda

| Configuração | Valor | Efeito |
|---|---|---|
| Power Management - Mode | Prefer maximum performance | GPU não cai de clock no meio da partida |
| Vertical Sync | Force off | Libera passar de 60 FPS e reduz latência |
| Maximum Pre-Rendered Frames | 1 | Menos frames na fila = menos input lag |
| Threaded Optimization | On | Melhor uso de múltiplos núcleos pelo driver |
| Texture Filtering - Quality | High performance | Alivia a filtragem de textura (é o "baixar um pouco o gráfico" no driver) |
| Triple Buffering | Off | Irrelevante com VSync off; evita buffer extra |
| Frame Rate Limiter V3 | Off | O driver não limita; quem limita é o BlueStacks (90) |
| Ultra Low Latency - Enabled | On | Caminho de agendamento de baixa latência |
| Shader Cache - Enabled (OpenGL) | On | O BlueStacks renderiza em OpenGL: cache evita "engasgo" ao compilar shader |
| Shader Cache - Cache Size | 1 GB | Mais espaço para o cache de shaders |

Todos os IDs usados foram tirados do `CustomSettingNames.xml` do próprio NPI, ou seja,
existem no banco do driver — não são valores inventados.

> **Atenção:** se você usa **NVIDIA Control Panel → Gerenciar configurações 3D → Configurações
> do programa**, vai ver o `HD-Player.exe` lá com esses valores. Não precisa mexer em nada.

---

## 5. Ajustes dentro do Free Fire (fazem tanta diferença quanto o emulador)

Em **Configurações → Gráficos**:

- **Gráficos:** `Suave` (Smooth) ou `Padrão` — é o que mais aumenta FPS
- **Taxa de quadros (High FPS):** `Alto` / `Ultra (90)` se aparecer a opção
- **Sombras:** desligadas
- **Texturas de alta resolução:** desligadas
- **Modo desempenho:** ligado (reduz aquecimento/engasgo)
- **Brilho/efeitos:** baixos

> **Realidade sobre os 90 FPS:** o Free Fire "normal" tem limite de **60 FPS** na maioria
> dos aparelhos. A opção de 90 FPS existe no **Free Fire MAX** e só aparece para modelos
> na lista de dispositivos suportados (é a Garena que libera, não o emulador). O ajuste
> que fizemos garante que **nada no PC/emulador limite você a 60** — se o jogo oferecer
> 90, você chega nos 90.

### Checklist se o FPS travar em 60

1. Monitor/Windows em **60 Hz**? Windows → Configurações → Sistema → Tela →
   Configurações de vídeo avançadas → **Taxa de atualização**: coloque 90/120/144 Hz se o
   monitor suportar.
2. No BlueStacks: **Configurações → Desempenho → Taxa de quadros = 90** e **Taxa de quadros
   alta = ativada**.
3. Contador de FPS do BlueStacks ligado (já está no conf: `enable_fps_display=1`) —
   ele mostra o FPS real do emulador, separado do FPS do jogo.
4. Se o contador do BlueStacks mostra 90 mas o do jogo mostra 60, o limite é **do jogo**
   (veja o aviso acima).
5. Windows em **Modo de energia: Alto desempenho** e notebook na tomada.

---

## 6. Sobre o `Pie64.vbox` (opcional)

Única alteração: `VRAMSize` de **12 MB para 128 MB**.

O que você precisa saber antes de aplicar:

- O BlueStacks **regrava esse arquivo** a cada inicialização, usando as opções do
  `bluestacks.conf`. Se ele sobrescrever, nada quebra — você só perde o ajuste.
- Com 12 MB a VM já renderiza 1280×720 sem problema; o ganho real é pequeno e aparece
  mais se você subir a resolução depois.
- O que **realmente** pesa na GPU é a resolução do framebuffer (`fb_width`/`fb_height`),
  e ela já está em 1280×720 — o ponto ideal para 90 FPS.
- Aplique só se quiser: `.\Aplicar-Otimizacoes.ps1 -IncluirVBox` (com o BlueStacks fechado).

Não mexi em `Memory` (5120 MB) nem em `CPU count` (4) porque os valores atuais já estão
coerentes com o `bluestacks.conf` otimizado.

---

## 7. Ping de 60–90 ms: o que funciona e o que é lenda

**O emulador não é o gargalo do seu ping.** O BlueStacks usa NAT e adiciona ~1–3 ms. O que
define 60, 90 ou 250 ms é a **rota entre a sua internet e o servidor da Garena**.

### Funciona de verdade

- **Cabo de rede** em vez de Wi-Fi (Wi-Fi em 2,4 GHz adiciona 10–40 ms de jitter).
  Se for Wi-Fi, use **5 GHz** e fique perto do roteador.
- **Nada baixando/streamando** enquanto joga: Windows Update, OneDrive, Steam, Epic,
  torrent, YouTube em 4K na casa toda. Isso é o que mais estraga ping no dia a dia.
- **Windows Update → Opções avançadas → Otimização de Distribuição:** desligue
  "Permitir downloads de outros PCs" (envia dados para a internet enquanto você joga).
- **QoS no roteador:** priorize o IP do seu PC (ou o serviço de jogos). Se o roteador tiver
  "SQM/fair queueing", ative — costuma derrubar o jitter.
- **Servidor mais próximo:** se o jogo/região permitir escolher, use o servidor mais perto
  (África do Sul/Oriente Médio costumam ser melhores para Angola que Europa).
- **Firewall/antivírus:** exclua a pasta do BlueStacks (o script faz com `-ExcluirDefender`).

### Não funciona (pode até piorar)

- "DNS gamer" (ExitLag, NoPing, DNS 1.1.1.1 etc.) **não muda o ping dentro da partida**.
  DNS só resolve nomes, e o Free Fire joga por **UDP** com IP direto. O 1.1.1.1 que está no
  conf ajuda a abrir o jogo/login mais rápido, não o ping em si.
- "Tweak de Nagle/TcpAckFrequency" no registro é para **TCP**; o jogo usa UDP. Não resolve.
- VPN grátis: quase sempre aumenta o ping. VPN paga só ajuda se a rota da sua operadora
  para o servidor for realmente ruim — teste com e sem, medindo de verdade.

### Como medir de forma honesta

- Use o **indicador de ping do próprio jogo** (se existir) ou o app de rede do BlueStacks.
- No PC, rode `ping -t <ip-do-servidor>` (descubra o IP no Monitor de Recursos →
  Rede → processos do BlueStacks) e olhe a **média** e o **jitter**, não um pico isolado.
- Meta realista: **60–90 ms** só é possível se o servidor estiver relativamente perto.
  Angola → servidores da Europa costumam dar **130–200 ms**, e nenhum ajuste de emulador
  muda física/roteamento. Se você já está em 90–110 ms com cabo e rede limpa, está no
  limite do que a sua rota permite.

---

## 8. Como reverter tudo

```powershell
PowerShell -ExecutionPolicy Bypass -File .\Restaurar-Backup.ps1
```

Isso restaura o `bluestacks.conf` (e o `.vbox`, se você aplicou) do backup mais recente.

Para remover o perfil da NVIDIA: abra o NPI como Administrador, selecione
**`BlueStacks 5 - Free Fire 90 FPS`** na lista e clique em **Remove profile** (ou
`Delete profile`), depois em **Apply changes**.

---

## 9. Perguntas rápidas

**O BlueStacks ficou com a interface pequena.**
Troque `bst.instance.Pie64.dpi="160"` por `"240"` no `bluestacks.conf` (com o emulador fechado).

**Meu PC tem 4 núcleos/4 threads e ficou pior.**
Volte `bst.instance.Pie64.cpus` para `"3"`.

**Deu tela preta ou o jogo não abre depois das mudanças.**
Rode o `Restaurar-Backup.ps1`. Se quiser testar outro caminho de renderização, em
**Configurações → Gráficos → Renderizador gráfico**, troque `OpenGL` por
`Vulkan e OpenGL` (no conf: `graphics_renderer="vlcn"` em vez de `"gl"`) e compare o FPS
no contador. Em placas NVIDIA modernas o Vulkan às vezes rende mais; em outras, o OpenGL
é mais estável.

**Posso usar 240 FPS no BlueStacks?**
Pode, mas só faz sentido com monitor de 240 Hz e um PC que sustente isso. Com meta de 90,
travar em 90 dá frame time mais constante do que "soltar" em 240 e oscilar.

**Isso viola alguma regra da Garena?**
Não. São ajustes de desempenho do emulador e do driver de vídeo — nada de modificar o jogo,
memória ou arquivos do Free Fire.

**Não tenho placa NVIDIA.**
O `.nip` é só para NVIDIA. O resto (`bluestacks.conf`) funciona em qualquer GPU; nesse caso
use o painel da AMD/Intel para colocar o `HD-Player.exe` em "Alto desempenho".

**O perfil vale para o Multi-Instance Manager também?**
Não. Ele é vinculado só ao `HD-Player.exe`, que é quem renderiza o jogo — é o que importa
para FPS. Se quiser incluir o gerenciador de instâncias, no NPI use **Add application** e
adicione `HD-MultiInstanceManager.exe` e `BlueStacks.exe` ao mesmo perfil.
