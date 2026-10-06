#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Verificador estatico para o FPS-Painel.bat.

Nao existe cmd.exe neste sandbox, entao este script faz a verificacao que da
para fazer fora do Windows:

  1. Codificacao UTF-8 valida, sem BOM, e terminacao de linha CRLF.
  2. Balanceamento de parenteses de blocos `if/for ( ... )`, ignorando texto
     entre aspas e caracteres escapados com ^.
  3. Todo `goto`/`call :` aponta para um rotulo que existe; rotulos nao
     referenciados sao avisados.
  4. Comentario `::` dentro de bloco (quebra o cmd).
  5. `!` solto em texto de echo (expansao retardada comeria o caractere).
  6. `%` solto em texto de echo (precisa de `%%`).
  7. Cauda incondicional: `if cond comando & outro` - o `&` escapa do if.
  8. As formulas matematicas passadas ao PowerShell sao reavaliadas aqui com
     valores de exemplo, para conferir a conta e a sintaxe.

Uso:  python3 tools/check_bat.py painel/FPS-Painel.bat
Saida: lista de problemas; exit code 1 se houver ERRO.
"""
import re
import sys

LABEL_RE = re.compile(r'^:([A-Za-z_][A-Za-z0-9_]*)\s*$')
GOTO_RE = re.compile(r'\bgoto\s+:?([A-Za-z_][A-Za-z0-9_]*)', re.I)
CALL_RE = re.compile(r'\bcall\s+:([A-Za-z_][A-Za-z0-9_]*)', re.I)


def strip_quotes_and_escapes(line):
    """Devolve a linha com o conteudo entre aspas e os chars escapados por ^
    removidos, para que parenteses 'de texto' nao sejam contados como bloco."""
    out = []
    i = 0
    in_quote = False
    n = len(line)
    while i < n:
        c = line[i]
        if c == '^' and i + 1 < n:          # caractere escapado: ignora os dois
            i += 2
            continue
        if c == '"':
            in_quote = not in_quote
            i += 1
            continue
        if not in_quote:
            out.append(c)
        i += 1
    return ''.join(out)


def check(path):
    erros = []
    avisos = []

    raw = open(path, 'rb').read()
    if raw.startswith(b'\xef\xbb\xbf'):
        erros.append('linha 0: arquivo tem BOM UTF-8; cmd.exe engasga na 1a linha')
    try:
        text = raw.decode('utf-8')
    except UnicodeDecodeError as e:
        print('ERRO FATAL: nao e UTF-8 valido: %s' % e)
        return 1
    if b'\r\n' not in raw:
        erros.append('arquivo sem CRLF; .bat com LF puro pode nao achar rotulos')
    bare_lf = len(re.findall(rb'(?<!\r)\n', raw))
    if bare_lf:
        erros.append('%d linha(s) com LF solto (esperado CRLF em todo o arquivo)' % bare_lf)

    lines = text.split('\r\n') if '\r\n' in text else text.split('\n')
    if lines and lines[-1] == '':
        lines.pop()

    labels = {}
    usados = set()
    depth = 0
    stack = []

    for idx, raw_line in enumerate(lines, start=1):
        line = raw_line.rstrip('\n')
        if len(line) > 8191:
            erros.append('linha %d: maior que 8191 caracteres' % idx)

        m = LABEL_RE.match(line)
        if m:
            name = m.group(1).lower()
            if name in labels:
                erros.append('linha %d: rotulo duplicado :%s (1a ocorrencia linha %d)'
                             % (idx, name, labels[name]))
            labels[name] = idx
            continue

        stripped = line.lstrip()
        code = strip_quotes_and_escapes(line)
        code_stripped = code.lstrip()

        is_comment = stripped.startswith('::') or re.match(r'(?i)^rem(\s|$)', stripped)
        if is_comment:
            if depth > 0 and stripped.startswith('::'):
                erros.append('linha %d: comentario :: dentro de bloco ( ) - use REM' % idx)
            continue

        for g in GOTO_RE.finditer(code):
            usados.add(g.group(1).lower())
        for c in CALL_RE.finditer(code):
            usados.add(c.group(1).lower())

        # balanceamento de parenteses do codigo (fora de aspas)
        for ch in code:
            if ch == '(':
                depth += 1
                stack.append(idx)
            elif ch == ')':
                depth -= 1
                if stack:
                    stack.pop()
                if depth < 0:
                    erros.append('linha %d: ) sem ( correspondente' % idx)
                    depth = 0

        low = code_stripped.lower()

        # '!' solto em echo: expansao retardada apaga o caractere
        # !VAR! e expansao retardada legitima; ! sozinho some do texto
        if low.startswith('echo'):
            resto = re.sub(r'![A-Za-z_][A-Za-z0-9_]*(?::[^!]*)?!', '', line)
            if '!' in resto:
                avisos.append('linha %d: "!" solto em echo some com EnableDelayedExpansion' % idx)

        # '%' solto em echo precisa ser '%%'
        if low.startswith('echo'):
            corpo = line[line.lower().find('echo') + 4:]
            # %VAR%, %~dp0 e %% sao legitimos; % sozinho nao e
            corpo_sem_var = re.sub(r'%(?:~[A-Za-z]*[0-9]*|[A-Za-z_][A-Za-z0-9_]*%|[0-9*]|%)', '', corpo)
            if '%' in corpo_sem_var:
                avisos.append('linha %d: "%%" solto em echo - precisa de %%%%' % idx)

        # cauda incondicional: if sem ( ) mas com &
        # & de redirecionamento (2>&1, >&2, <&0) nao conta como separador
        sem_redir = re.sub(r'[<>]?\d*>&\d*', '', code)
        if re.match(r'(?i)^if\s', code_stripped) and '(' not in code and \
                re.search(r'(?<!\^)&(?!&)', sem_redir):
            erros.append('linha %d: "&" fora do escopo do if - o que vem depois '
                         'executa sempre; envolva em ( )' % idx)
        if re.match(r'(?i)^for\s', code_stripped) and ' do ' in low \
                and '(' not in sem_redir.split(' do ', 1)[-1] \
                and re.search(r'(?<!\^)&(?!&)', sem_redir.split(' do ', 1)[-1]):
            erros.append('linha %d: "&" fora do escopo do for/do' % idx)

        # parenteses em texto de echo dentro de bloco
        if low.startswith('echo') and depth > 0:
            corpo = code[code.lower().find('echo') + 4:]
            if '(' in corpo or ')' in corpo:
                erros.append('linha %d: parenteses em texto de echo dentro de bloco' % idx)

    if depth != 0:
        erros.append('fim do arquivo: %d bloco(s) ( nao fechado(s), abertos na(s) linha(s) %s'
                     % (depth, stack[-3:] or stack))

    for g in sorted(usados):
        if g == 'eof':
            continue
        if g not in labels:
            erros.append('goto/call para :%s - rotulo nao existe' % g)
    for name, ln in labels.items():
        if name not in usados:
            avisos.append('linha %d: rotulo :%s nunca referenciado' % (ln, name))

    print('== %s ==' % path)
    print('linhas analisadas ....... %d' % len(lines))
    print('rotulos definidos ....... %d' % len(labels))
    print('rotulos referenciados ... %d' % len(usados - {'eof'}))
    print('')
    if avisos:
        print('-- AVISOS (%d) --' % len(avisos))
        for a in avisos:
            print('   [aviso] %s' % a)
        print('')
    if erros:
        print('-- ERROS (%d) --' % len(erros))
        for e in erros:
            print('   [ERRO] %s' % e)
        print('')
        print('RESULTADO: FALHOU')
        return 1
    print('RESULTADO: OK - nenhum erro de sintaxe de bloco, rotulo ou citacao')
    return 0


def check_math(path):
    """Reavalia no Python as formulas que o .bat entrega ao PowerShell, com
    valores de exemplo, para conferir a conta e a sintaxe da expressao."""
    text = open(path, encoding='utf-8').read()
    brutas = re.findall(r'\[math\]::Round\(([^,]+),\s*(\d)\)', text)
    if not brutas:
        print('ERRO: nenhuma formula [math]::Round encontrada em %s' % path)
        return 1

    amostras = {'SB': 85, 'CM_B': 30, 'CM_A': 24,
                'ATUAL': 90, 'PCT': -10,
                'DPI': 800, 'SG': 50}
    esperado = {
        'SB*(CM_B/CM_A)': 106.25,      # sensi do jogo B igualada ao jogo A
        'ATUAL*(1+PCT/100)': 81.0,     # ajuste fino de -10 porcento
        'DPI*SG': 40000.0,             # eDPI
    }

    print('== formulas de sensibilidade em %s ==' % path)
    falhas = 0
    vistas = set()
    for expr, casas in brutas:
        py = re.sub(r'[%!]([A-Za-z_][A-Za-z0-9_]*)[%!]', r'\1', expr).strip()
        vistas.add(py)
        if py not in esperado:
            print('   [ERRO] formula nao esperada: %s' % py)
            falhas += 1
            continue
        usadas = {k: v for k, v in amostras.items() if re.search(r'\b%s\b' % k, py)}
        val = round(eval(py, {'__builtins__': {}}, amostras), int(casas))
        exp = esperado[py]
        ok = abs(val - exp) < 1e-9
        print('   %-20s casas=%s amostras=%s -> %-9s esperado %-9s %s'
              % (py, casas, usadas, val, exp, 'OK' if ok else 'DIVERGE'))
        if not ok:
            falhas += 1

    for falta in sorted(set(esperado) - vistas):
        print('   [ERRO] formula esperada nao esta no arquivo: %s' % falta)
        falhas += 1

    print('')
    print('RESULTADO: %s' % ('OK - as 3 formulas conferem' if not falhas
                             else 'FALHOU - %d divergencia(s)' % falhas))
    return 1 if falhas else 0


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--math':
        alvo = sys.argv[2] if len(sys.argv) > 2 else 'painel/FPS-Painel.bat'
        sys.exit(check_math(alvo))
    alvo = sys.argv[1] if len(sys.argv) > 1 else 'painel/FPS-Painel.bat'
    sys.exit(check(alvo))
