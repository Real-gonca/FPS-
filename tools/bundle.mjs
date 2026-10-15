#!/usr/bin/env node
/**
 * Bundler mínimo do Range Lab — zero dependências.
 *
 *   node tools/bundle.mjs
 *
 * Junta os 16 módulos ES num único ficheiro (js/app.bundle.js) por duas razões:
 *
 *  1. Carregamento em cascata: com módulos separados o browser faz 17 pedidos
 *     sequenciais (cada import só é pedido depois do pai ser analisado). Num
 *     preview proxyizado isso demorava ~10 s.
 *  2. Cache: ficheiros separados ficam em cache do browser e já serviram uma
 *     versão partida depois de corrigida. O bundle é referenciado no index.html
 *     com ?v=<hash do código>, portanto qualquer alteração gera um URL novo e
 *     é impossível servir código velho.
 *
 * Os módulos em js/ continuam a ser a fonte de verdade — usados diretamente
 * pelos testes. Este ficheiro é gerado; não o edites à mão.
 */
import { readFileSync, writeFileSync, existsSync } from 'fs';
import path from 'path';
import crypto from 'crypto';
import { fileURLToPath } from 'url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const ENTRY = path.join(ROOT, 'js', 'app.js');
const OUT = path.join(ROOT, 'js', 'app.bundle.js');
const HTML = path.join(ROOT, 'index.html');

const IMPORT_RE = /^[ \t]*import\s+(?:[\s\S]*?\s+from\s+)?['"][^'"]+['"]\s*;?[ \t]*$/gm;
const EXPORT_BLOCK_RE = /^[ \t]*export\s*\{[\s\S]*?\}\s*;?[ \t]*$/gm;
const EXPORT_PREFIX_RE = /^([ \t]*)export\s+(?=(?:async\s+)?(?:function|class|const|let|var)\b)/gm;
const TOP_DECL_RE = /^(?:const|let|var|function|class|async function)\s+([A-Za-z0-9_$]+)/gm;

function resolve(spec, fromFile) {
  const p = path.resolve(path.dirname(fromFile), spec);
  if (!existsSync(p)) {
    throw new Error(`não encontro "${spec}" importado por ${path.relative(ROOT, fromFile)}`);
  }
  return p;
}

// ---------------------------------------------------------------- ordem
const order = [];
const seen = new Set();
let importCount = 0;

function visit(file) {
  if (seen.has(file)) return;
  seen.add(file);
  const src = readFileSync(file, 'utf8');
  for (const m of src.matchAll(/import\s+(?:[\s\S]*?\s+from\s+)?['"]([^'"]+)['"]\s*;?/g)) {
    importCount++;
    if (!m[1].startsWith('.')) continue; // só relativos; não há pacotes externos
    visit(resolve(m[1], file));
  }
  order.push(file); // pós-ordem: dependências primeiro
}

visit(ENTRY);

// ------------------------------------------------------------- transformação
const chunks = [];
const declared = new Map();

for (const file of order) {
  const rel = path.relative(ROOT, file).split(path.sep).join('/');
  let src = readFileSync(file, 'utf8');

  src = src.replace(IMPORT_RE, '');
  src = src.replace(EXPORT_BLOCK_RE, '');
  src = src.replace(EXPORT_PREFIX_RE, '$1');

  if (/^[ \t]*(?:import|export)\s/m.test(src)) {
    const line = src.split('\n').findIndex((l) => /^[ \t]*(?:import|export)\s/.test(l)) + 1;
    throw new Error(`${rel}: ficou import/export por remover (linha ~${line})`);
  }

  // duas declarações de topo com o mesmo nome rebentariam o bundle
  src.replace(TOP_DECL_RE, (_all, name) => {
    const prev = declared.get(name);
    if (prev && prev !== rel) {
      throw new Error(`colisão de nomes de topo: "${name}" em ${prev} e em ${rel}`);
    }
    declared.set(name, rel);
    return _all;
  });

  chunks.push({ rel, src: src.replace(/^\n+/, '').trimEnd() });
}

const body = chunks
  .map((c) => `// ${'─'.repeat(78)}\n// ${c.rel}\n// ${'─'.repeat(78)}\n\n${c.src}\n`)
  .join('\n');

const code = chunks.map((c) => c.src).join('\n');
const hash = crypto.createHash('sha256').update(code).digest('hex').slice(0, 12);

const header = `/* Range Lab — bundle gerado por tools/bundle.mjs. NÃO EDITAR.
 * Fonte: ${chunks.length} módulos em js/ · hash ${hash}
 * Para alterar código: edita js/ e corre "npm run build". */\n\n`;

const out = header + body;
writeFileSync(OUT, out);

// ------------------------------------------------- versionar o URL no HTML
let html = readFileSync(HTML, 'utf8');
const ref = `js/app.bundle.js?v=${hash}`;
if (!/js\/app\.bundle\.js\?v=[a-f0-9]+/.test(html)) {
  throw new Error('index.html não referencia js/app.bundle.js?v=<hash> — não posso versionar o URL');
}
html = html.replace(/js\/app\.bundle\.js\?v=[a-f0-9]+/, ref);
writeFileSync(HTML, html);

const kb = (Buffer.byteLength(out) / 1024).toFixed(1);
console.log(`bundle: js/app.bundle.js · ${chunks.length} módulos · ${importCount} imports resolvidos · ${kb} kB · v=${hash}`);
