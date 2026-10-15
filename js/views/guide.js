// Vista "Guia": conteúdo estático de treino, setup e preparação competitiva.
export function buildGuide(root) {
  root.innerHTML = `
  <section class="card">
    <h3>O que é o Range Lab — e o que não é</h3>
    <p>
      O Range Lab é um <b>ginásio de mira independente</b>. Corre inteiramente no teu browser: lê o rato
      e o teclado, desenha num canvas e guarda as tuas estatísticas no <code>localStorage</code> deste
      browser. <b>Não lê memória de jogos, não injeta código, não se liga a servidores, não altera
      ficheiros de jogos e não corre em segundo plano enquanto jogas.</b>
    </p>
    <div class="okbox">
      <p style="margin:0">
        <b>Porque é que isto interessa para campeonatos:</b> tudo o que ganhas aqui é memória muscular e
        leitura de jogo, que vivem no teu cérebro e na tua mão — não num programa. Passa em qualquer
        verificação de anti-cheat, em qualquer inspeção de dispositivo e em qualquer revisão de replay,
        porque não há nada para encontrar.
      </p>
    </div>
    <div class="warnbox" style="margin-top:10px">
      <p style="margin:0">
        <b>O caminho que não vamos seguir:</b> aim assist externo, remoção de recoil por software, macros
        de spray ou qualquer coisa desenhada para "não parecer hack". Isso é batota: quebra os termos de
        serviço (ban de conta e, em muitos casos, de dispositivo), desqualifica-te de torneios com
        devolução de prémios, e rouba a competição a quem treina a sério. Não há versão "segura" disto —
        há apenas a versão em que ainda não foste apanhado.
      </p>
    </div>
  </section>

  <section class="card">
    <h3>Calibrar o treinador ao TEU spray real</h3>
    <p>
      Os padrões de recoil incluídos são <b>arquétipos genéricos</b> (SMG, rifle, DMR, LMG), não dados de
      nenhum jogo. Para o treino transferir-se, tens de os ajustar ao que vês no jogo:
    </p>
    <ol>
      <li>Entra no <b>campo de treino / modo de prática</b> do jogo e aponta para uma parede a ~10 m.</li>
      <li>Esvazia um carregador <b>sem compensar nada</b> e olha para a forma das marcas: sobe muito nos
        primeiros tiros? Deriva para a direita e depois volta? Abre muito no fim?</li>
      <li>Voltas ao Range Lab, escolhes o perfil mais parecido e ajustas <b>Subida</b>, <b>Deriva</b>,
        <b>Dispersão</b> e <b>Cadência</b> até o padrão vermelho (o fantasma) ter a mesma forma que viste.</li>
      <li>Grava esses valores — ficam guardados — e treina contra eles.</li>
    </ol>
    <p>
      Deixa a opção <b>"Padrão aleatório" desligada</b> enquanto memorizas: assim o padrão é idêntico em
      todas as séries e o teu cérebro consegue automatizá-lo. Liga-a só quando já fechas o grupo de forma
      consistente, para não ficares dependente de um padrão decorado.
    </p>
  </section>

  <section class="card">
    <h3>Rotina de 15 minutos (antes de cada sessão)</h3>
    <p>Usa o botão <b>"Rotina diária 15 min"</b> no topo, ou segue esta ordem:</p>
    <ol>
      <li><b>Reação</b> — 2 min. Aquece o sistema nervoso; também te avisa se estiveres lento por cansaço.</li>
      <li><b>Tracking</b> — 3 min. Começa em "Strafe" a 0.8× e sobe 0.1× até falhares ~30% do tempo.</li>
      <li><b>Flicks</b> — 3 min. Precisão acima de 95% primeiro; só depois reduces o raio do alvo.</li>
      <li><b>Grelha</b> — 3 min. Com 2 alvos em simultâneo para treinar troca e re-centragem.</li>
      <li><b>Controlo de Recoil</b> — 4 min. Rajadas curtas → carregador cheio. É o prato principal.</li>
    </ol>
    <p style="margin-bottom:0">
      <b>Consistência bate volume:</b> 15 minutos todos os dias durante 3 semanas supera 3 horas num só dia.
      E para antes de doer — tendinite no pulso acaba carreiras mais depressa do que má mira.
    </p>
  </section>

  <section class="card">
    <h3>Fundamentos que valem mais do que qualquer reflexo</h3>
    <h4>1. Crosshair placement</h4>
    <p>
      Mantém a mira sempre à altura da cabeça e pré-apontada para onde o inimigo vai aparecer. Isto elimina
      a maior parte do tempo de reação: não precisas de flickar se já estás lá. É a diferença nº 1 entre
      jogadores medianos e jogadores de torneio.
    </p>
    <h4>2. Disciplina de rajada</h4>
    <p>
      O spray completo só é bom a curta distância. A médio/longo alcance, rajadas de 3–5 tiros com
      re-centragem entre elas dão muito mais dano do que um carregador inteiro espalhado. Treina no modo
      Recoil com "Tiros por série" = 5 até o grupo ficar fechado, e só depois sobe.
    </p>
    <h4>3. Movimento e precisão</h4>
    <p>
      Em quase todos os FPS mobile/emulador, disparar em movimento abre muito o spread. Practica parar
      (ou counter-strafe) uma fração de segundo antes do tiro. Um duelo ganha-se por quem estabiliza primeiro.
    </p>
    <h4>4. ADS vs hip fire</h4>
    <p>
      Hip fire é para 0–5 m; ADS é para o resto. O erro clássico é ADS a 2 m (perdes tempo) ou hip a 20 m
      (perdes balas). Define a tua distância de transição e respeita-a.
    </p>
    <h4>5. Ler o adversário</h4>
    <p>
      O modo Tracking "Errático" treina exatamente isto: seguir alguém que muda de direção. Em jogo, observa
      o padrão de strafe do inimigo durante 1–2 segundos antes de comprometer o duelo.
    </p>
  </section>

  <section class="card">
    <h3>Setup de PC e emulador (ganho grátis de desempenho)</h3>
    <ul>
      <li><b>Desliga a aceleração do rato</b> no sistema operativo ("Aprimorar precisão do ponteiro" no
        Windows). É o assassino nº 1 de memória muscular: o mesmo movimento físico dá deslocações diferentes.</li>
      <li><b>FPS estável &gt; FPS máximo.</b> 90 fps constantes valem mais do que picos de 144 com quedas.
        Limita os fps um pouco abaixo do teu mínimo habitual.</li>
      <li><b>Latência de entrada:</b> desliga V-Sync, usa modo de janela sem bordas ou ecrã inteiro, e fecha
        overlays desnecessários.</li>
      <li><b>Polling rate do rato:</b> 500–1000 Hz. Acima disso, em emulador, raramente ajuda e pode
        introduzir micro-stutter.</li>
      <li><b>DPI + sensibilidade:</b> escolhe um DPI fixo (400–1600) e ajusta a sensibilidade no jogo.
        Mira com o pulso para micro-ajustes e com o antebraço para flicks longos.</li>
      <li><b>Um valor, sempre o mesmo.</b> Aponta o teu cm/360 (vês o cálculo em Definições) e mantém-no
        igual entre o Range Lab e o jogo. Mudar de sensibilidade a meio da semana deita fora o treino.</li>
      <li><b>Mapeamento de teclas no emulador:</b> coloca as teclas onde os dedos descansam naturalmente,
        não onde o preset as pôs. Muda uma de cada vez e dá-lhe 3 dias.</li>
    </ul>
  </section>

  <section class="card">
    <h3>Preparação para campeonatos</h3>
    <h4>Antes de te inscreveres</h4>
    <ul>
      <li>Lê o <b>regulamento completo</b>: dispositivos permitidos, emulador vs mobile, macros, periféricos,
        horário, formato e política de anti-cheat.</li>
      <li>Confirma se a tua conta cumpre os requisitos (nível, verificação de identidade, região).</li>
      <li>Se o torneio for em LAN ou com verificação, informa-te sobre o que é inspecionado.</li>
    </ul>
    <h4>Na semana do torneio</h4>
    <ul>
      <li>Reduz o volume de jogo e aumenta a qualidade: 3 blocos de 45 min com pausa, em vez de 8 horas seguidas.</li>
      <li>Dorme. Reflexos caem 10–20% com uma noite mal dormida — mais do que qualquer ajuste de sensibilidade.</li>
      <li>Nada de mudanças de setup nas 72 h anteriores. Não mudes sensibilidade, não mudes teclas, não mudes de rato.</li>
      <li>Faz a rotina de 15 min antes de cada ronda do torneio, não apenas no primeiro dia.</li>
    </ul>
    <h4>Revisão de jogo (o multiplicador escondido)</h4>
    <ul>
      <li>Grava as tuas partidas e revê <b>as mortes</b>, não as kills. Pergunta: onde estava a minha mira
        antes do contacto? Estava a mover-me? Tinha informação?</li>
      <li>Aponta 2 erros máximos por sessão e treina só esses no Range Lab no dia seguinte.</li>
      <li>Regista por escrito. Um caderno de erros vale mais do que 20 horas de jogo sem reflexão.</li>
    </ul>
    <h4>Conduta</h4>
    <p style="margin-bottom:0">
      Uma reputação limpa é um ativo de carreira. Jogadores que ganham de forma limpa recebem convites,
      equipas e patrocínios; jogadores apanhados a fazer batota ficam marcados para sempre — e os registos
      de ban são públicos e permanentes.
    </p>
  </section>

  <section class="card">
    <h3>Atalhos de teclado</h3>
    <ul>
      <li><kbd>1</kbd>–<kbd>5</kbd> — mudar de modo de treino</li>
      <li><kbd>R</kbd> — reiniciar a série no modo atual</li>
      <li><kbd>ESC</kbd> — libertar o rato (termina a série em modos com rato capturado)</li>
      <li><kbd>Clique esquerdo</kbd> — disparar / acertar</li>
    </ul>
    <p style="margin-bottom:0;color:var(--txt-3);font-size:12px">
      Todos os dados ficam neste browser. Usa Definições → Exportar JSON para guardar uma cópia dos teus
      registos.
    </p>
  </section>
  `;
}
