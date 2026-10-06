# Guia de sensibilidade — Blood Strike e Free Fire no emulador

Este guia acompanha o `painel/FPS-Painel.bat`. Ele separa o que é **fato
verificável** do que é **ponto de partida** para você calibrar.

---

## 1. Por que a sensi parece "instável"

Sensibilidade instável quase nunca é o valor da sensi. É **aceleração**: algum
lugar da cadeia está multiplicando o movimento do mouse pela *velocidade* da
sua mão. Resultado: o mesmo movimento físico vira um giro diferente a cada vez.

A cadeia tem três camadas, e as três podem ter aceleração:

| Camada | Onde | O que fazer |
|---|---|---|
| 1. Windows | `Aprimorar precisão do ponteiro` | **Desligar** (opção [4] do painel) |
| 2. Emulador | Suavização / smoothing de mira no editor de controles | Desligar |
| 3. Jogo | Blood Strike: `Sensitivity → Acceleration` | **Fixed Speed** |
| 3. Jogo | Free Fire: qualquer ajuste automático de mira | Desligar |

Se qualquer uma das três estiver ligada, nenhuma sensi fica estável — e nenhum
número que você digitar vai resolver.

---

## 2. O que o painel realmente controla

O painel controla a **camada 1** e parte da **camada 2**.

**Camada 1 (Windows, via registro):**
- `MouseSpeed`, `MouseThreshold1`, `MouseThreshold2` → `0` (aceleração off)
- `MouseSensitivity` → `10` (ponto 1:1 do Windows, o "6 de 11" da interface).
  Abaixo de 10 o Windows divide seus counts, acima ele multiplica; nos dois
  casos o movimento deixa de ser 1:1 e o arredondamento introduz erro.
- Teclado: `KeyboardDelay=0`, `KeyboardSpeed=31`
- Game DVR off, plano de energia, throttle de rede, HAGS

**Camada 2 (Android do emulador, via ADB):**
- `settings put system pointer_speed` (−7 a 7) — velocidade do cursor do mouse
  dentro do Android
- `peak_refresh_rate` / `min_refresh_rate` — trava a taxa de atualização
- escalas de animação → `0`

> **Limite honesto:** se o seu emulador usa *mira com mouse travado* (mouse
> lock), a mira pode não passar pelo `pointer_speed` do Android — ela passa pelo
> mapeamento de teclas do emulador. Nesse caso o `pointer_speed` só afeta o
> cursor solto, e a sensi da mira vem do editor de controles + sensi do jogo.
> O painel aplica o valor e te mostra os dois lugares; ele não consegue saber
> qual modo o seu emulador está usando.

---

## 3. O que o painel **não** faz

**Ele não muda a sensibilidade dentro do jogo.** Esse número fica no perfil da
sua conta, dentro do Android do emulador, em arquivo privado do app. Não há
comando de Windows nem de ADB sem root que escreva isso com segurança.

O que o painel faz é **calcular o número** para você digitar (opção [10]).

---

## 4. Calibração real: cm por volta de 360°

É a única medição que funciona em qualquer jogo, em qualquer escala de
sensibilidade. Não depende de constante secreta do jogo.

1. Entre no treino, fique parado em pé.
2. Escolha um objeto distante como referência.
3. Mova o mouse na horizontal até dar uma volta completa e voltar a olhar
   exatamente para o mesmo objeto.
4. Marque com fita no mousepad o início e o fim. Meça.
5. Repita 3 vezes e use a média.

**Se as três medidas variarem mais de ~1 cm, ainda há aceleração ligada em
algum lugar.** Corrija isso antes de mexer em número — medir com aceleração
ligada não serve para nada.

### Igualar a sensi entre dois jogos

```
sensi_nova_do_jogo_B = sensi_atual_do_B × (cm360_do_B ÷ cm360_do_A)
```

Você precisa de duas medidas: o cm/360 do jogo A (o que você quer copiar) e o
cm/360 do jogo B **junto com a sensi que estava ativa quando você mediu**.
A opção [10] do painel faz essa conta.

Exemplo verificado pela ferramenta de teste: jogo B está com sensi 85 e gasta
30 cm por volta; o jogo A gasta 24 cm. Nova sensi do B = `85 × 30 ÷ 24 = 106.25`.

---

## 5. Pontos de partida (não são número oficial)

Os valores abaixo são **pontos de partida** para você começar a calibrar. Não
existe configuração oficial publicada pela Garena nem pela NetEase, e qualquer
tabela que afirme "sensi de pro player" está chutando. Use, teste e ajuste de
2 em 2 pontos.

### Free Fire — emulador, teclado e mouse

| Perfil | Geral | Red Dot | 2x | 4x | Mira (sniper) |
|---|---|---|---|---|---|
| Rush | 95 | 90 | 85 | 75 | 55 |
| Equilibrado | 90 | 85 | 80 | 70 | 50 |
| Precisão | 82 | 78 | 72 | 62 | 45 |

A cascata importa mais que o valor absoluto: geral > red dot > 2x > 4x > sniper.

### Blood Strike

| Perfil | Câmera | Disparo | Red Dot | 4x | 8x |
|---|---|---|---|---|---|
| Rush | 80 | 65 | 75 | 55 | 35 |
| Equilibrado | 70 | 58 | 68 | 48 | 30 |
| Precisão | 60 | 50 | 58 | 42 | 26 |

Antes de tocar em qualquer slider:
- `Acceleration` → **Fixed Speed**
- `Global Sensitivity Sync` → **OFF** enquanto você calibra (com Sync ligado,
  mexer em um slider reescala os outros e você perde a referência)

---

## 6. eDPI — e por que ele engana entre jogos

```
eDPI = DPI do mouse × sensibilidade do jogo
```

Serve para comparar setups **dentro do mesmo jogo**. Não serve para converter
Free Fire → Blood Strike: a escala 0–100 do Free Fire não é a mesma do Blood
Strike, e nenhuma delas é a escala de Valorant ou CS. Conversão de verdade é a
medição em cm/360 da seção 4.

---

## 7. O que mais mexe com a "rapidez" da sensi

Ordem de impacto na prática, do maior para o menor:

1. **FPS estável.** FPS oscilando muda o tempo entre o seu movimento e o frame
   que o mostra. 60 FPS fixos valem mais que 120 FPS oscilando.
2. **Aceleração desligada** nas três camadas.
3. **Polling rate do mouse** (500/1000 Hz) e porta USB direta, sem hub.
4. **Plano de energia** e prioridade do processo do emulador — evitam o pico de
   input lag quando o Discord ou o antivírus disputam CPU.
5. Game DVR / gravação em segundo plano desligados.

O painel cobre 2, 4 e 5. FPS estável, polling rate e USB são seus.

---

## 8. Depois de mudar

Jogue **3 partidas de treino sem mexer de novo**. Memória muscular só trava com
repetição; mudar a sensi depois de cada partida ruim é o erro mais comum e o
que mais atrasa a evolução de mira.
