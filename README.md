# Range Lab — treinador de mira e controlo de recoil

Ginásio de mira **100% offline no browser**. Serve para ganhar a skill a sério —
memória muscular de spray, tracking, flicks e tempo de reação — sem tocar em
qualquer jogo.

> **Fair play.** O Range Lab não lê memória de jogos, não injeta código, não usa
> macros ligadas a um jogo, não se liga a servidores e não corre enquanto jogas.
> É uma página web com um canvas e um rato. O que ganhas aqui é teu e passa em
> qualquer verificação de anti-cheat, inspeção de dispositivo ou revisão de
> replay de um torneio.
>
> Aim assist externo, "no recoil" por software e qualquer coisa desenhada para
> **não parecer** batota estão fora de questão: violam os termos de serviço, dão
> ban de conta e de dispositivo, desqualificam de campeonatos com devolução de
> prémios e roubam a competição a quem treina a sério.

---

## Arrancar

Precisas de servir os ficheiros por HTTP (os módulos ES não funcionam a partir
de `file://`).

```bash
# opção 1 — python
python3 -m http.server 8080 --bind 0.0.0.0

# opção 2 — npm
npm start
```

Depois abre <http://localhost:8080>.

Não há instalação, não há `node_modules`, não há build. Zero dependências.

## Testes

A lógica (padrões de recoil, simulação de séries, métricas e pontuações) tem
testes que correm em Node, sem browser:

```bash
npm test          # ou: node tests/logic.test.mjs
```

---

## Modos

| # | Modo | Rato | O que treina |
|---|------|------|--------------|
| 1 | **Controlo de Recoil** | capturado | Puxar contra o padrão de spray e manter o grupo fechado. Mostra o "fantasma" (onde as balas iriam sem compensar) ao lado da tua mira real. |
| 2 | **Tracking** | capturado | Manter a mira colada a um alvo em movimento (strafe, errático, vertical). |
| 3 | **Flicks** | cursor livre | Movimento rápido e reto até um alvo novo; mede precisão, tempo, eficiência de trajetória e overshoot. |
| 4 | **Grelha** | cursor livre | Velocidade de aquisição, troca de alvo e re-centragem. |
| 5 | **Reação** | cursor livre | Tempo de resposta puro + disciplina de gatilho (caixas vermelhas = aliado, não disparar). |

Atalhos: <kbd>1</kbd>–<kbd>5</kbd> mudam de modo · <kbd>R</kbd> reinicia a série ·
<kbd>ESC</kbd> liberta o rato.

Existe ainda uma **Rotina diária de 15 min** (botão no topo) que encadeia os cinco
modos na ordem certa de aquecimento.

## Calibrar o recoil ao TEU spray

Os padrões incluídos são **arquétipos genéricos** (SMG / rifle / DMR / LMG) — não
são dados de nenhum jogo. Para o treino se transferir:

1. Vai ao campo de treino do jogo e esvazia um carregador numa parede a ~10 m
   **sem compensar nada**.
2. Observa a forma: sobe muito nos primeiros tiros? Deriva para a direita e volta?
   Abre no fim do carregador?
3. No Range Lab, escolhe o perfil mais parecido e ajusta **Subida**, **Deriva**,
   **Dispersão** e **Cadência** até o fantasma vermelho ter a mesma forma.
4. Deixa "Padrão aleatório" **desligado** enquanto memorizas; liga-o só quando já
   fechas o grupo de forma consistente.

Os valores ficam guardados no browser.

## Métricas que cada série devolve

- **Controlo de Recoil** — erro médio/P90, tamanho do grupo, % na zona útil, viés
  vertical e horizontal (diz-te se estás a puxar a menos ou a mais), erro do
  fantasma para comparação, e um gráfico de erro tiro a tiro.
- **Tracking** — % de tempo no alvo, melhor sequência, tempo médio de aquisição e
  suavidade (penaliza correções em dente de serra).
- **Flicks** — precisão, tempo médio, eficiência de trajetória e overshoot.
- **Grelha** — alvos/segundo, precisão e tempo médio por alvo.
- **Reação** — média, melhor, P90 e fogos amigos.

Cada série termina com 1–3 **conselhos automáticos** baseados nos teus números
(ex.: "estás a puxar pouco para baixo", "muito overshoot, baixa a sensibilidade").

Tudo fica em **Progresso**, com evolução por modo e histórico completo. Podes
exportar em JSON nas Definições.

## Estrutura

```
index.html
css/style.css
js/
  app.js              arranque, navegação, toasts
  patterns.js         arquétipos genéricos de recoil + gerador de padrões
  core/
    engine.js         canvas, ciclo de render, rato/teclado, pointer lock, som
    mode.js           classe base dos modos + controlos de opções
    draw.js           primitivas de desenho (alvos, miras, gráficos)
    util.js           matemática, PRNG determinístico, armazenamento local
  modes/              recoil.js  tracking.js  flick.js  grid.js  reaction.js
  views/              train.js  stats.js  settings.js  guide.js
tests/logic.test.mjs  testes de lógica (Node, sem browser)
```

## Nota sobre otimização de PC

A ideia original deste repositório continua válida e está no separador **Guia**:
fps estáveis batem fps máximos, aceleração do rato desligada, V-Sync desligado,
polling rate 500–1000 Hz, um cm/360 fixo e consistente entre o treino e o jogo.
São ganhos legais e contam para o mesmo objetivo.
