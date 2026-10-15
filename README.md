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
npm start         # ou: python3 serve.py 8080
```

Depois abre <http://localhost:8080>.

Não há instalação, não há `node_modules`, não há dependências externas.

O `serve.py` envia `Cache-Control: no-store` — é de propósito. Com o
`http.server` normal o browser aplica cache heurística e pode continuar a servir
um ficheiro antigo depois de corrigido.

## Build

O browser carrega **um único ficheiro**, `js/app.bundle.js`, gerado a partir dos
módulos em `js/`:

```bash
npm run build
```

Duas razões para o bundle existir:

1. **Cascata de pedidos.** Com 16 módulos separados o browser fazia 17 pedidos
   sequenciais (cada `import` só é pedido depois do pai ser analisado) — ~10 s
   até arrancar atrás de um proxy. Assim são 2 pedidos.
2. **Cache.** O `index.html` referencia o bundle com `?v=<hash do código>`,
   reescrito em cada build. Se o código mudar, o URL muda: é impossível o browser
   servir uma versão antiga.

Os módulos em `js/` continuam a ser a **fonte de verdade** — o bundler é só
concatenação com deteção de colisões de nomes. O `npm test` faz build primeiro e
testa as duas formas (módulos e bundle), portanto um bundle esquecido ou partido
é apanhado imediatamente.

## Testes

```bash
npm test
```

Corre três suites, todas em Node e sem browser:

| Suite | O que cobre |
|-------|-------------|
| `tests/logic.test.mjs` | Padrões de recoil, simulação de séries completas dos 5 modos, métricas, pontuações e aconselhamento (relógio e motor fingidos) |
| `tests/dom.smoke.mjs` | Arranque real da app num DOM fingido: as 4 vistas, os 5 modos, um `draw()` de cada, a rotina diária e a coerência de ids entre HTML e JS |
| `tests/dom.smoke.mjs --bundle` | O mesmo, mas contra o bundle que o browser carrega, incluindo a correspondência do hash no `index.html` |

O smoke test também captura `console.error`, porque o `boot()` engole exceções e
mostra um cartão de erro — sem isso, um arranque falhado parecia um sucesso.

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
index.html              carrega js/app.bundle.js?v=<hash>
css/style.css
serve.py                servidor estático sem cache
tools/bundle.mjs        bundler (zero dependências) -> js/app.bundle.js
js/
  app.bundle.js         GERADO — o ficheiro que o browser carrega
  app.js                arranque, navegação, toasts
  patterns.js           arquétipos genéricos de recoil + gerador de padrões
  core/
    engine.js           canvas, ciclo de render, rato/teclado, pointer lock, som
    mode.js             classe base dos modos + controlos de opções
    draw.js             primitivas de desenho (alvos, miras, gráficos)
    util.js             matemática, PRNG determinístico, armazenamento local
  modes/                recoil.js  tracking.js  flick.js  grid.js  reaction.js
  views/                train.js  stats.js  settings.js  guide.js
tests/
  logic.test.mjs        testes de lógica (Node, sem browser)
  dom.smoke.mjs         smoke test de arranque com DOM fingido
```

Edita sempre os módulos em `js/`, nunca o `app.bundle.js`.

## Nota sobre otimização de PC

A ideia original deste repositório continua válida e está no separador **Guia**:
fps estáveis batem fps máximos, aceleração do rato desligada, V-Sync desligado,
polling rate 500–1000 Hz, um cm/360 fixo e consistente entre o treino e o jogo.
São ganhos legais e contam para o mesmo objetivo.
