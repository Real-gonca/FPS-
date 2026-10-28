# O que software consegue (e não consegue) fazer com um mouse

Este documento existe porque a maior parte dos "mouse drivers" na internet promete coisas
que o hardware não permite. O que segue é a parte que costuma ficar de fora.

---

## 1. DPI

**DPI é uma propriedade do sensor.** O chip óptico conta deslocamentos e reporta
"counts" (ou "mickeys"): quantas unidades o sensor andou desde o último relatório. Esse
número vem do firmware; nenhum driver de kernel o altera.

O que existe de verdade:

| O que se chama de "mudar DPI" | O que é de fato |
|---|---|
| Botão de DPI no mouse | o firmware muda a **resolução interna** e a contagem reportada |
| Synapse/G HUB mudando DPI | comando proprietário ao firmware (SDK do fabricante) |
| **"DPI stage" em software** | multiplicar o vetor de movimento depois de recebido |

PrecisionCurve faz o terceiro. Consequências que importam:

- **Não há ganho de informação.** Se o sensor reporta 10 counts, não existe software que
  invente detalhe entre eles. Aumentar o "DPI" por software **amplifica o erro de
  quantização** do sensor, exatamente como aumentar a sensibilidade no jogo.
- **Diferente de mudar DPI no firmware**: em DPI/2 real, o sensor reporta menos counts e o
  Windows recebe menos eventos (jitter relativo maior). Em escala 0,5× por software, os
  eventos continuam chegando e são somados/truncados — o resultado é mais suave, não igual.
- **A contagem bruta não muda** (requisito do projeto): o driver multiplica na saída,
  preservando o resíduo sub-count para não perder movimento lento.

Por isso a GUI mostra **"DPI efetivo (software)"**, e não "DPI": é honestidade de rótulo.

## 2. Polling rate

O intervalo de relatório é `bInterval`, um campo do **endpoint descriptor USB**. Ele é
negociado entre o dispositivo e o controlador USB; o driver de classe HID não o expõe ao
user mode, e nenhuma API do Windows permite alterá-lo.

| Alegação comum | Realidade |
|---|---|
| "o app muda o polling para 1000 Hz" | não existe API; o mouse decide |
| "driver aumenta o polling" | no máximo lê o que chega |
| "polling rate limiter" | descarta/atrasa relatórios — **adiciona latência**, o oposto do que se quer |

PrecisionCurve:

1. **Mede** a taxa efetiva por contagem de `WM_INPUT` (Raw Input) em janelas de 500 ms e
   mostra o pico — o valor que converge para o polling real durante um swipe.
   A GUI escreve "measured", não "configured".
2. **Estabiliza o tempo** no kernel: mantém uma média exponencial do intervalo entre
   relatórios e, quando um relatório chega >25% fora dessa média, usa a média para calcular a
   velocidade (flag `PC_FLAG_DT_SMOOTHING`). Isso evita picos de ganho quando o driver USB
   agrupa ou atrasa relatórios.
3. **Não faz buffering de relatórios.** Um smoother de verdade exigiria segurar pacotes e
   liberá-los num timer de alta resolução — o que troca jitter por latência. Se você quiser
   essa opção, ela precisa de um timer por dispositivo no kernel (ver roadmap).

Sem esta nota, "polling smoother" viraria marketing. Aqui é estabilização de estimativa de
tempo, e está implementado de forma verificável nos testes (`TestFramePacing`).

## 3. Aceleração do Windows

`win32k` aplica *pointer ballistics* depois que o driver de classe entrega o pacote. Ou seja:

```
sensor → HID → mouclass → [PrecisionCurve aplica a curva] → win32k aplica a ballistics
```

A curva desenhada descreve **a parte do PrecisionCurve**, não o comportamento final do
cursor. Para ver a curva na prática, desligue *"Aperfeiçoar a precisão do ponteiro"*
(Configurações → Mouse → Opções adicionais). A GUI não faz isso por você (é uma preferência
do sistema, por usuário, e mexer nela sem avisar é rude).

## 4. Latência

Honestamente:

- o filtro acrescenta o custo de algumas dezenas de instruções (medido: 215–240 ns/pacote no
  harness host com shim, ~0,2% de um núcleo a 8 kHz);
- a latência dominante **não** está aí: está no polling (125 µs a 8 kHz, 1 ms a 1000 Hz), no
  compositor e no frame do jogo;
- "near-zero latency" é uma forma de dizer "menos que o período de polling". É o caso.

## 5. SDKs de fabricante

Mudar DPI/polling de verdade exige falar com o firmware:

- Logitech (G HUB / Logi SDK), Razer (Synapse/Chroma SDK), SteelSeries (GG SDK), Corsair
  (iCUE SDK).
- São **proprietários**, exigem licença/assinatura, cobrem apenas os modelos suportados e
  mudam sem aviso.
- Integrar isso significaria um código que funciona em 3 marcas e quebra nas outras — o
  oposto de um utilitário universal.

Se um dia for integrar, o desenho correto é: um plugin por fabricante, atrás de uma
interface `IHardwareBackend` no app, chamando o SDK em user mode. O driver não muda.

## 6. Anti-cheat e ética

- Anti-cheats (Vanguard, EAC, BattlEye, FACEIT) **detectam** drivers de kernel que alteram
  input. Não é teoria: é a assinatura clássica de macro/aim-assist.
- Mesmo um driver legítimo e assinado pode ser sinalizado, porque a técnica é a mesma.
- O que este projeto faz é *input shaping* (curva de aceleração), como Raw Accel. O que ele
  **não** faz e não deve ser adaptado para fazer: correção automática de mira, snap para
  inimigo, macro de recoil. Snap de **eixo** (horizontal/vertical/diagonal) não é snap de
  alvo — é uma restrição geométrica do movimento do próprio usuário.
- Usar em conta competitiva é risco seu. Não use em torneio sem autorização do organizador.

## 7. O que este projeto deliberadamente não faz

| Não faz | Por quê |
|---|---|
| alterar DPI físico | impossível por software genérico |
| alterar polling rate | `bInterval` é do endpoint USB/firmware |
| segurar/reenviar relatórios | adiciona latência; fora do escopo |
| esconder-se de anti-cheat | seria malware; não é o objetivo |
| configurar por dispositivo | ABI atual é global (roadmap) |
| ler o descriptor USB completo | exige abrir a interface HID e parse de relatório; o ganho não paga o risco |
| funcionar em ARM64 hoje | o código compila, mas não há projeto de build/teste para ARM64 |

## 8. Como comparar com Raw Accel

PrecisionCurve foi inspirado no Raw Accel, mas:

- as curvas têm **parametrização própria** (documentadas em `shared/precisioncurve_curves.h`);
  os nomes Linear/Natural/Motivity/Jump são familiares, as fórmulas não são bit a bit as dele;
- a LUT é compilada em user mode e o kernel só interpola — decisão de segurança e
  testabilidade deste projeto;
- há DPI stages, snap de eixo, perfis por processo e serviço, que não são o foco do Raw Accel.

Se você quer fidelidade exata ao Raw Accel, use o Raw Accel. Se quer entender (e modificar)
a arquitetura inteira, é para isso que este repositório existe.
