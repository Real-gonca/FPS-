# Prompts que fazem a IA produzir projetos com "aspecto humano"

> Manual de copy-paste. Funciona em ChatGPT, Claude, Gemini, Cursor, v0, Lovable, Bolt, etc.
> A ideia central: **a IA não falha por falta de capacidade, falha por falta de restrições.**
> Quanto mais específico for o "mundo" que lhe dás (referências, limites, defeitos permitidos), menos genérico fica o resultado.

---

## 1. Porque é que o resultado "cheira a IA"

Antes dos prompts, os sinais que denunciam tudo à distância:

| Área | Sinal de IA | O que um humano faz em vez disso |
|---|---|---|
| Paleta | Gradiente roxo→azul, tudo neon | 1 cor de acento + neutros; contraste por tipografia e espaço |
| Tipografia | Inter/Roboto em tudo, títulos gigantes centrados | Par tipográfico com carácter (ex.: serif display + sans neutra) |
| Layout | Hero centrado + 3 cards iguais + secção de features | Grelha quebrada, assimetria, densidade variável, coluna editorial |
| Ícones | Emoji como ícone, outline genérico | 1 família de ícones, ou nenhum (usa números, setas, labels) |
| Sombras | `shadow-xl` em todos os elementos + `rounded-2xl` | Sombra só onde há elevação real; raios 2–6px |
| Copy | "Elevate", "seamless", "unlock", "in today's fast-paced world" | Nome concreto, número concreto, uma opinião, uma limitação |
| Texto | Parágrafos todos com o mesmo comprimento | Frases curtas. Depois uma longa que respira. Ritmo irregular |
| Estado vazio | Tudo perfeito, sem bordas, sem erros | Pequenas imperfeições: 1 elemento torto, grain, sublinhado à mão |
| Prova | "Trusted by thousands" sem fonte | Depoimento com nome, cargo, número, data |

---

## 2. Prompt-base (o "briefing de mundo")

Cola isto **antes** do pedido. É o bloco que mais muda o resultado.

```
CONTEXTO
Vais construir [o quê] para [quem, com um detalhe real sobre essa pessoa].
O objetivo desta peça é [uma frase, concreta, não "engajar"].

RESTRIÇÕES DE DESIGN (obrigatórias)
- Paleta: máximo 3 cores — 1 neutro escuro, 1 neutro claro, 1 acento. Sem gradientes.
- Tipografia: [display/ serif para títulos] + [sans para corpo]. Tamanhos: 14/16/20/32/56.
- Espaçamento: escala de 4px, mas permite 2 quebras de ritmo intencionais.
- Sem emojis como ícones. Sem glassmorphism. Sem sombras decorativas. Sem "rounded-2xl".
- Densidade: alterna blocos densos com blocos quase vazios. Não distribuas tudo por igual.

RESTRIÇÕES DE CONTEÚDO (obrigatórias)
- Zero clichés: elevate, seamless, unlock, revolutionise, "in today's world", "game-changer".
- Cada afirmação tem um número, um nome ou uma fonte. Se não tiveres, escreve "[dado a confirmar]".
- Inclui 1 limitação honesta do produto/serviço.
- Escreve como uma pessoa com opinião, não como um folheto.

RESTRIÇÕES TÉCNICAS
- [stack, ex.: HTML+CSS puro, sem frameworks]
- Código legível, sem dependências que eu não pedi.

PROIBIDO (anti-padrão de IA)
- Hero centrado com dois botões e screenshot em perspetiva.
- Grelha 3×2 de cards idênticos com ícone + título + 2 linhas.
- Secção de "features" onde todos os cards têm o mesmo peso visual.

ENTREGA
1. Primeiro, 3 direções criativas diferentes em 3 linhas cada (não construas ainda).
2. Espera que eu escolha.
3. Só depois constróis, com comentários no código a explicar as decisões não óbvias.
```

**Truque que quase ninguém usa:** obrigar a IA a **pedir referências** antes de criar.

```
Antes de escrever qualquer linha: faz-me 5 perguntas cujas respostas mudariam
radicalmente o resultado. Não avances sem elas.
```

---

## 3. Receitas por tipo de projeto

### 3.1 Site / landing page com cara de estúdio (não de template)

```
Age como diretor de arte de um estúdio pequeno (5 pessoas), não como agência.
Referências de linguagem visual: [site1], [site2]. NÃO copies; absorve a atitude.

Quero uma landing page de 1 página para [produto], com:
- Grelha editorial de 12 colunas, com 1 bloco que a quebra de propósito.
- Título em serif, corpo em sans, uma linha em itálico no meio do texto.
- Uma secção "prova" com números reais (mesmo que inventados, marca-os como exemplo).
- Fundo off-white (#FAF8F3), texto quase-preto (#141414), um acento [cor].
- Zero imagens de stock genéricas: usa espaço, tipografia, ou 1 ilustração SVG simples desenhada por ti.

Faz primeiro o wireframe em texto (bloco por bloco, com a hierarquia),
depois o HTML/CSS. Explica em 3 bullets onde está a imperfeição intencional.
```

### 3.2 Código que parece escrito por uma pessoa

```
Escreve este código como um sénior preguiçoso mas competente:
- Nomes de variáveis descritivos, sem abreviaturas de 2 letras.
- Zero comentários óbvios ("// incrementa i"). Só comentários que expliquem o "porquê".
- Sem over-engineering: sem camadas de abstração para 1 caso de uso.
- Trata 1 caso-limite que um júnior esqueceria (diz qual no fim).
- Se houver um trade-off, escolhe e justifica numa linha.

Depois faz uma segunda passagem como revisor hostil: aponta 3 coisas que
cheiram a "gerado por IA" e corrige-as.
```

### 3.3 Texto / conteúdo que não parece ChatGPT

```
Reescreve o texto abaixo com estas regras:
1. Começa no meio do assunto (nada de "Nos dias de hoje...").
2. Frases de comprimentos irregulares: 4 palavras, depois 22.
3. Um exemplo concreto por cada afirmação abstrata.
4. Uma opinião assumida ("na minha experiência..."), uma dúvida assumida.
5. Zero palavras desta lista: elevar, desbloquear, transformador, robusto,
   seamlessly, mergulhar, panorama, no mundo atual, game-changer.
6. Máximo 1 travessão em todo o texto. Máximo 1 "não só... mas também".
7. Termina com uma frase seca, não com um resumo motivacional.

Texto: """
[...]
"""
```

### 3.4 Identidade / marca

```
Cria 3 identidades distintas para [marca] — não 3 variações da mesma.
Cada uma deve partir de uma tensão real do negócio:
A) [premium vs acessível]  B) [técnico vs humano]  C) [local vs global]

Para cada: nome de fonte + porquê (1 linha), paleta com HEX e função de cada cor,
1 elemento gráfico recorrente, e o que a marca se RECUSA a fazer.
Sem apresentar mockups antes de eu escolher a direção.
```

### 3.5 Dashboard / app

```
Desenha o ecrã principal como se fosse para uso diário 8h/dia, não para um shot no Dribbble.
Prioridades: densidade de informação legível, hierarquia por peso tipográfico, zero decoração.
Sem cards flutuantes gigantes. Tabelas alinhadas à direita nos números.
Mostra também o estado vazio e o estado de erro — é aí que se vê se é design real.
```

---

## 4. Os 6 comandos de iteração (o verdadeiro segredo)

O primeiro output é sempre o mais "IA". Estes comandos moldam-no:

1. **Anti-slop**
   ```
   Lista as 5 coisas neste resultado que gritam "gerado por IA" e reescreve-as.
   Sê cruel. Não elogies nada.
   ```
2. **Remover simetria**
   ```
   Torna isto 20% menos simétrico: quebra a grelha num sítio, dá pesos
   diferentes a elementos que agora têm o mesmo peso.
   ```
3. **Adicionar especificidade**
   ```
   Substitui cada afirmação vaga por um número, nome ou data.
   Onde não souberes, escreve [VERIFICAR] em vez de inventar.
   ```
4. **Cortar 30%**
   ```
   Corta 30% do texto/elementos sem perder informação. O que sobra deve
   doer um pouco a remover.
   ```
5. **Teste do especialista**
   ```
   Um designer/engenheiro com 15 anos olha para isto e diz:
   "isto foi feito por alguém que não viveu o problema". Porquê? Corrige.
   ```
6. **Uma imperfeição**
   ```
   Adiciona exatamente 1 imperfeição intencional e explica-a.
   Só 1. Mais do que isso passa a ser um estilo, não uma escolha.
   ```

---

## 5. Checklist final (passa antes de mostrar a alguém)

- [ ] Se tapasse o logótipo, ainda saberia de quem é? Se não → falta personalidade.
- [ ] Existe pelo menos uma decisão que eu **não** pedi e que defendo?
- [ ] Há um número ou nome real na primeira dobra?
- [ ] Consegui apagar 20% sem perder nada? Se sim, apaga.
- [ ] A paleta cabe em 3 cores e 0 gradientes?
- [ ] O texto tem uma frase que só uma pessoa escreveria?
- [ ] Vi o estado vazio, o erro e o mobile?
- [ ] Há **exatamente uma** coisa imperfeita/assimétrica?

---

## 6. Aplicado ao teu caso: otimização de FPS

Prompt pronto para o tema do repositório (artigo + página):

```
Quero uma página de 1 ecrã sobre otimização de FPS, escrita por alguém que
passou horas a testar, não por um blogue de SEO.

Ângulo: [ex.: "o que dá FPS de verdade vs o que é placebo"].
Estrutura: 1 tabela comparativa (tweak → ganho médio → risco → veredicto),
1 parágrafo de opinião assumida, 1 aviso honesto sobre o que NÃO fazer.
Números marcados como [medido em: CPU/GPU/resolução] ou [estimativa].
Design: off-white, quase-preto, 1 acento; tabela densa; sem cards flutuantes;
título em serif, corpo em sans, mono nos valores numéricos.
Entrega HTML+CSS num ficheiro único, sem dependências.
```

---

### Regra de ouro

> Um humano não é original por ter ideias melhores — é original por **aceitar restrições** e **fazer escolhas que custam alguma coisa**.
> Dá restrições à IA, obriga-a a escolher, e o resultado deixa de parecer gerado.
