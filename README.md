# FPS-

Painel em CMD para otimizar **teclado e mouse** e ajustar **sensibilidade** no
**Blood Strike** e no **Free Fire** rodando em emulador — com foco em resposta,
rapidez e estabilidade da sensi.

As otimizações num PC melhoram o seu desempenho e eficiência. Basicamente, elas
ajudam o computador a funcionar mais rápido, de forma mais suave e a usar os
seus recursos (como memória e processador) de maneira mais inteligente.

---

## Começar

```
painel\FPS-Painel.bat
```

Clique com o botão direito → **Executar como administrador** (o painel também
pede elevação sozinho). Windows 10/11.

| Opção | O que faz |
|---|---|
| **1** | Aplica tudo: backup + mouse + teclado + sistema + emulador |
| **2** | Backup do estado atual (faça antes de qualquer coisa) |
| **3** | Diagnóstico — mostra o que está ativo agora |
| **4** | Mouse: aceleração off, ponteiro 1:1 |
| **5** | Teclado: repetição e resposta mais rápidas |
| **6** | Sistema: Game DVR, energia, rede, GPU |
| **7** | Emulador via ADB: sensi do mouse, animações, Hz |
| **8** | Boost do emulador: prioridade de processo |
| **9** | Perfis prontos: RUSH / EQUILIBRADO / PRECISÃO |
| **10** | Sensibilidade: calibração, conversão e calculadora |
| **11** | Guia de sensi — onde mexer no jogo e no emulador |
| **12** | Avançado: GPU dedicada, fullscreen, fila de entrada |
| **13** | Restaurar padrão / desfazer |

**Reinicie o Windows** depois de aplicar: as mudanças de mouse e teclado do
registro só valem após logoff ou reboot. As mudanças via ADB valem na hora.

---

## O caminho curto

1. Opção **[2]** — backup.
2. Opção **[9]** — perfil EQUILIBRADO (ou RUSH / PRECISÃO).
3. Reinicie o Windows.
4. No Blood Strike: `Settings → Sensitivity → Acceleration = Fixed Speed`.
5. Opção **[10]** — meça o giro de 360° e calibre a sensi de verdade.

---

## O limite honesto

O painel **não** muda a sensibilidade dentro do jogo. Esse número fica no
perfil da sua conta, dentro do Android do emulador, e só pode ser digitado na
tela de Settings do próprio jogo.

O que ele faz é eliminar tudo o que deixa a sensi instável — aceleração do
Windows, animações do Android, Game DVR, throttle de rede, plano de energia — e
**calcular o número exato** para você digitar, medindo quantos centímetros o
mouse percorre para dar uma volta de 360°.

Detalhes em [`docs/GUIA-SENSI.md`](docs/GUIA-SENSI.md) e
[`docs/O-QUE-O-PAINEL-FAZ.md`](docs/O-QUE-O-PAINEL-FAZ.md).

---

## Estrutura

```
painel/FPS-Painel.bat     painel interativo (autocontido, sem dependências)
painel/backups/           backups de registro gerados em tempo de execução
docs/GUIA-SENSI.md        guia de sensibilidade e calibração
docs/O-QUE-O-PAINEL-FAZ.md referência técnica, chave por chave
tools/check_bat.py        verificador estático do .bat
```

## Verificação

Não existe `cmd.exe` fora do Windows, então o `.bat` é verificado estaticamente:

```
python3 tools/check_bat.py painel/FPS-Painel.bat          # sintaxe, blocos, rótulos
python3 tools/check_bat.py --math painel/FPS-Painel.bat   # contas de sensibilidade
```

O primeiro checa codificação UTF-8, terminação CRLF, balanceamento de blocos
`if/for ( )`, rótulos de `goto`/`call` inexistentes, `&` fora do escopo do `if`
e `::` dentro de bloco. O segundo extrai as fórmulas que o painel entrega ao
PowerShell e as reavalia com valores de exemplo.
